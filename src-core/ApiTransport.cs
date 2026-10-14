using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed class ApiTransport
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false }) {
        Timeout = TimeSpan.FromMinutes(10), MaxResponseContentBufferSize = 16 * 1024 * 1024 };
    private readonly HttpClient client;
    private readonly ISecretStore secrets;
    public ApiTransport(ISecretStore secrets, HttpClient? client = null)
    {
        this.secrets = secrets;
        this.client = client ?? SharedClient;
    }
    public void Validate(AgentConfig agent)
    {
        Catalog.Validate(agent);
        if (string.IsNullOrWhiteSpace(agent.Model)) throw new ArgumentException($"{agent.Name}: chọn model trước khi chạy API.");
        if (Key(agent).Length == 0 && (agent.Connection != "chat-api" || BaseUri(agent).Host == "api.openai.com"))
            throw new ArgumentException($"{agent.Name}: chưa cấu hình API key. Mở Thiết lập / model để nhập key hoặc biến môi trường.");
    }
    public static Uri BaseUri(AgentConfig agent)
    {
        string endpoint = string.IsNullOrWhiteSpace(agent.Endpoint) ? Catalog.Endpoint(agent.Connection) : agent.Endpoint;
        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new ArgumentException("API base URL cần HTTPS, hoặc HTTP localhost. Không đặt key trong URL.");
        return uri;
    }
    private string Key(AgentConfig agent)
    {
        string key = secrets.Read(agent.Id);
        string variable = agent.KeyEnvironment.Length > 0 ? agent.KeyEnvironment : agent.Connection switch {
            "anthropic-api" when BaseUri(agent).Host == "api.anthropic.com" => "ANTHROPIC_API_KEY",
            "gemini-api" when BaseUri(agent).Host == "generativelanguage.googleapis.com" => "GEMINI_API_KEY",
            "chat-api" when BaseUri(agent).Host == "api.openai.com" => "OPENAI_API_KEY", _ => "" };
        return key.Length > 0 ? key : variable.Length > 0 ? Environment.GetEnvironmentVariable(variable) ?? "" : "";
    }
    private HttpRequestMessage Request(AgentConfig agent, HttpMethod method, string relative)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri(agent), relative));
        string key = Key(agent);
        if (agent.Connection != "chat-api" && key.Length == 0) throw new InvalidOperationException("Chưa cấu hình API key.");
        if (agent.Connection == "anthropic-api") { request.Headers.Add("anthropic-version", "2023-06-01"); request.Headers.Add("x-api-key", key); }
        else if (agent.Connection == "gemini-api") request.Headers.Add("x-goog-api-key", key);
        else if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
    private async Task<JsonDocument> Send(AgentConfig agent, HttpRequestMessage request, CancellationToken token)
    {
        using var response = await client.SendAsync(request, token);
        string body = await response.Content.ReadAsStringAsync(token);
        // Avoid echoing keys or user prompts from provider error bodies.
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode is 400 or 402 or 403 or 429 && Quota.ApiError(body)) throw new AgentQuotaException();
            throw new HttpRequestException($"API trả HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Kiểm tra endpoint, key, model và hạn mức.");
        }
        if (body.Length > 16000000) throw new InvalidDataException("Phản hồi API quá lớn.");
        return JsonDocument.Parse(body);
    }
    public async Task<string> Execute(AgentConfig agent, string prompt, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(agent.Model)) throw new ArgumentException("Chọn model trước khi gọi API.");
        object payload; string route;
        if (agent.Connection == "anthropic-api")
        { route = "messages"; payload = new { model = agent.Model, max_tokens = 8192, messages = new[] { new { role = "user", content = prompt } } }; }
        else if (agent.Connection == "gemini-api")
        { route = "models/" + Uri.EscapeDataString(agent.Model.Replace("models/", "")) + ":generateContent"; payload = new { contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } } }; }
        else
        { route = "chat/completions"; payload = new { model = agent.Model, messages = new[] { new { role = "user", content = prompt } }, stream = false }; }
        using var request = Request(agent, HttpMethod.Post, route);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var doc = await Send(agent, request, token); var root = doc.RootElement; string result;
        try
        {
            if (agent.Connection == "anthropic-api") result = string.Join("\n", root.GetProperty("content").EnumerateArray().Where(b => b.Str("type") == "text").Select(b => b.Str("text")));
            else if (agent.Connection == "gemini-api") result = string.Join("\n", root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts").EnumerateArray().Where(b => !b.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True).Select(b => b.Str("text")));
            else result = root.GetProperty("choices")[0].GetProperty("message").Str("content");
        }
        catch (Exception e) when (e is KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        { throw new InvalidDataException("API không trả câu trả lời văn bản theo định dạng đã chọn. Kiểm tra model, endpoint hoặc bộ lọc nội dung."); }
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("API không trả nội dung văn bản.");
        return CliParser.Limit(result);
    }
    public async Task<List<string>> Models(AgentConfig agent, CancellationToken token)
    {
        var models = new List<string>(); string next = "";
        for (int page = 0; page < 10; page++)
        {
            string route = "models";
            if (next.Length > 0) route += agent.Connection == "gemini-api" ? "?pageToken=" + Uri.EscapeDataString(next) : "?after_id=" + Uri.EscapeDataString(next);
            using var request = Request(agent, HttpMethod.Get, route);
            using var doc = await Send(agent, request, token); var root = doc.RootElement;
            string listKey = agent.Connection == "gemini-api" ? "models" : "data";
            foreach (var model in root.GetProperty(listKey).EnumerateArray())
            {
                if (agent.Connection == "gemini-api" && model.TryGetProperty("supportedGenerationMethods", out var methods) && !methods.EnumerateArray().Any(m => m.GetString() == "generateContent")) continue;
                models.Add(agent.Connection == "gemini-api" ? model.Str("name").Replace("models/", "") : model.Str("id"));
            }
            next = agent.Connection == "gemini-api" ? root.Str("nextPageToken") : root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True ? root.Str("last_id") : "";
            if (next.Length == 0) break;
        }
        return models.Where(m => m.Length > 0).Distinct().Order().ToList();
    }
}
