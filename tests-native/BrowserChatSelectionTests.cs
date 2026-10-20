using System.Security.Cryptography;
using System.Text.Json;
using MultiAgentHub.Core;

internal static class BrowserChatSelectionTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Browser chatbot selection assertion failed."); }
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        await test("chatbot selection never changes the running workspace agent selection", () => {
            var work = new AgentConfig { Id = "work", Name = "Work", Provider = "codex", Connection = "codex-cli", Enabled = true };
            var gemini = new AgentConfig { Id = "gemini", Name = "Gemini", Provider = "gemini", Connection = "gemini-web", BrowserBindingId = "tab-a", Model = "User confirmed model", Enabled = false };
            var chat = new AgentConfig { Id = "chat", Name = "ChatGPT", Provider = "chatgpt", Connection = "chatgpt-web", BrowserBindingId = "tab-b", Model = "Another confirmed model", Enabled = false };
            var config = new HubConfig { Project = root, Agents = [work, gemini, chat], ChatAgentIds = [gemini.Id, chat.Id] };
            Storage.Validate(config); Assert(config.SelectedAgents().Single().Id == "work");
            config.Surface = "chat"; Assert(config.SelectedAgents().Select(a => a.Id).SequenceEqual(new[] { "gemini", "chat" }));
            config.SelectAgent(gemini, false); Assert(config.SelectedAgents().Single().Id == "chat" && work.Enabled && !gemini.Enabled);
            config.Surface = "work"; Assert(config.SelectedAgents().Single().Id == "work");
            var copy = JsonSerializer.Deserialize<HubConfig>(JsonSerializer.Serialize(config, Json.Options), Json.Options)!;
            Assert(copy.ChatAgentIds!.SequenceEqual(new[] { "chat" })); return Task.CompletedTask;
        });
        await test("browser chatbots can support work as text advisers without claiming local tools", async () => {
            var browser = new AgentConfig { Provider = "gemini", Connection = "gemini-web", Model = "Confirmed model", BrowserBindingId = "tab-a" };
            var fixture = new TextAdviser();
            var store = new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))); var runner = new JobRunner(fixture, store);
            runner.Start(new() { Project = root, Agents = [browser] }, "Review the supplied design."); await runner.Completion!;
            Assert(runner.Snapshot()!.State == "completed" && fixture.Prompt.Contains("HỖ TRỢ CÔNG VIỆC QUA VĂN BẢN")
                && fixture.Prompt.Contains("Không có quyền đọc/sửa") && runner.Snapshot()!.Project == root);
        });
        await test("bundled browser extension key matches the native host allowlist", () => {
            string folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "browser-extension"));
            if (!Directory.Exists(folder)) folder = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            var digest = SHA256.HashData(Convert.FromBase64String(doc.RootElement.GetProperty("key").GetString()!));
            string id = string.Concat(digest.Take(16).SelectMany(b => new[] { (char)('a' + (b >> 4)), (char)('a' + (b & 15)) }));
            Assert(id == BrowserChatProtocol.ExtensionId);
            Assert(BrowserChatProtocol.IsExtensionOrigin("chrome-extension://" + id + "/") && !BrowserChatProtocol.IsExtensionOrigin("chrome-extension://wrong/"));
            var permissions = doc.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray();
            Assert(!permissions.Contains("cookies") && !permissions.Contains("debugger") && !permissions.Contains("tabs")); return Task.CompletedTask;
        });
        await test("new Hub topics cannot silently reuse a browser conversation assigned elsewhere", () => {
            var agent = new AgentConfig { Id = "chat", Provider = "chatgpt", Connection = "chatgpt-web", BrowserBindingId = "used-tab", Model = "Confirmed" };
            var config = new HubConfig { Surface = "chat", Mode = "discussion", Project = root, Agents = [agent], BrowserTopics = new() { ["used-tab"] = "old-topic" } };
            var runner = new JobRunner(new PreflightOnly(), new Storage(Path.Combine(root, Guid.NewGuid().ToString("N")))); bool rejected = false;
            try { runner.Start(config, "A different topic."); }
            catch (ArgumentException error) { rejected = error.Message.Contains("chủ đề Hub khác"); }
            Assert(rejected && !runner.Busy && config.BrowserTopics["used-tab"] == "old-topic"); return Task.CompletedTask;
        });
    }
    private sealed class TextAdviser : IAgentExecutor
    {
        public string Prompt { get; private set; } = "";
        public Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token) { Prompt = prompt; return Task.FromResult("Design feedback, no local changes performed."); }
    }
    private sealed class PreflightOnly : IAgentExecutor
    {
        public Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token) => throw new Exception("A conflicting topic must not start inference.");
    }
}
