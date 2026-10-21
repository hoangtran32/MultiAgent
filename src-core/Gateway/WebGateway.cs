using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MultiAgentHub.Core;

public sealed record GatewayModel(string Id, string Name, string AccountId, string AccountName, string BindingId, string WebModelId, string Protocol);

/// <summary>Loopback OpenAI text gateway. Authentication to websites stays in the user's explicitly linked browser document.</summary>
public sealed class WebGateway(GatewayAccounts accounts, BrowserChatBridge bridge) : IDisposable
{
    private WebApplication? server;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    public GatewayAccounts Accounts { get; } = accounts;
    public bool Running => server != null;
    public string Endpoint => $"http://127.0.0.1:{Accounts.Port}/v1";
    public IReadOnlyList<GatewayModel> Models => Accounts.List.SelectMany(a => bridge.Bindings.Where(b => b.Id == a.BindingId && b.Ready
        && new Uri(b.Url).GetLeftPart(UriPartial.Authority) == a.Origin).SelectMany(b => b.ApiModels.Select(m =>
            new GatewayModel(a.Id + ":" + m.Id, m.Name, a.Id, a.Name, b.Id, m.Id, m.Protocol)))).ToArray();
    public GatewayModel Validate(string accountId, string modelId) => Models.SingleOrDefault(m => m.AccountId == accountId && m.WebModelId == modelId)
        ?? throw new InvalidOperationException("Phiên/model API web chưa sẵn sàng. Mở Gateway & tài khoản web, liên kết đúng tab và nhận diện request của model đang chọn; Hub không tự dùng model/quota CLI.");
    public Task<string> Execute(string accountId, string modelId, string prompt, Action<string> text, Action<string> log, CancellationToken token)
    {
        var selected = Validate(accountId, modelId);
        log($"\nGateway web: {selected.AccountName} · {selected.Name} · {selected.Protocol}.\n");
        return bridge.Execute("browser-web", selected.BindingId, "", prompt, text, log, token, selected.WebModelId);
    }
    public async Task Start()
    {
        await lifecycle.WaitAsync();
        try {
            if (server != null) return;
            string key = Accounts.LocalKey();
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => { options.Listen(IPAddress.Loopback, Accounts.Port); options.Limits.MaxRequestBodySize = 1024 * 1024; });
            var app = builder.Build();
            app.Use(async (context, next) => {
                context.Response.Headers.CacheControl = "no-store";
                var request = context.Request;
                if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) || request.Host.Host != "127.0.0.1"
                    || request.Host.Port != Accounts.Port || request.Headers.ContainsKey("Origin")) { context.Response.StatusCode = 403; return; }
                var authorization = request.Headers.Authorization.ToString();
                if (!SecureEquals(authorization, "Bearer " + key)) { await Error(context, 401, "Cần API key cục bộ của Hub."); return; }
                try { await next(); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
                catch (Exception e) {
                    // Do not return raw upstream bodies, account URLs, request headers or secrets.
                    string message = e is ArgumentException or JsonException or BadHttpRequestException ? "Yêu cầu không hợp lệ: chỉ hỗ trợ chat văn bản, model đã liên kết, tối đa 200.000 ký tự."
                        : "Phiên web chưa sẵn sàng hoặc upstream đã dừng. Kiểm tra tab và trạng thái tài khoản trong Hub; không tự gửi lại.";
                    if (!context.Response.HasStarted) await Error(context, e is ArgumentException or JsonException or BadHttpRequestException ? 400 : 502, message);
                    else { await context.Response.WriteAsync("data: " + JsonSerializer.Serialize(new { error = new { message, type = "web_gateway_error" } }) + "\n\n", context.RequestAborted); await context.Response.Body.FlushAsync(context.RequestAborted); }
                }
            });
            app.MapGet("/v1/models", () => Results.Json(new { @object = "list", data = Models.Select(m => new { id = m.Id, @object = "model", owned_by = m.AccountName, name = m.Name, transport = m.Protocol }) }));
            app.MapPost("/v1/chat/completions", Completion);
            try { await app.StartAsync(); server = app; } catch { await app.DisposeAsync(); throw; }
        }
        finally { lifecycle.Release(); }
    }
    private async Task Completion(HttpContext context)
    {
        using var input = await JsonDocument.ParseAsync(context.Request.Body, new JsonDocumentOptions { MaxDepth = 16 }, context.RequestAborted);
        var parsed = Parse(input.RootElement);
        var selected = Models.SingleOrDefault(m => m.Id == parsed.Model) ?? throw new ArgumentException("Model gateway chưa được liên kết.");
        string id = "chatcmpl-" + Guid.NewGuid().ToString("N"); long created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!parsed.Stream) {
            string answer = await Execute(selected.AccountId, selected.WebModelId, parsed.Prompt, _ => { }, _ => { }, context.RequestAborted);
            await context.Response.WriteAsJsonAsync(new { id, @object = "chat.completion", created, model = selected.Id,
                choices = new[] { new { index = 0, message = new { role = "assistant", content = answer }, finish_reason = "stop" } } }, context.RequestAborted); return;
        }
        var snapshots = Channel.CreateBounded<string>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
        async Task<string> Generate() {
            try { return await Execute(selected.AccountId, selected.WebModelId, parsed.Prompt, value => snapshots.Writer.TryWrite(value), _ => { }, context.RequestAborted); }
            finally { snapshots.Writer.TryComplete(); }
        }
        Task<string> generation = Generate();
        context.Response.ContentType = "text/event-stream";
        string previous = "";
        async Task Send(string content, bool done = false) {
            await context.Response.WriteAsync("data: " + JsonSerializer.Serialize(new { id, @object = "chat.completion.chunk", created, model = selected.Id,
                choices = new[] { new { index = 0, delta = done ? (object)new { } : new { content }, finish_reason = done ? "stop" : (string?)null } } }) + "\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
        try {
            await foreach (var snapshot in snapshots.Reader.ReadAllAsync(context.RequestAborted)) {
                if (!snapshot.StartsWith(previous, StringComparison.Ordinal)) throw new InvalidOperationException("Upstream đã thay lại văn bản đang stream.");
                if (snapshot.Length > previous.Length) await Send(snapshot[previous.Length..]); previous = snapshot;
            }
            string final = await generation;
            if (!final.StartsWith(previous, StringComparison.Ordinal)) throw new InvalidOperationException("Upstream đã thay lại văn bản đang stream.");
            if (final.Length > previous.Length) await Send(final[previous.Length..]);
            await Send("", true); await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
        }
        finally { if (!generation.IsCompleted) { context.Abort(); try { await generation; } catch { } } }
    }
    public static (string Model, string Prompt, bool Stream) Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(model.GetString()) || model.GetString()!.Length > 200 || !input.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() is < 1 or > 128) throw new ArgumentException("Model/messages không hợp lệ.");
        foreach (var property in input.EnumerateObject())
            if (property.Name is not ("model" or "messages" or "stream" or "n")) throw new ArgumentException("Tham số chưa được web adapter hỗ trợ: " + property.Name);
        if (input.TryGetProperty("n", out var n) && (n.ValueKind != JsonValueKind.Number || !n.TryGetInt32(out int count) || count != 1)) throw new ArgumentException("Chỉ hỗ trợ n=1.");
        bool stream = false;
        if (input.TryGetProperty("stream", out var streaming)) { if (streaming.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("stream cần boolean."); stream = streaming.GetBoolean(); }
        var transcript = new StringBuilder();
        foreach (var message in messages.EnumerateArray()) {
            string role = message.Str("role");
            if (role is not ("system" or "developer" or "user" or "assistant") || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) throw new ArgumentException("Chỉ hỗ trợ messages văn bản.");
            if (messages.GetArrayLength() == 1 && role == "user") transcript.Append(content.GetString());
            else transcript.Append('[').Append(role).Append("]\n").Append(content.GetString()).Append("\n\n");
            if (transcript.Length > BrowserChatProtocol.MaxPromptCharacters) throw new ArgumentException("Ngữ cảnh vượt giới hạn.");
        }
        if (string.IsNullOrWhiteSpace(transcript.ToString())) throw new ArgumentException("Prompt rỗng.");
        return (model.GetString()!, transcript.ToString(), stream);
    }
    public static bool SecureEquals(string value, string expected) => value.Length == expected.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(expected));
    private static Task Error(HttpContext context, int code, string message) { context.Response.StatusCode = code; return context.Response.WriteAsJsonAsync(new { error = new { message, type = "web_gateway_error" } }); }
    public async Task Stop()
    {
        await lifecycle.WaitAsync();
        try { if (server is { } app) { server = null; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); try { await app.StopAsync(timeout.Token); } finally { await app.DisposeAsync(); } } }
        finally { lifecycle.Release(); }
    }
    public void Dispose() => Stop().GetAwaiter().GetResult();
}
