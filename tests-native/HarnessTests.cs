using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MultiAgentHub.Core;

internal static class HarnessTests
{
    private static void Assert(bool value, string message = "Harness assertion failed") { if (!value) throw new Exception(message); }
    private static JsonElement JsonValue(object value) => JsonSerializer.SerializeToElement(value, Json.Options);
    private static HarnessCall Call(string name, object args, string? id = null) => new(id ?? Guid.NewGuid().ToString("N"), name, JsonValue(args));
    private static HarnessTurn Tools(params HarnessCall[] calls) => new("", calls, JsonValue(new { role = "assistant", tool_calls = calls.Select(c => new { id = c.Id, type = "function", function = new { name = c.Name, arguments = c.Arguments.GetRawText() } }) }));
    private static HarnessTurn Final(string text = "Requested result") => new(text, [], JsonValue(new { role = "assistant", content = text }));
    private static async Task Reject(Func<Task> action) { try { await action(); } catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException) { return; } throw new Exception("Expected harness rejection"); }
    private static HarnessBinding Binding(string root, string surface = "work", HarnessOptions? options = null, Action<HarnessRun>? observe = null) {
        string project = Path.Combine(root, "project"); Directory.CreateDirectory(project);
        return new(new Storage(Path.Combine(root, "profile")), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), surface, surface == "work" ? project : "", options ?? new(), observe ?? (_ => { }));
    }
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        await test("harness capabilities follow surface permission and workflow phase, never model text", async () => {
            var binding = Binding(Path.Combine(root, "harness-permissions"));
            foreach (string permission in new[] { "default", "read-only", "workspace-write", "full" }) {
                var agent = new AgentConfig { Permission = permission };
                var tools = new HarnessTools(binding, agent);
                Assert(tools.Definitions.Any(t => t.Name == "read_file"));
                Assert(tools.CanWrite == (permission is "workspace-write" or "full")); Assert(tools.CanCommand == (permission == "full"));
                var chat = new HarnessTools(binding with { Surface = "chat", Project = "" }, agent);
                Assert(!chat.Definitions.Any(t => t.Name is "read_file" or "write_file" or "run_command"));
                await Reject(() => chat.Execute(Call("read_file", new { path = "a.txt" }), default));
                var review = new HarnessTools(binding with { AllowChanges = false }, agent); Assert(!review.CanWrite && !review.CanCommand);
            }
        });
        await test("harness file writes use a content hash and reject stale edits traversal and Git paths", async () => {
            var binding = Binding(Path.Combine(root, "harness-files")); var tools = new HarnessTools(binding, new() { Permission = "workspace-write" });
            await tools.Execute(Call("write_file", new { path = "src/file.txt", text = "original", expected_hash = "new" }), default);
            var read = JsonValue(await tools.Execute(Call("read_file", new { path = "src/file.txt" }), default)); string hash = read.Str("sha256");
            Assert(read.GetProperty("content").Str("text") == "original");
            File.WriteAllText(Path.Combine(binding.Project, "src/file.txt"), "User edit");
            await Reject(() => tools.Execute(Call("write_file", new { path = "src/file.txt", text = "bad", expected_hash = hash }), default));
            Assert(File.ReadAllText(Path.Combine(binding.Project, "src/file.txt")) == "User edit");
            foreach (string path in new[] { "../outside.txt", ".git/config", ".git./config", "C:/outside.txt", "src/file.txt:stream" }) await Reject(() => tools.Execute(Call("write_file", new { path, text = "bad", expected_hash = "new" }), default));
            Assert(!File.Exists(Path.Combine(Path.GetDirectoryName(binding.Project)!, "outside.txt")));
        });
        await test("harness performs tools and continues the same model conversation with durable receipts", async () => {
            var observed = new List<HarnessRun>(); var binding = Binding(Path.Combine(root, "harness-loop"), observe: observed.Add);
            var model = new Model((input, count) => Task.FromResult(count == 1 ? Tools(Call("read_file", new { path = "a.txt" })) : Final()));
            File.WriteAllText(Path.Combine(binding.Project, "a.txt"), "Actual file content");
            string answer = await new AgentHarness(model).Execute(new() { Permission = "read-only", RuntimeHarness = binding }, "Read a.txt", _ => { }, _ => { }, default);
            Assert(answer == "Requested result" && model.Inputs[1].History.Single().Results.Single().Output.Contains("Actual file content"));
            var state = observed.Last(); Assert(state.State == "completed" && state.ModelCalls == 2 && state.ToolCalls == 1);
            var receipt = JsonValue(HarnessJournal.Read(binding, state.Id, "call-1", 0, 16000)); Assert(receipt.Str("text").Contains("completed") && receipt.Str("text").Contains("Actual file content"));
        });
        await test("harness rejects duplicate call IDs before any tool side effect", async () => {
            var binding = Binding(Path.Combine(root, "harness-duplicate"));
            var call = Call("memory_write", new { key = "must-not-exist", value = "bad" }, "duplicate");
            var model = new Model((_, _) => Task.FromResult(Tools(call, call)));
            await Reject(() => new AgentHarness(model).Execute(new() { RuntimeHarness = binding }, "Remember only if valid", _ => { }, _ => { }, default));
            var notes = JsonValue(await new HarnessTools(binding, new()).Execute(Call("memory_read", new { }), default)); Assert(!notes.TryGetProperty("must-not-exist", out _));
        });
        await test("harness budget stops retain written files and never claim task completion", async () => {
            var observed = new List<HarnessRun>(); var binding = Binding(Path.Combine(root, "harness-budget"), options: new() { MaxModelCalls = 1 }, observe: observed.Add);
            var model = new Model((_, _) => Task.FromResult(Tools(Call("write_file", new { path = "saved.txt", text = "persisted", expected_hash = "new" }))));
            await Reject(() => new AgentHarness(model).Execute(new() { Permission = "workspace-write", RuntimeHarness = binding }, "Write saved.txt", _ => { }, _ => { }, default));
            Assert(File.ReadAllText(Path.Combine(binding.Project, "saved.txt")) == "persisted" && observed.Last().State == "limited");
        });
        await test("new user steering invalidates an in-flight tool proposal before file mutation", async () => {
            var binding = Binding(Path.Combine(root, "harness-steering")); string updates = ""; binding = binding with { UserUpdates = () => updates };
            var model = new Model((input, number) => {
                if (number == 1) { updates = "Do not create that file; only explain."; return Task.FromResult(Tools(Call("write_file", new { path = "wrong.txt", text = "bad", expected_hash = "new" }))); }
                Assert(input.Prompt.Contains(updates)); return Task.FromResult(Final("Explanation"));
            });
            string result = await new AgentHarness(model).Execute(new() { Permission = "workspace-write", RuntimeHarness = binding }, "Write a file", _ => { }, _ => { }, default);
            Assert(result == "Explanation" && !File.Exists(Path.Combine(binding.Project, "wrong.txt")));
        });
        await test("harness memory is shared only inside the current conversation and traces cannot escape", async () => {
            var binding = Binding(Path.Combine(root, "harness-memory"), "chat"); var tools = new HarnessTools(binding, new());
            await tools.Execute(Call("memory_write", new { key = "fact", value = "User fact" }), default);
            Assert(JsonValue(await new HarnessTools(binding, new()).Execute(Call("memory_read", new { }), default)).Str("fact") == "User fact");
            var other = binding with { ConversationId = Guid.NewGuid().ToString("N") };
            Assert(!JsonValue(await new HarnessTools(other, new()).Execute(Call("memory_read", new { }), default)).TryGetProperty("fact", out _));
            await Reject(() => tools.Execute(Call("read_trace", new { run = "../../outside", receipt = "call-1" }), default));
            for (int i = 0; i < 7; i++) await tools.Execute(Call("memory_write", new { key = "unicode-" + i, value = new string('ệ', 7000) }), default);
            Assert(JsonValue(await tools.Execute(Call("memory_read", new { }), default)).Str("unicode-6").Length == 7000);
            var legacy = binding with { ConversationId = "legacy-topic-1" }; var journal = new HarnessJournal(legacy, Guid.NewGuid().ToString("N")); journal.Save("state", new HarnessRun());
            await new HarnessTools(legacy, new()).Execute(Call("memory_write", new { key = "legacy", value = "Kept" }), default);
        });
        await test("project instructions are scoped and Chat artifacts stay outside any user project", async () => {
            var binding = Binding(Path.Combine(root, "harness-instructions")); var tools = new HarnessTools(binding, new());
            File.WriteAllText(Path.Combine(binding.Project, "AGENTS.md"), "Project coding rule"); Directory.CreateDirectory(Path.Combine(binding.Project, "src"));
            File.WriteAllText(Path.Combine(binding.Project, "src/AGENTS.md"), "Nested coding rule"); File.WriteAllText(Path.Combine(binding.Project, "src/a.txt"), "data");
            Assert(tools.RootInstructions().Contains("Project coding rule"));
            var read = JsonValue(await tools.Execute(Call("read_file", new { path = "src/a.txt" }), default)); Assert(read.GetProperty("projectInstructions").GetArrayLength() == 2);
            var chat = new HarnessTools(binding with { Surface = "chat", Project = "" }, new()); Assert(chat.RootInstructions() == "");
            var artifact = JsonValue(await chat.Execute(Call("create_artifact", new { name = "notes.md", text = "# User result" }), default));
            Assert(artifact.Str("path").StartsWith(binding.Storage.DataDir) && File.ReadAllText(artifact.Str("path")) == "# User result");
            await Reject(() => chat.Execute(Call("create_artifact", new { name = "../bad.md", text = "bad" }), default));
        });
        await test("harness additional requests consume the outer budget and dead owners are presented interrupted", async () => {
            var observed = new List<HarnessRun>(); var binding = Binding(Path.Combine(root, "harness-outer-budget"), "chat", observe: observed.Add) with { TryAdditionalModelCall = () => false };
            var model = new Model((_, _) => Task.FromResult(Tools(Call("memory_read", new { }))));
            await Reject(() => new AgentHarness(model).Execute(new() { RuntimeHarness = binding }, "Read notes", _ => { }, _ => { }, default));
            Assert(model.Inputs.Count == 1 && observed.Last().State == "limited");
            var dead = new HarnessRun { OwnerProcessId = int.MaxValue }; new HarnessJournal(binding, dead.Id).Save("state", dead);
            var runs = JsonValue(HarnessJournal.Runs(binding)).EnumerateArray(); Assert(runs.Single(r => r.Str("id") == dead.Id).Str("state") == "interrupted");
        });
        await test("harness compaction keeps the original request and atomic recent call-result groups", () => {
            var history = new List<HarnessExchange>();
            for (int i = 0; i < 8; i++) history.Add(new(Tools(Call("read_file", new { path = "file-" + i })), [new("call", "read_file", new string('x', 10000))]));
            string prompt = "Original direct user request"; var selected = AgentHarness.Context(prompt, history, new() { ContextChars = 32768 }, "run", out string note);
            Assert(selected.Count is > 0 and < 8 && ReferenceEquals(selected.Last(), history.Last()) && history.Count == 8 && prompt == "Original direct user request" && note.Contains("read_trace"));
            return Task.CompletedTask;
        });
        await test("harness full-permission commands return actual exit evidence and cancellation leaves a pending receipt", async () => {
            var binding = Binding(Path.Combine(root, "harness-command")); var tools = new HarnessTools(binding, new() { Permission = "full" });
            var result = JsonValue(await tools.Execute(Call("run_command", new { executable = Environment.ProcessPath!, arguments = new[] { "--fixture", "harness-command" } }), default));
            Assert(result.Str("state") == "passed" && result.GetProperty("exitCode").GetInt32() == 0 && result.Str("log").Contains("Harness command proof"));
            using var cancel = new CancellationTokenSource(); var observed = new List<HarnessRun>(); binding = binding with { Observe = observed.Add };
            var model = new Model((_, _) => Task.FromResult(Tools(Call("run_command", new { executable = Environment.ProcessPath!, arguments = new[] { "--fixture", "slow" } }))));
            var execution = new AgentHarness(model).Execute(new() { Permission = "full", RuntimeHarness = binding }, "Run the fixture", _ => { }, log => { if (log.Contains("đang chạy")) cancel.Cancel(); }, cancel.Token);
            try { await execution; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            Assert(observed.Last().State == "interrupted");
            string receipt = JsonValue(HarnessJournal.Read(binding, observed.Last().Id, "call-1", 0, 16000)).Str("text"); Assert(receipt.Contains("pending") && !receipt.Contains("completed"));
        });
        await test("native API harness roundtrip includes tool schemas and role-correct tool results", async () => {
            var fixtures = new[] { "chat-api", "anthropic-api", "gemini-api" };
            foreach (string connection in fixtures) {
                int calls = 0; var binding = Binding(Path.Combine(root, "harness-api-" + connection), "chat");
                var handler = new FakeHandler(async request => {
                    using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var data = payload.RootElement;
                    Assert(data.GetProperty("tools").GetArrayLength() > 0); calls++;
                    if (calls == 2) { Assert(data.GetRawText().Contains("memory_read") && (data.GetRawText().Contains("tool_result") || data.GetRawText().Contains("functionResponse") || data.GetRawText().Contains("tool_call_id"))); if (connection == "gemini-api") Assert(data.GetRawText().Contains("preserve-signature")); }
                    return connection switch {
                        "chat-api" => JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = calls == 1 ? "tool_calls" : "stop", message = calls == 1 ? (object)new { role = "assistant", tool_calls = new[] { new { id = "native-call", type = "function", function = new { name = "memory_read", arguments = "{}" } } } } : new { role = "assistant", content = "Done" } } } }),
                        "anthropic-api" => JsonSerializer.Serialize(new { stop_reason = calls == 1 ? "tool_use" : "end_turn", content = calls == 1 ? new object[] { new { type = "tool_use", id = "native-call", name = "memory_read", input = new { } } } : new object[] { new { type = "text", text = "Done" } } }),
                        _ => JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { role = "model", parts = calls == 1 ? new object[] { new { functionCall = new { name = "memory_read", args = new { } }, thoughtSignature = "preserve-signature" } } : new object[] { new { text = "Done" } } } } } })
                    };
                });
                var agent = new AgentConfig { Connection = connection, Provider = connection == "chat-api" ? "compatible" : connection == "gemini-api" ? "gemini" : "claude", Model = "selected-model", RuntimeHarness = binding };
                string result = await new AgentHarness(new ProviderHarnessModel(new ApiTransport(new FakeSecrets(), new HttpClient(handler)), null)).Execute(agent, "Read notes", _ => { }, _ => { }, default);
                Assert(result == "Done" && calls == 2);
            }
        });
        await test("Responses harness preserves opaque reasoning and waits for completed before tool execution", async () => {
            var output = new object[] { new { type = "reasoning", id = "reason", encrypted_content = "opaque" }, new { type = "function_call", call_id = "id", name = "memory_read", arguments = "{}" } };
            string data = "data: " + JsonSerializer.Serialize(new { type = "response.completed", response = new { output } }) + "\n\n";
            var parsed = await ChatGptPlanStream.ReadTurn(new StringReader(data), _ => { }, default);
            var turn = ProviderHarnessModel.Responses(parsed.Text, parsed.Output);
            Assert(turn.Calls.Single().Name == "memory_read" && turn.Native[0].Str("encrypted_content") == "opaque");
            var file = ChatAttachments.FromBytes("notes.txt", Encoding.UTF8.GetBytes("Unique document sentence"));
            var agent = new AgentConfig { Connection = "chatgpt-plan", RuntimeAttachments = [file] };
            var input = new HarnessInput("Instructions", ChatAttachments.WithText("Question", agent), [new(turn, [new("id", "memory_read", "{}")])], [], [file]);
            var payload = JsonValue(ChatGptPlan.HarnessPayload(agent, input));
            Assert(payload.GetProperty("input")[0].GetProperty("content").GetArrayLength() == 1);
            Assert(payload.GetProperty("input")[1].Str("encrypted_content") == "opaque" && payload.GetProperty("input")[3].Str("type") == "function_call_output");
            await Reject(() => ChatGptPlanStream.ReadTurn(new StringReader("data: {\"type\":\"response.output_item.done\"}\n\n"), _ => { }, default));
        });
        await test("provider truncation and private network addresses are rejected before tool execution", async () => {
            using var truncated = JsonDocument.Parse("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"role\":\"assistant\",\"content\":\"partial\"}}]}");
            await Reject(() => Task.FromResult(ApiTransport.ParseHarness("chat-api", truncated.RootElement)));
            foreach (string address in new[] { "127.0.0.1", "10.1.1.1", "172.16.0.2", "192.168.1.1", "169.254.169.254", "100.64.0.1", "::1", "fc00::1", "fe80::1", "::ffff:127.0.0.1" }) Assert(!HarnessWeb.PublicAddress(IPAddress.Parse(address)), address);
            Assert(HarnessWeb.PublicAddress(IPAddress.Parse("1.1.1.1")));
            await Reject(() => HarnessWeb.Read("http://localhost/private", default));
        });
        await test("JobRunner attaches the same harness to Chat and Code and persists observed execution state", async () => {
            foreach (string surface in new[] { "chat", "work" }) {
                string directory = Path.Combine(root, "harness-job-" + surface); Directory.CreateDirectory(directory);
                var storage = new Storage(Path.Combine(directory, "profile")); int calls = 0;
                var handler = new FakeHandler(_ => Task.FromResult(++calls == 1 ? "{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"one\",\"type\":\"function\",\"function\":{\"name\":\"memory_read\",\"arguments\":\"{}\"}}]}}]}" : "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"Finished\"}}]}"));
                var runner = new JobRunner(new AgentExecutor(new ApiTransport(new FakeSecrets(), new HttpClient(handler))), storage);
                var config = new HubConfig { Surface = surface, Project = directory, Mode = "parallel", Agents = [new() { Provider = "compatible", Connection = "chat-api", Model = "chosen" }] };
                runner.Start(config, "Read notes"); await runner.Completion!;
                var job = runner.Snapshot()!; Assert(job.Results.Single().Text == "Finished" && job.HarnessRuns.Single().State == "completed" && calls == 2 && !runner.Busy);
                Assert((surface == "chat") == (job.Project.Length == 0));
            }
        });
    }
    private sealed class Model(Func<HarnessInput, int, Task<HarnessTurn>> turn) : IHarnessModel
    {
        public List<HarnessInput> Inputs { get; } = [];
        public Task<HarnessTurn> Turn(AgentConfig agent, HarnessInput input, Action<string> text, CancellationToken token) { Inputs.Add(input); return turn(input, Inputs.Count); }
    }
}
