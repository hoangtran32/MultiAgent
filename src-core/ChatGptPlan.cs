using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MultiAgentHub.Core;

/// <summary>Official Sign in with ChatGPT public-client flow and Responses SSE. No app/profile scraping.</summary>
public sealed class ChatGptPlan(ChatGptPlanStore store)
{
    private const string Auth = "https://auth.openai.com", Resource = "https://api.openai.com/v1";
    private const string Scope = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    public ChatGptPlanStore Store { get; } = store;
    public void Validate(AgentConfig agent)
    {
        Catalog.Validate(agent);
        if (agent.PlanAccountId.Length == 0 || agent.Model.Length == 0) throw new InvalidOperationException("Đăng nhập ChatGPT và chọn model trong Tài khoản ChatGPT • API.");
        var session = Store.Load(agent.PlanAccountId);
        if (session.AccessToken.Length == 0) throw new InvalidOperationException("Tài khoản ChatGPT đã đăng xuất. Đăng nhập lại trong Tài khoản ChatGPT • API.");
        if (!session.PlanEnabled) throw new InvalidOperationException("Chưa cấp quyền Using ChatGPT plan. Mở tài khoản và cho phép dùng gói ChatGPT.");
    }
    private static string RandomValue() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public async Task<ChatGptPlanAccount> SignIn(string? accountId, string label, Action<Uri> openBrowser, CancellationToken token)
    {
        if (label.Length > 80 || label.Any(char.IsControl)) throw new ArgumentException("Tên tài khoản tối đa 80 ký tự, không có ký tự điều khiển.");
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token); attempt.CancelAfter(TimeSpan.FromMinutes(5)); token = attempt.Token;
        var previous = accountId == null ? null : Store.Load(accountId);
        string state = RandomValue(), nonce = RandomValue(), verifier = RandomValue();
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(o => { o.Listen(IPAddress.Loopback, 0); o.Limits.MaxRequestLineSize = 16384; });
        await using var callback = builder.Build();
        var result = new TaskCompletionSource<Dictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int accepted = 0;
        callback.MapGet("/auth/callback", async context => {
            context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) || context.Request.Host.Host != "127.0.0.1" || context.Request.Headers.ContainsKey("Origin")) { context.Response.StatusCode = 403; return; }
            var query = context.Request.Query;
            if (query.Any(q => q.Value.Count != 1) || !query.TryGetValue("state", out var returned) || returned.ToString() != state || Interlocked.CompareExchange(ref accepted, 1, 0) != 0) { context.Response.StatusCode = 400; return; }
            await context.Response.WriteAsync("Authorization received. Return to Multi-Agent Hub to see the result.", token);
            result.TrySetResult(query.ToDictionary(q => q.Key, q => q.Value.ToString()));
        });
        await callback.StartAsync(token);
        string redirect = callback.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/') + "/auth/callback";
        var parameters = new Dictionary<string, string> {
            ["client_id"] = previous?.ClientId ?? "dynamic_agent_client", ["ext_agent_host_id"] = Store.HostId,
            ["response_type"] = "code", ["redirect_uri"] = redirect, ["scope"] = Scope, ["resource"] = Resource,
            ["state"] = state, ["nonce"] = nonce, ["code_challenge_method"] = "S256", ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) };
        if (previous == null) parameters["agent_name_hint"] = "Multi-Agent Hub";
        else if (previous.IdToken.Length > 0) parameters["id_token_hint"] = previous.IdToken;
        if (previous is { PlanEnabled: false, AccessToken.Length: > 0 }) parameters["prompt"] = "consent";
        try {
            try { openBrowser(new Uri(Auth + "/api/accounts/authorize?" + string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))))); }
            catch { throw new InvalidOperationException("Không mở được trình duyệt đăng nhập ChatGPT. Kiểm tra trình duyệt mặc định rồi thử lại."); }
            var query = await result.Task.WaitAsync(token);
            if (query.ContainsKey("error")) throw new InvalidOperationException("Đăng nhập ChatGPT bị từ chối hoặc đã hủy. Phiên đang lưu được giữ nguyên.");
            string clientId = query.GetValueOrDefault("client_id") ?? previous?.ClientId ?? "";
            if (clientId.Length is 0 or > 200 || clientId == "dynamic_agent_client" || clientId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')) || (previous != null && clientId != previous.ClientId) || !query.TryGetValue("code", out var code) || code.Length == 0)
                throw new InvalidDataException("Đăng ký ChatGPT chưa hoàn tất hoặc client không khớp. Hãy đăng nhập lại.");
            using var tokens = await Token(new() { ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["resource"] = Resource }, token);
            var identity = await Identity(tokens.RootElement.Str("id_token"), clientId, nonce, token);
            if (previous != null && identity.Subject != previous.Subject) throw new InvalidDataException("Đăng nhập khác tài khoản đã chọn. Thêm tài khoản riêng để sử dụng.");
            var session = previous ?? new ChatGptPlanStore.Session();
            session.ClientId = clientId; session.Subject = identity.Subject; session.Label = label.Trim().Length > 0 ? label.Trim() : identity.Name;
            ApplyTokens(session, tokens.RootElement, true);
            using var accountLock = await Store.Lock(session.Id, token); Store.Save(session);
            return session.Info;
        }
        finally { await callback.StopAsync(CancellationToken.None); }
    }
    private static async Task<(string Subject, string Name)> Identity(string idToken, string clientId, string nonce, CancellationToken token)
    {
        // Metadata and keys are fetched from the fixed issuer, with redirects disabled.
        using var keysRequest = new HttpRequestMessage(HttpMethod.Get, Auth + "/.well-known/jwks.json");
        using var keys = await Send(keysRequest, token);
        return await VerifyIdentity(idToken, clientId, nonce, keys.RootElement.GetRawText());
    }
    internal static async Task<(string Subject, string Name)> VerifyIdentity(string idToken, string clientId, string nonce, string jwks)
    {
        var parameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = Auth, ValidateAudience = true, ValidAudience = clientId,
            ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true, ValidateIssuerSigningKey = true,
            IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(), ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.FromSeconds(5) };
        var validated = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, parameters);
        if (!validated.IsValid || validated.SecurityToken is not JsonWebToken jwt || jwt.Subject.Length == 0 || !jwt.TryGetClaim("iat", out _) || !jwt.TryGetClaim("nonce", out var claim) || claim.Value != nonce)
            throw new InvalidDataException("Không xác minh được chữ ký/danh tính đăng nhập ChatGPT. Phiên cũ được giữ nguyên.");
        string name = jwt.TryGetClaim("email", out var email) ? email.Value : "ChatGPT";
        return (jwt.Subject, name);
    }
    private static void ApplyTokens(ChatGptPlanStore.Session session, JsonElement tokens, bool initial)
    {
        string access = tokens.Str("access_token"), refresh = tokens.Str("refresh_token");
        if (tokens.Str("token_type") is not ("Bearer" or "bearer") || access.Length == 0 || refresh.Length == 0 || !tokens.TryGetProperty("expires_in", out var expiry) || !expiry.TryGetInt32(out int seconds) || seconds <= 0)
            throw new InvalidDataException("Phản hồi OAuth ChatGPT thiếu token hoặc hạn sử dụng.");
        if (initial && tokens.Str("id_token").Length == 0) throw new InvalidDataException("Thiếu ID token ChatGPT.");
        session.AccessToken = access; session.RefreshToken = refresh;
        if (tokens.Str("id_token").Length > 0) session.IdToken = tokens.Str("id_token");
        if (tokens.TryGetProperty("scope", out _)) session.Scopes = tokens.Str("scope").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        else if (initial) session.Scopes = [];
        session.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
    }
    private async Task<string> AccessToken(string accountId, CancellationToken token)
    {
        using var accountLock = await Store.Lock(accountId, token);
        var session = Store.Load(accountId);
        if (session.AccessToken.Length == 0 || !session.PlanEnabled) throw new InvalidOperationException("Đăng nhập và cho phép dùng gói ChatGPT trước khi gọi API.");
        if (session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return session.AccessToken;
        try { using var renewed = await Token(new() { ["grant_type"] = "refresh_token", ["client_id"] = session.ClientId, ["refresh_token"] = session.RefreshToken, ["resource"] = Resource }, token); ApplyTokens(session, renewed.RootElement, false); Store.Save(session); }
        catch (ChatGptPlanException error) when (error.Code is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused") { Store.ClearTokens(session); throw new InvalidOperationException("Phiên ChatGPT hết hiệu lực. Đăng nhập lại tài khoản này."); }
        if (!session.PlanEnabled) throw new InvalidOperationException("Quyền dùng gói ChatGPT đã bị thu hồi.");
        return session.AccessToken;
    }
    public async Task<bool> SignOut(string accountId, CancellationToken token)
    {
        using var accountLock = await Store.Lock(accountId, token); var session = Store.Load(accountId); bool revoked = session.RefreshToken.Length == 0;
        try {
            if (!revoked) { using var request = new HttpRequestMessage(HttpMethod.Post, Auth + "/api/accounts/oauth/revoke") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = session.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = session.ClientId }) }; using var response = await Client.SendAsync(request, token); revoked = response.StatusCode == HttpStatusCode.OK; }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
        finally { Store.ClearTokens(session); }
        return revoked;
    }
    private static Task<JsonDocument> Token(Dictionary<string, string> form, CancellationToken token) => TokenCore(form, token);
    private static async Task<JsonDocument> TokenCore(Dictionary<string, string> form, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Auth + "/api/accounts/oauth/token") { Content = new FormUrlEncodedContent(form) };
        return await Send(request, token);
    }
    private static HttpRequestMessage Authorized(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, Resource + path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access); return request;
    }
    private static async Task<JsonDocument> Send(HttpRequestMessage request, CancellationToken token)
    {
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        string body = await ReadBounded(response.Content, token);
        if (!response.IsSuccessStatusCode) throw ChatGptPlanException.From(response, body);
        return JsonDocument.Parse(body);
    }
    private static async Task<string> ReadBounded(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token); using var buffer = new MemoryStream(); var bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, token)) > 0) { if (buffer.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("Phản hồi ChatGPT quá lớn."); buffer.Write(bytes, 0, count); }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    public async Task<IReadOnlyList<Choice>> Models(string accountId, CancellationToken token)
    {
        string access = await AccessToken(accountId, token); using var request = Authorized(HttpMethod.Get, "/models", access); using var catalog = await Send(request, token);
        if (!catalog.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) throw new InvalidDataException("API không trả danh sách model ChatGPT theo tài khoản.");
        return models.EnumerateArray().Where(m => m.Str("visibility") == "list" && m.Str("slug").Length > 0).Select(m => new Choice(m.Str("slug"), m.Str("display_name").Length > 0 ? m.Str("display_name") : m.Str("slug"))).DistinctBy(m => m.Id).ToArray();
    }
    public async Task<string> Execute(AgentConfig agent, string prompt, Action<string> text, CancellationToken token)
    {
        Validate(agent); string access = await AccessToken(agent.PlanAccountId, token);
        using var request = Authorized(HttpMethod.Post, "/responses", access);
        request.Content = new StringContent(JsonSerializer.Serialize(new { model = agent.Model, input = ChatAttachments.ResponsesInput(prompt, agent.RuntimeAttachments), store = false, stream = true }), Encoding.UTF8, "application/json");
        // The caller owns the exchange timeout. Do not terminate a healthy stream on HttpClient's short metadata timeout.
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw ChatGptPlanException.From(response, await ReadBounded(response.Content, token));
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") throw new InvalidDataException("ChatGPT API không trả luồng SSE.");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        return await ChatGptPlanStream.Read(reader, text, token);
    }
}

public sealed class ChatGptPlanException : Exception
{
    public string Code { get; }
    public ChatGptPlanException(string code, string requestId = "", int? status = null) : base(MessageFor(code) + (status == null ? "" : $" HTTP {status}.") + (requestId.Length == 0 ? "" : " Request ID: " + requestId)) { Code = code; }
    private static string MessageFor(string code) => code switch {
        "subscription_sharing_usage_limit_exceeded" => "Đã chạm hạn mức dùng gói ChatGPT của ứng dụng/tài khoản. Xem ChatGPT Settings → Usage; không tự chuyển sang API tính phí.",
        "subscription_sharing_user_not_eligible" => "Tài khoản/workspace này chưa được phép dùng gói ChatGPT qua API.",
        "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable" => "Dịch vụ chưa kiểm tra được quyền/hạn mức ChatGPT. Giữ phiên và thử lại sau.",
        "subscription_sharing_unsupported_capability" => "Model hoặc tính năng không được hỗ trợ trên API dùng gói ChatGPT.",
        _ => "API dùng gói ChatGPT chưa hoàn tất yêu cầu. Mã: " + code + ". Kiểm tra quyền đăng nhập, model và trạng thái dịch vụ." };
    internal static ChatGptPlanException From(HttpResponseMessage response, string body)
    {
        string code = "admission_error";
        try { using var doc = JsonDocument.Parse(body); var root = doc.RootElement; code = root.Str("error"); if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object) code = error.Str("code"); if (code.Length == 0) code = "admission_error"; } catch (JsonException) { }
        // Never include provider text that may echo prompts or credentials.
        if (code.Length > 100 || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) code = "upstream_error";
        string requestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() ?? "" : "";
        return new(code, requestId.Length < 200 && !requestId.Any(char.IsControl) ? requestId : "", (int)response.StatusCode);
    }
}
