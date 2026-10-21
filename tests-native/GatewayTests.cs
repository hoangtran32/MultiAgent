using System.IO.Pipes;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using MultiAgentHub.Core;

internal static class GatewayTests
{
    private static void Assert(bool condition) { if (!condition) throw new Exception("Gateway fixture assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected validation rejection."); }
    private sealed class Secrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public string Read(string id) => values.GetValueOrDefault(id, "");
        public void Write(string id, string value) => values[id] = value;
        public void Delete(string id) => values.Remove(id);
    }
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        await test("web gateway parses text context and rejects unsupported tools multimodal and generation settings", () => {
            using var input = JsonDocument.Parse("""{"model":"account:web-model","stream":true,"messages":[{"role":"system","content":"Be helpful"},{"role":"user","content":"hello"}]}""");
            var parsed = WebGateway.Parse(input.RootElement); Assert(parsed.Stream && parsed.Model == "account:web-model" && parsed.Prompt.Contains("[system]\nBe helpful"));
            foreach (var body in new[] {
                """{"model":"m","messages":[{"role":"user","content":[{"type":"image_url"}]}]}""",
                """{"model":"m","tools":[],"messages":[{"role":"user","content":"hello"}]}""",
                """{"model":"m","temperature":0,"messages":[{"role":"user","content":"hello"}]}""",
                """{"model":"m","n":"2","messages":[{"role":"user","content":"hello"}]}""",
                """{"model":"m","stream":"true","messages":[{"role":"user","content":"hello"}]}""" }) {
                using var invalid = JsonDocument.Parse(body); Reject(() => WebGateway.Parse(invalid.RootElement));
            }
            Assert(WebGateway.SecureEquals("same", "same") && !WebGateway.SecureEquals("same", "diff") && !WebGateway.SecureEquals("short", "longer"));
            return Task.CompletedTask;
        });
        await test("web account profiles start empty preserve aliases and keep local keys in secret store", () => {
            var storage = new Storage(Path.Combine(root, "gateway-profile-" + Guid.NewGuid().ToString("N"))); var secrets = new Secrets();
            var accounts = new GatewayAccounts(storage, secrets); Assert(accounts.List.Count == 0);
            string key = accounts.LocalKey(); Assert(key.Length == 64 && new GatewayAccounts(storage, secrets).LocalKey() == key);
            accounts.Save(new() { Name = "My browser account", BindingId = "linked-document", Origin = "https://research.example" });
            var restored = new GatewayAccounts(storage, secrets); Assert(restored.List.Count == 1 && restored.List[0].Name == "My browser account");
            Assert(!File.ReadAllText(Path.Combine(storage.DataDir, "gateway-accounts.json")).Contains(key));
            foreach (string origin in new[] { "https://research.example/path", "http://research.example", "https://user:secret@research.example", "https://research.example:444" })
                Reject(() => accounts.Save(new() { BindingId = "tab", Origin = origin }));
            accounts.Remove(restored.List[0].Id); Assert(accounts.List.Count == 0); return Task.CompletedTask;
        });
        await test("gateway routes only an explicitly bound discovered model over IPC and exposes authenticated loopback API", async () => {
            // CI-only synthetic extension; no website, user credentials or inference involved.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var bridge = new BrowserChatBridge(); bridge.Start();
            using var client = new NamedPipeClientStream(".", BrowserChatProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(deadline.Token);
            async Task<JsonDocument> Read() => BrowserChatProtocol.Parse((await BrowserChatProtocol.ReadFrame(client, deadline.Token))!);
            Task Send(object value) => BrowserChatProtocol.WriteFrame(client, BrowserChatProtocol.Serialize(value), deadline.Token);
            using var hello = await Read();
            const string modelId = "web-0123456789abcdef01234567";
            await Send(new { type = "state", bindings = new[] { new { id = "selected-document", provider = "browser-web", tabId = 9,
                url = "https://research.example/chat/new", title = "Fixture", model = "Website UI label", ready = true,
                apiModels = new[] { new { id = modelId, name = "Observed model", protocol = "chat-completions", source = "request" } } } } });
            while (bridge.Bindings.Count == 0) await Task.Delay(5, deadline.Token);
            var accounts = new GatewayAccounts(new Storage(Path.Combine(root, "gateway-api-" + Guid.NewGuid().ToString("N"))), new Secrets());
            var account = new GatewayAccount { BindingId = "selected-document", Origin = "https://research.example", Name = "Fixture account" }; accounts.Save(account);
            using var gateway = new WebGateway(accounts, bridge);
            Assert(gateway.Models.Count == 1 && gateway.Models[0].WebModelId == modelId);
            var agent = new AgentConfig { Provider = "gateway", Connection = "gateway-web", GatewayAccountId = account.Id, Model = modelId };
            var executor = new AgentExecutor(new ApiTransport(new Secrets()), bridge, gateway); executor.Validate(agent);
            string partial = "";
            Task<string> answer = executor.Execute(agent, "", "hello peer", value => partial = value, _ => { }, deadline.Token);
            using var dispatched = await Read(); var command = dispatched.RootElement;
            Assert(command.Str("type") == "api-request" && command.Str("modelId") == modelId && command.Str("prompt") == "hello peer");
            await Send(new { type = "delta", id = command.Str("id"), text = "Hello" });
            await Send(new { type = "result", id = command.Str("id"), text = "Hello teammate" });
            Assert(await answer == "Hello teammate" && partial == "Hello teammate");
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); accounts.SetPort(port);
            await gateway.Start();
            using var http = new HttpClient { BaseAddress = new Uri(gateway.Endpoint + "/"), Timeout = TimeSpan.FromSeconds(8) };
            using var unauthorized = await http.GetAsync("models", deadline.Token); Assert(unauthorized.StatusCode == HttpStatusCode.Unauthorized);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accounts.LocalKey());
            using var models = await http.GetAsync("models", deadline.Token); Assert(models.IsSuccessStatusCode && (await models.Content.ReadAsStringAsync()).Contains(account.Id + ":" + modelId));
            http.DefaultRequestHeaders.Add("Origin", "https://evil.example");
            using var crossOrigin = await http.GetAsync("models", deadline.Token); Assert(crossOrigin.StatusCode == HttpStatusCode.Forbidden);
            http.DefaultRequestHeaders.Remove("Origin");
            using var invalid = await http.PostAsync("chat/completions", new StringContent("""{"model":"unknown","messages":[{"role":"user","content":"hello"}]}""", System.Text.Encoding.UTF8, "application/json"), deadline.Token);
            Assert(invalid.StatusCode == HttpStatusCode.BadRequest);
            await gateway.Stop(); Assert(!gateway.Running);
            accounts.Save(new() { Id = account.Id, Name = account.Name, BindingId = account.BindingId, Origin = "https://another.example" }); Assert(gateway.Models.Count == 0);
        });
    }
}
