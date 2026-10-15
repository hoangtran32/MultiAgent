using System.Net;
using System.Text;
using System.Text.Json;
using MultiAgentHub.Core;

Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
if (args.FirstOrDefault() == "--fixture")
{
    if (args.Contains("pipe-holder")) { await Task.Delay(6000); return; }
    if (args.Contains("background-pipe")) {
        // Keep inherited stdout/stderr, but do not retain the temporary workspace as the child's cwd.
        // Windows cannot remove that directory while the intentionally surviving pipe-holder is running.
        var child = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        child.ArgumentList.Add("--fixture"); child.ArgumentList.Add("pipe-holder");
        System.Diagnostics.Process.Start(child)?.Dispose();
        Console.WriteLine("root-complete"); await Console.Out.FlushAsync(); return;
    }
    if (args.Contains("quota-event")) { Console.WriteLine("{\"type\":\"turn.failed\",\"error\":{\"code\":\"usage_limit_reached\"}}"); await Console.Out.FlushAsync(); await Task.Delay(60000); return; }
    if (args.Contains("quota")) { Console.Error.WriteLine("Error: insufficient_quota"); Environment.ExitCode = 2; return; }
    if (args.Contains("flood")) { for (int i = 0; i < 10000; i++) Console.WriteLine(new string('x', 4096)); await Task.Delay(60000); return; }
    if (args.Contains("slow")) { await Task.Delay(60000); return; }
    string input = await Console.In.ReadToEndAsync();
    if (args.Contains("discussion")) {
        await Task.Delay(700);
        Console.WriteLine(input.Contains("BẢN TỔNG HỢP BUỔI SÁNG") ? "TỔNG HỢP GIẢ LẬP\n\nÝ tưởng: thử nghiệm phiên thảo luận ngắn trước khi chạy qua đêm.\nBất đồng: cần đo chi phí thực tế thay vì ước lượng từ số lượt.\nBước tiếp: chọn chủ đề, giới hạn lượt và kiểm chứng giả định." : "ĐÓNG GÓP GIẢ LẬP\n\nĐề xuất: chia bài toán thành giả thuyết có thể kiểm chứng.\nPhản biện: model đồng ý với nhau chưa chứng minh ý tưởng đúng.\nSổ ý tưởng: I1 — thử nghiệm nhỏ, cần xác nhận bằng dữ liệu.\nNhận ý kiến trước: " + input.Contains("[Lượt"));
        return;
    }
    Console.Write(JsonSerializer.Serialize(new { input, args = args.Skip(1).ToArray() })); return;
}
int passed = 0;
var root = Path.Combine(Path.GetTempPath(), "MultiAgentHub-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch (Exception) { return; } throw new Exception("Expected failure"); }
async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
Storage Store() => new(Path.Combine(root, Guid.NewGuid().ToString("N")));
AgentConfig Agent(string provider, string connection, string? name = null) => new() { Provider = provider, Connection = connection, Name = name ?? provider };
async Task Until(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (!condition()) await Task.Delay(10, timeout.Token);
}
try
{
    await Test("fresh and empty profiles do not invent vendor agents", () => {
        var store = Store(); var config = store.LoadConfig();
        Assert(config.Agents.Count == 0 && new HubConfig().Agents.Count == 0);
        Storage.Validate(config); store.Write("native-config", config);
        Assert(store.LoadConfig().Agents.Count == 0);
        var legacy = Store(); File.WriteAllText(Path.Combine(legacy.DataDir, "config.json"), "{}");
        Assert(legacy.LoadConfig().Agents.Count == 0);
        return Task.CompletedTask;
    });
    await Test("project organization survives merge and removal preserves files and history", () => {
        string path = Path.Combine(root, "project-library"); Directory.CreateDirectory(path); string file = Path.Combine(path, "source.txt"); File.WriteAllText(file, "keep");
        var config = new HubConfig { RecentProjects = [path] };
        var conversations = new[] { new ConversationInfo { Project = path } };
        ProjectCatalog.Merge(config, conversations); ProjectCatalog.Change(config, path, new(Name: "Tên hiển thị", Pinned: true, Section: "Nghiên cứu"));
        ProjectCatalog.Change(config, path, new(Removed: true)); ProjectCatalog.Merge(config, conversations);
        Assert(config.Projects.Single().Removed && config.RecentProjects.Count == 0 && File.ReadAllText(file) == "keep");
        var store = Store(); store.Write("native-config", config); var reloaded = store.LoadConfig();
        var restored = ProjectCatalog.Ensure(reloaded, path); Assert(!restored.Removed && restored.Pinned && restored.Name == "Tên hiển thị" && restored.Section == "Nghiên cứu");
        Assert(reloaded.ProjectSections.SequenceEqual(new[] { "Nghiên cứu" })); return Task.CompletedTask;
    });
    await Test("bulk project archive changes only matching metadata and can be restored", () => {
        var store = Store(); var a = new JobRecord { Project = root, Prompt = "A", State = "completed" }; var b = new JobRecord { Project = Path.Combine(root, "other"), Prompt = "B", State = "completed" };
        store.SaveJob(a); store.SaveJob(b); var info = store.Conversations().Single(c => c.Id == a.ConversationId); string text = File.ReadAllText(store.ConversationTextPath(info));
        Assert(store.ArchiveProject(root, true) == 1 && store.ArchiveProject(root, true) == 0);
        var reloaded = new Storage(store.DataDir); Assert(reloaded.Conversations().Single(c => c.Id == a.ConversationId).Archived && !reloaded.Conversations().Single(c => c.Id == b.ConversationId).Archived);
        Assert(File.ReadAllText(store.ConversationTextPath(info)) == text && reloaded.ReadConversationJobs(a.ConversationId).Single().Prompt == "A");
        Assert(reloaded.ArchiveProject(root, false) == 1 && !store.Conversations().Single(c => c.Id == a.ConversationId).Archived); return Task.CompletedTask;
    });
    await Test("catalog separates providers, IDE/2.0/CLI and validates connection", () => {
        Assert(Catalog.Connections("antigravity").Select(c => c.Id).SequenceEqual(new[] { "antigravity-ide", "antigravity-desktop", "agy-cli" }));
        Assert(Catalog.Connections("claude").Any(c => c.Id == "claude-cli"));
        Throws(() => Catalog.Validate(Agent("claude", "codex-cli")));
        var config = new HubConfig { Agents = Enumerable.Range(0, 33).Select(i => Agent("claude", "manual")).ToList() }; Throws(() => Storage.Validate(config));
        return Task.CompletedTask;
    });
    await Test("storage migrates legacy agents/models/order and leaves source intact", () => {
        var store = Store(); string legacy = """
        {"activeProject":"PROJECT","defaultMode":"pipeline","permission":"full","defaultAgents":["anti","code"],"agentDefinitions":[{"id":"anti","kind":"antigravity","name":"Review"},{"id":"code","kind":"codex","name":"Build"}],"agentOptions":{"anti":{"model":"gemini-review"},"code":{"model":"test-model","reasoning":"high"}}}
        """.Replace("PROJECT", root.Replace("\\", "\\\\"));
        File.WriteAllText(Path.Combine(store.DataDir, "config.json"), legacy);
        var config = store.LoadConfig(); Assert(config.Agents[0].Connection == "antigravity-ide" && config.Agents[1].Model == "test-model");
        Assert(config.Agents[1].Permission == "full" && config.Mode == "pipeline");
        Assert(File.ReadAllText(Path.Combine(store.DataDir, "config.json")) == legacy);
        Assert(store.LoadConfig().Agents[0].Id == "anti"); return Task.CompletedTask;
    });
    await Test("CLI invocation has distinct model/permission protocols and stdin", () => {
        var codex = Agent("codex", "codex-cli"); codex.Model = "test-model"; codex.Reasoning = "high"; codex.Permission = "workspace-write";
        var invocation = ProcessTransport.Build(codex, root, "Tiếng Việt", "fixture.exe");
        Assert(invocation.Input == "Tiếng Việt" && invocation.Arguments.Contains("workspace-write") && invocation.Arguments.Contains("test-model"));
        var claude = Agent("claude", "claude-cli"); claude.Model = "sonnet"; claude.Permission = "read-only";
        invocation = ProcessTransport.Build(claude, root, "prompt", "fixture.exe"); Assert(invocation.Arguments.Contains("plan") && invocation.Arguments.Contains("stream-json"));
        var agy = Agent("antigravity", "agy-cli"); invocation = ProcessTransport.Build(agy, root, "Tiếng Việt", "fixture.exe");
        using var doc = JsonDocument.Parse(invocation.Input); Assert(doc.RootElement.GetProperty("message").Str("content") == "Tiếng Việt"); return Task.CompletedTask;
    });
    await Test("native process preserves Unicode stdin and literal arguments", async () => {
        var output = new StringBuilder(); var command = new Invocation(Environment.ProcessPath!, ["--fixture", "$(literal)", "a b", "model/x"], "Xin chào 👋");
        await ProcessTransport.Run(command, root, s => output.Append(s), _ => { }, CancellationToken.None);
        using var doc = JsonDocument.Parse(output.ToString()); Assert(doc.RootElement.Str("input") == "Xin chào 👋"); Assert(doc.RootElement.GetProperty("args")[0].GetString() == "$(literal)");
    });
    await Test("cancellation kills a running native process", async () => {
        using var cancel = new CancellationTokenSource(180); var watch = System.Diagnostics.Stopwatch.StartNew();
        try { await ProcessTransport.Run(new(Environment.ProcessPath!, ["--fixture", "slow"], ""), root, _ => { }, _ => { }, cancel.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Assert(watch.Elapsed < TimeSpan.FromSeconds(5)); }
    });
    await Test("exited CLI does not wait for a descendant holding redirected pipes", async () => {
        var output = new StringBuilder(); var logs = new StringBuilder();
        var watch = System.Diagnostics.Stopwatch.StartNew(); using var timeout = new CancellationTokenSource(10000);
        await ProcessTransport.Run(new(Environment.ProcessPath!, ["--fixture", "background-pipe"], ""), root, s => output.Append(s), s => logs.Append(s), timeout.Token);
        Assert(output.ToString().Contains("root-complete") && logs.ToString().Contains("stdout/stderr"));
        Assert(watch.Elapsed < TimeSpan.FromSeconds(5.5));
    });
    await Test("Codex, Claude and agy stream parsers return final text and reject failure", () => {
        foreach (var pair in new[] {
            ("codex-cli", "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"Đúng\"}}\n"),
            ("claude-cli", "{\"type\":\"result\",\"is_error\":false,\"result\":\"Đúng\"}\n"),
            ("agy-cli", "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"Đúng\"}}\n") })
        { var parser = new CliParser(pair.Item1, _ => { }, _ => { }); parser.Feed(pair.Item2[..10]); parser.Feed(pair.Item2[10..]); Assert(parser.Finish() == "Đúng"); }
        var failure = new CliParser("claude-cli", _ => { }, _ => { }); failure.Feed("{\"type\":\"result\",\"is_error\":true,\"result\":\"bad\"}\n"); Throws(() => failure.Finish()); return Task.CompletedTask;
    });
    await Test("pipeline mixes Codex, Claude, Antigravity with independent model and context", async () => {
        var store = Store(); var fixture = new FixtureExecutor(); var runner = new JobRunner(fixture, store);
        var codex = Agent("codex", "codex-cli", "Coder"); codex.Model = "code-model"; codex.Instructions = "Sửa lỗi";
        var claude = Agent("claude", "claude-cli", "Reviewer"); claude.Model = "review-model";
        var anti = Agent("antigravity", "antigravity-desktop", "Antigravity 2.0");
        runner.Start(new() { Project = root, Mode = "pipeline", Agents = [codex, claude, anti] }, "Yêu cầu gốc");
        await Until(() => runner.Snapshot()!.Results[2].State == "waiting");
        Assert(fixture.Calls[1].Prompt.Contains("result:code-model") && fixture.Calls[0].Prompt.Contains("Sửa lỗi"));
        Assert(runner.Snapshot()!.Results[2].HandoffPrompt.Contains("result:review-model"));
        runner.Reply(anti.Id, "Xong"); await runner.Completion!; Assert(runner.Snapshot()!.State == "completed"); Assert(store.History()[0].Results[2].Text == "Xong");
        Assert(store.History()[0].Results[2].SentPrompt.Contains("result:review-model"));
        Assert(store.History()[0].Results[2].HandoffPrompt == "");
    });
    await Test("parallel manual replies route by ID and cancellation unblocks pending stages", async () => {
        var a = Agent("claude", "manual"); var b = Agent("antigravity", "antigravity-ide"); var runner = new JobRunner(new FixtureExecutor(), Store());
        runner.Start(new() { Project = root, Agents = [a, b] }, "Test"); await Until(() => runner.Snapshot()!.Results.All(r => r.State == "waiting"));
        runner.Reply(b.Id, "reply B"); runner.Cancel(); await runner.Completion!;
        Assert(runner.Snapshot()!.State == "cancelled" && runner.Snapshot()!.Results[1].Text == "reply B");
    });
    await Test("pipeline failure skips later providers and snapshots isolate settings", async () => {
        var fixture = new FixtureExecutor { Fail = true }; var runner = new JobRunner(fixture, Store());
        var a = Agent("claude", "claude-cli"); var b = Agent("codex", "codex-cli");
        runner.Start(new() { Project = root, Mode = "pipeline", Agents = [a, b] }, "Test"); await runner.Completion!;
        Assert(runner.Snapshot()!.Results[1].State == "skipped" && runner.Snapshot()!.State == "failed");
    });
    await Test("Anthropic, Gemini and compatible API requests use direct auth/body and parse results", async () => {
        foreach (string connection in new[] { "anthropic-api", "gemini-api", "chat-api" })
        {
            string provider = connection == "anthropic-api" ? "claude" : connection == "gemini-api" ? "gemini" : "compatible";
            var agent = Agent(provider, connection); agent.Model = "test-model"; agent.Endpoint = "https://example.invalid/v1/";
            var handler = new FakeHandler(async request => {
                Assert(request.RequestUri!.Host == "example.invalid"); Assert(!request.RequestUri.Query.Contains("key"));
                Assert(connection == "anthropic-api" ? request.Headers.GetValues("x-api-key").Single() == "fixture-key" : connection == "gemini-api" ? request.Headers.GetValues("x-goog-api-key").Single() == "fixture-key" : request.Headers.Authorization?.Parameter == "fixture-key");
                string body = await request.Content!.ReadAsStringAsync(); Assert(body.Contains("prompt"));
                return connection == "anthropic-api" ? "{\"content\":[{\"type\":\"text\",\"text\":\"answer\"}]}" : connection == "gemini-api" ? "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"answer\"}]}}]}" : "{\"choices\":[{\"message\":{\"content\":\"answer\"}}]}";
            });
            Assert(await new ApiTransport(new FakeSecrets(), new HttpClient(handler)).Execute(agent, "prompt", CancellationToken.None) == "answer");
        }
    });
    await Test("API model list, endpoint validation and errors avoid leaking secrets", async () => {
        var agent = Agent("claude", "anthropic-api");
        var api = new ApiTransport(new FakeSecrets(), new HttpClient(new FakeHandler(_ => Task.FromResult("{\"data\":[{\"id\":\"claude-test\"}],\"has_more\":false}"))));
        Assert((await api.Models(agent, CancellationToken.None)).Single() == "claude-test");
        agent.Endpoint = "http://remote.example/v1"; Throws(() => ApiTransport.BaseUri(agent)); agent.Endpoint = "http://127.0.0.1:1234/v1"; Assert(ApiTransport.BaseUri(agent).IsLoopback);
    });
    await Test("context rejects outside paths and binary/oversized attachments", () => {
        string folder = Path.Combine(root, "project"); Directory.CreateDirectory(folder); string file = Path.Combine(folder, "file.txt"); File.WriteAllText(file, "hello");
        Assert(ProjectContext.Read(folder, file).Contains("hello")); string outside = Path.Combine(root, "outside.txt"); File.WriteAllText(outside, "private"); Throws(() => ProjectContext.Read(folder, outside));
        File.WriteAllText(file, "binary\0"); Throws(() => ProjectContext.Read(folder, file)); File.WriteAllText(file, new string('x', 512 * 1024 + 1)); Throws(() => ProjectContext.Read(folder, file)); return Task.CompletedTask;
    });
    if (OperatingSystem.IsWindows()) await Test("Windows Credential Manager roundtrip uses independent agent key", () => {
        var secrets = new CredentialSecrets(); string id = "test-" + Guid.NewGuid().ToString("N");
        try { secrets.Write(id, "fixture-key-🔒"); Assert(secrets.Read(id) == "fixture-key-🔒"); secrets.Delete(id); Assert(secrets.Read(id) == ""); } finally { secrets.Delete(id); }
        return Task.CompletedTask;
    });
    await Test("app.ico exists and has valid icon file header", () => {
        string rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string iconPath = Path.Combine(rootDir, "app.ico");
        if (!File.Exists(iconPath)) iconPath = Path.Combine(Directory.GetCurrentDirectory(), "app.ico");
        Assert(File.Exists(iconPath), "app.ico must exist");
        var bytes = File.ReadAllBytes(iconPath);
        Assert(bytes.Length > 1000, "app.ico must be a non-trivial file");
        Assert(bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0, "app.ico header must match Windows Icon format");
        return Task.CompletedTask;
    });
    await Test("preflight failure prevents every agent from starting", () => {
        var fixture = new FixtureExecutor { Reject = "bad" }; var runner = new JobRunner(fixture, Store());
        Throws(() => runner.Start(new() { Project = root, Agents = [Agent("codex", "codex-cli", "good"), Agent("claude", "claude-cli", "bad")] }, "Test"));
        Assert(!runner.Busy && runner.Completion == null && fixture.Calls.Count == 0); return Task.CompletedTask;
    });
    await Test("callback failure kills process without pipe deadlock", async () => {
        var watch = System.Diagnostics.Stopwatch.StartNew(); using var timeout = new CancellationTokenSource(5000);
        try { await ProcessTransport.Run(new(Environment.ProcessPath!, ["--fixture", "flood"], ""), root, _ => throw new InvalidDataException("fixture callback"), _ => { }, timeout.Token); throw new Exception("Expected callback failure"); }
        catch (InvalidDataException e) { Assert(e.Message == "fixture callback" && watch.Elapsed < TimeSpan.FromSeconds(5)); }
    });
    await Test("API HTTP failures never include provider body or key", async () => {
        var agent = Agent("claude", "anthropic-api"); agent.Model = "test";
        var api = new ApiTransport(new FakeSecrets(), new HttpClient(new ErrorHandler()));
        try { await api.Execute(agent, "private prompt", CancellationToken.None); throw new Exception("Expected HTTP failure"); }
        catch (HttpRequestException e) { Assert(e.Message.Contains("401") && !e.Message.Contains("fixture-key") && !e.Message.Contains("private prompt")); }
    });
    await Test("Gemini pagination filters non-text models and omits thoughts", async () => {
        var agent = Agent("gemini", "gemini-api"); agent.Model = "test"; int pages = 0;
        var api = new ApiTransport(new FakeSecrets(), new HttpClient(new FakeHandler(request => {
            if (request.Method == HttpMethod.Post) return Task.FromResult("{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"hidden\"},{\"text\":\"answer\"}]}}]}");
            pages++; if (pages == 1) return Task.FromResult("{\"models\":[{\"name\":\"models/embedding\",\"supportedGenerationMethods\":[\"embedContent\"]}],\"nextPageToken\":\"next token\"}");
            Assert(request.RequestUri!.Query.Contains("pageToken=next%20token")); return Task.FromResult("{\"models\":[{\"name\":\"models/test\",\"supportedGenerationMethods\":[\"generateContent\"]}]}");
        })));
        Assert((await api.Models(agent, CancellationToken.None)).SequenceEqual(new[] { "test" }));
        Assert(await api.Execute(agent, "prompt", CancellationToken.None) == "answer");
    });
    await Test("empty native/legacy teams stay empty and clones isolate settings", () => {
        var store = Store(); File.WriteAllText(Path.Combine(store.DataDir, "config.json"), "{\"agentDefinitions\":[],\"defaultAgents\":[]}");
        Assert(store.LoadConfig().Agents.Count == 0); Assert(store.LoadConfig().Agents.Count == 0);
        var agent = Agent("custom", "custom-cli"); agent.Arguments = ["one"]; var copy = agent.Copy(true); copy.Arguments[0] = "two";
        Assert(copy.Id != agent.Id && agent.Arguments[0] == "one"); return Task.CompletedTask;
    });
    await Test("Markdown export includes original request, models, result and errors", () => {
        var text = Export.Markdown(new() { Prompt = "Tiếng Việt", Project = root, Results = [new() { Name = "Review", Provider = "claude", Model = "sonnet", Text = "Done", Error = "fixture error", Logs = "private log" }] });
        Assert(text.Contains("Tiếng Việt") && text.Contains("Claude / Anthropic") && text.Contains("sonnet") && text.Contains("Done") && text.Contains("fixture error") && !text.Contains("private log")); return Task.CompletedTask;
    });
    await Test("non-object CLI events are logged without interrupting final response", () => {
        var log = new StringBuilder(); var parser = new CliParser("codex-cli", _ => { }, s => log.Append(s));
        parser.Feed("null\n[]\n42\n{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"ok\"}}\n"); Assert(parser.Finish() == "ok" && log.ToString().Contains("null")); return Task.CompletedTask;
    });
    await Test("quota detector separates explicit exhaustion from transient rate limits", () => {
        foreach (string message in new[] { "You've hit your usage limit. Try again later.", "You've hit your limit", "Error: insufficient_quota", "You exceeded your current quota", "Your credit balance is too low", "Hết quota", "Đã vượt hạn mức" }) Assert(Quota.Exhausted(message), message);
        foreach (string message in new[] { "429 Too Many Requests", "rate_limit_error", "RESOURCE_EXHAUSTED", "Rate limit reached, retry in 5s", "quota remaining: 15", "Quota has not been exceeded", "network timeout", "invalid model" }) Assert(!Quota.Exhausted(message), message);
        return Task.CompletedTask;
    });
    await Test("API quota errors are typed and sanitized; transient 429 and 5xx stay ordinary errors", async () => {
        var cases = new[] {
            ("compatible", "chat-api", HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"private fixture-key\"}}", true),
            ("claude", "anthropic-api", HttpStatusCode.BadRequest, "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"You have reached your spend limit. private fixture-key\"}}", true),
            ("gemini", "gemini-api", HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":\"quota_exceeded\",\"message\":\"private fixture-key\"}}", true),
            ("claude", "anthropic-api", HttpStatusCode.TooManyRequests, "{\"error\":{\"type\":\"rate_limit_error\",\"message\":\"Too many requests\"}}", false),
            ("gemini", "gemini-api", HttpStatusCode.TooManyRequests, "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\",\"message\":\"Quota exceeded\",\"details\":[{\"quotaId\":\"RequestsPerMinute\"}]}}", false),
            ("compatible", "chat-api", HttpStatusCode.InternalServerError, "{\"error\":{\"code\":\"insufficient_quota\"}}", false)
        };
        foreach (var (provider, connection, status, body, quota) in cases) {
            var agent = Agent(provider, connection); agent.Model = "fixture";
            var api = new ApiTransport(new FakeSecrets(), new HttpClient(new FakeHandler(_ => Task.FromResult(body), status)));
            try { await api.Execute(agent, "prompt", CancellationToken.None); throw new Exception("Expected API error"); }
            catch (Exception e) when (e is AgentQuotaException or HttpRequestException) { Assert((e is AgentQuotaException) == quota); Assert(!e.Message.Contains("fixture-key") && !e.Message.Contains("private")); }
        }
    });
    await Test("CLI quota errors survive nonzero exit and structured provider events", async () => {
        foreach (var (connection, json) in new[] {
            ("codex-cli", "{\"type\":\"turn.failed\",\"error\":{\"code\":\"usage_limit_reached\"}}\n"),
            ("claude-cli", "{\"type\":\"result\",\"is_error\":true,\"errors\":[\"You've hit your limit\"]}\n"),
            ("claude-cli", "{\"type\":\"assistant\",\"error\":\"rate_limit\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"You've hit your usage limit\"}]}}\n"),
            ("agy-cli", "{\"event\":\"result\",\"result\":{\"status\":\"ERROR\",\"error\":{\"code\":\"quota_exceeded\"}}}\n")
        }) {
            var parser = new CliParser(connection, _ => { }, _ => { }); parser.Feed(json);
            try { parser.Finish(); throw new Exception("Expected quota error"); } catch (AgentQuotaException) { }
        }
        var agent = Agent("custom", "custom-cli"); agent.Command = Environment.ProcessPath!; agent.Arguments = ["--fixture", "quota"];
        try { await new AgentExecutor(new ApiTransport(new FakeSecrets())).Execute(agent, root, "prompt", _ => { }, _ => { }, CancellationToken.None); throw new Exception("Expected quota exit"); } catch (AgentQuotaException) { }
    });
    await Test("successful answers discussing quota are not treated as exhaustion", async () => {
        var parser = new CliParser("claude-cli", _ => { }, _ => { }); parser.Feed("{\"type\":\"result\",\"is_error\":false,\"result\":\"You've hit your usage limit is an example error\"}\n"); Assert(parser.Finish().Contains("example"));
        var agent = Agent("compatible", "chat-api"); agent.Model = "fixture";
        var api = new ApiTransport(new FakeSecrets(), new HttpClient(new FakeHandler(_ => Task.FromResult("{\"choices\":[{\"message\":{\"content\":\"quota exceeded is an example\"}}]}"))));
        Assert((await api.Execute(agent, "prompt", CancellationToken.None)).Contains("example"));
    });
    await Test("pipeline skips exhausted agents, keeps good context and identifies only exhausted IDs", async () => {
        var first = Agent("codex", "codex-cli", "first"); first.Model = "good";
        var bad = Agent("claude", "claude-cli", "bad"); bad.Model = "bad";
        var last = Agent("codex", "codex-cli", "last"); last.Model = "last";
        var fixture = new FixtureExecutor(); fixture.QuotaNames.Add("bad"); var store = Store(); var runner = new JobRunner(fixture, store); var disabled = new List<string>(); runner.AgentQuotaReached += disabled.Add;
        runner.Start(new() { Project = root, Mode = "pipeline", Agents = [first, bad, last] }, "Original request"); await runner.Completion!;
        var job = runner.Snapshot()!; Assert(job.State == "partial" && job.Results[1].State == "quota" && job.Results[2].State == "success");
        Assert(disabled.SequenceEqual(new[] { bad.Id }) && fixture.Calls[2].Prompt.Contains("Original request") && fixture.Calls[2].Prompt.Contains("result:good") && !fixture.Calls[2].Prompt.Contains("result:bad"));
        Assert(store.History()[0].Results[1].State == "quota");
    });
    await Test("parallel exhaustion preserves other results; all exhausted finishes without retries", async () => {
        var fixture = new FixtureExecutor(); fixture.QuotaNames.UnionWith(new[] { "bad1", "bad2" }); var runner = new JobRunner(fixture, Store()); var disabled = new System.Collections.Concurrent.ConcurrentBag<string>(); runner.AgentQuotaReached += disabled.Add;
        var a = Agent("codex", "codex-cli", "bad1"); var b = Agent("claude", "claude-cli", "bad2"); var good = Agent("codex", "codex-cli", "good");
        runner.Start(new() { Project = root, Agents = [a, b, good] }, "Test"); await runner.Completion!;
        Assert(runner.Snapshot()!.State == "partial" && runner.Snapshot()!.Results[2].State == "success" && disabled.Count == 2);
        runner.Start(new() { Project = root, Mode = "pipeline", Agents = [a, b] }, "Test again"); await runner.Completion!;
        Assert(!runner.Busy && runner.Snapshot()!.State == "quota" && fixture.Calls.Count == 5);
    });
    await Test("manual quota report unblocks pipeline; stale reports and normal errors do not disable agents", async () => {
        var manual = Agent("antigravity", "antigravity-desktop"); var next = Agent("codex", "codex-cli"); var runner = new JobRunner(new FixtureExecutor(), Store()); var disabled = new List<string>(); runner.AgentQuotaReached += disabled.Add;
        runner.Start(new() { Project = root, Mode = "pipeline", Agents = [manual, next] }, "Original"); await Until(() => runner.Snapshot()!.Results[0].State == "waiting");
        runner.ReportQuota(manual.Id); await runner.Completion!;
        Assert(disabled.SequenceEqual(new[] { manual.Id }) && runner.Snapshot()!.Results[1].State == "success"); Throws(() => runner.ReportQuota(manual.Id));
        var errors = new JobRunner(new FixtureExecutor { Fail = true }, Store()); int calls = 0; errors.AgentQuotaReached += _ => calls++;
        errors.Start(new() { Project = root, Agents = [next] }, "Test"); await errors.Completion!; Assert(calls == 0 && errors.Snapshot()!.State == "failed");
    });
    await Test("structured quota immediately stops a CLI even if it remains running", async () => {
        var parser = new CliParser("codex-cli", _ => { }, _ => { }); var watch = System.Diagnostics.Stopwatch.StartNew(); using var timeout = new CancellationTokenSource(5000);
        try { await ProcessTransport.Run(new(Environment.ProcessPath!, ["--fixture", "quota-event"], ""), root, value => { parser.Feed(value); parser.ThrowIfQuota(); }, _ => { }, timeout.Token); throw new Exception("Expected immediate quota"); }
        catch (AgentQuotaException) { Assert(watch.Elapsed < TimeSpan.FromSeconds(5)); }
    });
    await Test("discussion alternates models, shares context and produces a morning report", async () => {
        var first = Agent("codex", "codex-cli", "Proposer"); first.Model = "alpha"; first.Permission = "full";
        var second = Agent("gemini", "gemini-api", "Critic"); second.Model = "beta";
        var store = Store(); var fixture = new DiscussionFixture(); var runner = new JobRunner(fixture, store);
        var config = new HubConfig { Project = root, Mode = "discussion", Agents = [first, second], Discussion = new() { MaxTurns = 4, DurationMinutes = 10 } };
        runner.Start(config, "Original topic", "Attached context"); config.Discussion.MaxTurns = 400;
        try { await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { runner.Cancel(); await runner.Completion!; }
        var job = runner.Snapshot()!;
        Assert(fixture.Calls.Select(c => c.Model).SequenceEqual(new[] { "alpha", "beta", "alpha", "beta", "alpha" }));
        Assert(fixture.Calls.Where(c => c.Model == "alpha").All(c => c.Permission == "full") && first.Permission == "full");
        Assert(fixture.Calls.Where(c => c.Model == "beta").All(c => c.Permission == second.Permission));
        Assert(fixture.Calls[1].Prompt.Contains("idea-1") && fixture.Calls[3].Prompt.Contains("idea-3"));
        Assert(fixture.Calls.All(c => c.Prompt.Contains("Original topic") && c.Prompt.Contains("Attached context")));
        Assert(job.State == "completed" && job.ReportByModel && job.Report == "idea-5" && job.Turns.Count == 5 && job.Discussion!.MaxTurns == 4);
        Assert(store.History()[0].Turns.Count == 5 && !File.Exists(Path.Combine(store.DataDir, "native-active-discussion.json")));
        string markdown = Export.Markdown(job); Assert(markdown.Contains("Tổng hợp") && markdown.Contains("idea-1") && markdown.Contains("idea-5") && markdown.Contains("Lượt 4"));
    });
    await Test("discussion quota disables only the exhausted model and continues solo", async () => {
        var first = Agent("codex", "codex-cli", "exhausted"); var second = Agent("claude", "claude-cli", "available");
        var fixture = new DiscussionFixture { Behavior = (agent, n) => agent.Name == "exhausted" ? throw new AgentQuotaException() : "solo-" + n };
        var runner = new JobRunner(fixture, Store()); var disabled = new List<string>(); runner.AgentQuotaReached += disabled.Add;
        runner.Start(new() { Project = root, Mode = "discussion", Agents = [first, second], Discussion = new() { MaxTurns = 4, PauseSeconds = 0 } }, "topic"); await runner.Completion!;
        Assert(disabled.SequenceEqual(new[] { first.Id }) && fixture.Calls.Count(c => c.Name == "exhausted") == 1);
        Assert(fixture.Calls.Skip(1).All(c => c.Name == "available") && fixture.Calls[2].Prompt.Contains("tự phản biện"));
        Assert(runner.Snapshot()!.State == "partial" && runner.Snapshot()!.ReportByModel);
    });
    await Test("discussion stops when all models exhaust; ordinary errors do not uncheck", async () => {
        var agents = new List<AgentConfig> { Agent("codex", "codex-cli"), Agent("claude", "claude-cli") };
        var fixture = new DiscussionFixture { Behavior = (_, _) => throw new AgentQuotaException() }; var runner = new JobRunner(fixture, Store());
        runner.Start(new() { Project = root, Mode = "discussion", Agents = agents, Discussion = new() { MaxTurns = 100, PauseSeconds = 0 } }, "topic"); await runner.Completion!;
        Assert(fixture.Calls.Count == 2 && runner.Snapshot()!.State == "quota" && !runner.Snapshot()!.ReportByModel);
        fixture = new() { Behavior = (a, _) => a.Provider == "codex" ? throw new IOException("network") : "ok" }; runner = new(fixture, Store()); int quota = 0; runner.AgentQuotaReached += _ => quota++;
        runner.Start(new() { Project = root, Mode = "discussion", Agents = agents, Discussion = new() { MaxTurns = 3, PauseSeconds = 0 } }, "topic"); await runner.Completion!;
        Assert(quota == 0 && fixture.Calls.Count(c => c.Name == "codex") == 1 && runner.Snapshot()!.ReportByModel);
    });
    await Test("discussion cancellation interrupts pause, keeps checkpoint and avoids summary calls", async () => {
        var store = Store(); var fixture = new DiscussionFixture(); var runner = new JobRunner(fixture, store);
        runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("codex", "codex-cli")], Discussion = new() { MaxTurns = 50, PauseSeconds = 3600 } }, "topic");
        await Until(() => runner.Snapshot()!.DiscussionPhase == "resting");
        var recovered = store.History()[0]; Assert(recovered.State == "interrupted" && recovered.Turns.Count == 1 && recovered.Report.Contains("idea-1"));
        runner.Cancel(); await runner.Completion!;
        Assert(fixture.Calls.Count == 1 && runner.Snapshot()!.State == "cancelled" && !runner.Snapshot()!.ReportByModel && runner.Snapshot()!.Report.Contains("idea-1"));
        Assert(store.History()[0].State == "cancelled");
    });
    await Test("discussion input wakes pause, reaches next prompt and survives export/reopen", async () => {
        var store = Store(); var fixture = new DiscussionFixture(); var runner = new JobRunner(fixture, store);
        runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("codex", "codex-cli")], Discussion = new() { MaxTurns = 2, PauseSeconds = 3600 } }, "topic");
        try {
            await Until(() => runner.Snapshot()!.DiscussionPhase == "resting");
            runner.AddDiscussionInstruction("Focus on failure recovery, not more features.");
            await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10));
            var job = runner.Snapshot()!; var input = job.Turns.Single(t => t.Kind == "user");
            Assert(!runner.Busy && job.State == "completed" && job.ReportByModel && fixture.Calls.Count == 3);
            Assert(fixture.Calls[1].Prompt.Contains(input.Text) && fixture.Calls[2].Prompt.Contains(input.Text));
            Assert(input.State == "success" && input.Number == 2 && job.Turns.Where(t => t.Kind != "user").Select(t => t.Number).SequenceEqual(new[] { 1, 2, 3 }));
            var saved = store.ReadConversationJobs(job.ConversationId).Single();
            Assert(saved.Turns.Any(t => t.Id == input.Id && t.Text == input.Text) && Export.PlainText(saved).Contains("BẠN BỔ SUNG") && Export.Markdown(saved).Contains(input.Text));
            Assert(store.ConversationContext(job.ConversationId, job.Id).Contains(input.Text));
            Throws(() => runner.AddDiscussionInstruction("too late"));
        } finally { runner.Cancel(); await runner.Completion!; }
    });
    await Test("discussion can skip a pause and finish with summary without another discussion call", async () => {
        var fixture = new DiscussionFixture(); var runner = new JobRunner(fixture, Store());
        runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("codex", "codex-cli")], Discussion = new() { MaxTurns = 50, PauseSeconds = 3600 } }, "topic");
        try {
            await Until(() => runner.Snapshot()!.DiscussionPhase == "resting"); runner.NextDiscussionTurn();
            await Until(() => runner.Snapshot() is { DiscussionPhase: "resting" } job && job.Turns.Count == 2);
            runner.FinishDiscussion(); await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10));
            var job = runner.Snapshot()!;
            Assert(job.State == "completed" && job.ReportByModel && job.DiscussionFinishRequested && job.NextTurnAt == null);
            Assert(job.Turns.Count(t => t.Kind == "discussion") == 2 && fixture.Calls.Count == 3 && job.StopReason.Contains("Người dùng kết thúc"));
        } finally { runner.Cancel(); await runner.Completion!; }
    });
    await Test("discussion finishing preserves in-flight output and includes queued input in summary", async () => {
        var fixture = new InteractiveDiscussionFixture(); var runner = new JobRunner(fixture, Store());
        runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("codex", "codex-cli")], Discussion = new() { MaxTurns = 50, PauseSeconds = 3600 } }, "topic");
        try {
            await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            runner.AddDiscussionInstruction("Summarize the remaining risks."); runner.FinishDiscussion();
            Throws(() => runner.AddDiscussionInstruction("late input"));
            Assert(runner.Busy && !fixture.FirstToken.IsCancellationRequested);
            fixture.Release.TrySetResult(); await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10));
            var job = runner.Snapshot()!;
            Assert(job.State == "completed" && job.Turns.First().Text == "finished first turn" && job.Report == "final summary");
            Assert(fixture.Prompts.Count == 2 && fixture.Prompts[1].Contains("Summarize the remaining risks.") && job.Turns.Single(t => t.Kind == "user").Number == 2);
        } finally { fixture.Release.TrySetResult(); runner.Cancel(); await runner.Completion!; }
    });
    await Test("discussion deadline bounds turns and summary; failed summary preserves ideas", async () => {
        var clock = new DiscussionClock(); var fixture = new DiscussionFixture { Behavior = (_, n) => { clock.Advance(TimeSpan.FromSeconds(n == 1 ? 55 : 10)); return "deadline-idea"; } };
        var runner = new JobRunner(fixture, Store(), clock);
        runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("codex", "codex-cli")], Discussion = new() { MaxTurns = 400, DurationMinutes = 1, PauseSeconds = 0 } }, "topic"); await runner.Completion!;
        Assert(fixture.Calls.Count == 2 && runner.Snapshot()!.Turns.Count(t => t.Kind == "discussion") == 1);
        fixture = new() { Behavior = (_, n) => n == 2 ? throw new AgentQuotaException() : "kept-idea" }; runner = new(fixture, Store());
        runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("codex", "codex-cli")], Discussion = new() { MaxTurns = 1, PauseSeconds = 0 } }, "topic"); await runner.Completion!;
        Assert(runner.Snapshot()!.State == "partial" && !runner.Snapshot()!.ReportByModel && runner.Snapshot()!.Report.Contains("kept-idea"));
    });
    await Test("discussion preflight rejects manual and invalid limits before inference", () => {
        var fixture = new DiscussionFixture(); var runner = new JobRunner(fixture, Store());
        Throws(() => runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("codex", "codex-cli"), Agent("antigravity", "antigravity-ide")] }, "topic"));
        foreach (var invalid in new[] { new DiscussionOptions { MaxTurns = 0 }, new DiscussionOptions { MaxTurns = 401 }, new DiscussionOptions { DurationMinutes = 1441 }, new DiscussionOptions { PauseSeconds = -1 } }) Throws(invalid.Validate);
        Assert(fixture.Calls.Count == 0 && !runner.Busy); return Task.CompletedTask;
    });
    await Test("discussion summary falls back after quota and active cancellation kills the turn", async () => {
        var agents = new List<AgentConfig> { Agent("codex", "codex-cli"), Agent("claude", "claude-cli") };
        var fixture = new DiscussionFixture { Behavior = (_, n) => n == 3 ? throw new AgentQuotaException() : "idea-" + n };
        var runner = new JobRunner(fixture, Store()); var disabled = new List<string>(); runner.AgentQuotaReached += disabled.Add;
        runner.Start(new() { Project = root, Mode = "discussion", Agents = agents, Discussion = new() { MaxTurns = 2, PauseSeconds = 0 } }, "topic"); await runner.Completion!;
        Assert(fixture.Calls.Count == 4 && runner.Snapshot()!.Report == "idea-4" && disabled.SequenceEqual(new[] { agents[0].Id }));
        var store = Store(); runner = new(new BlockingDiscussionFixture(), store);
        runner.Start(new() { Project = root, Mode = "discussion", Agents = [agents[1]], Discussion = new() { MaxTurns = 10, PauseSeconds = 0 } }, "topic");
        await Until(() => runner.Snapshot()!.Turns.Count > 0); runner.Cancel(); await runner.Completion!;
        Assert(runner.Snapshot()!.State == "cancelled" && runner.Snapshot()!.Turns.Single().State == "cancelled" && store.History()[0].Turns.Count == 1);
    });
    await Test("discussion context stays bounded and includes newest contributions", () => {
        var job = new JobRecord { Turns = Enumerable.Range(1, 400).Select(n => new DiscussionTurn { Number = n, State = "success", Name = new string('n', 80), Model = new string('m', 120), Text = "contribution-" + n + new string('x', 16000) }).ToList() };
        string context = DiscussionEngine.Context(job);
        Assert(context.Length < 60000 && context.Contains("contribution-400") && context.Contains("contribution-395")); return Task.CompletedTask;
    });
    await Test("starting a new discussion preserves an interrupted conversation", async () => {
        var store = Store(); var interrupted = new JobRecord { Mode = "discussion", Prompt = "old topic", Discussion = new(), Turns = [new() { Number = 1, State = "success", Text = "old idea" }, new() { Number = 2 }] };
        store.Checkpoint(interrupted); var runner = new JobRunner(new DiscussionFixture(), store);
        runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("codex", "codex-cli")], Discussion = new() { MaxTurns = 1, PauseSeconds = 0 } }, "new topic"); await runner.Completion!;
        var history = store.History(); Assert(history.Count == 2 && history[1].Id == interrupted.Id && history[1].State == "interrupted" && history[1].Turns[1].State == "cancelled");
    });
    await Test("conversation text keeps Unicode topic/date, all messages and safe filenames", () => {
        var store = Store(); var first = new JobRecord { ConversationId = "chat-a", Title = "Ý tưởng: AI / đêm?", Prompt = "Nội dung đầu", Project = root, State = "completed", Results = [new() { Name = "Claude", Model = "fixture", Text = "Phản biện tiếng Việt" }] };
        store.SaveJob(first);
        var second = new JobRecord { ConversationId = first.ConversationId, Title = "ignored follow-up title", Prompt = "Nội dung tiếp theo", Project = root, State = "completed", PreviousJobId = first.Id };
        store.SaveJob(second);
        var info = store.Conversations().Single(); string path = store.ConversationTextPath(info); string text = File.ReadAllText(path);
        Assert(Path.GetFileName(path).StartsWith("Ý tưởng") && Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Path.GetFileName(path).Contains(first.Timestamp.LocalDateTime.ToString("dd-MM-yyyy")));
        Assert(text.Contains("Nội dung đầu") && text.Contains("Nội dung tiếp theo") && text.Contains("Phản biện tiếng Việt") && !text.Contains("ignored follow-up title"));
        Assert(store.ReadConversationJobs(info.Id).Count == 2);
        var collision = new JobRecord { ConversationId = "chat-b", Title = first.Title, Prompt = "Different chat", Project = root, State = "completed", Timestamp = first.Timestamp };
        store.SaveJob(collision); Assert(store.Conversations().Count == 2 && store.ConversationTextPath(store.Conversations().First(c => c.Id == "chat-b")) != path);
        Throws(() => store.ReadConversationJobs("../escape")); return Task.CompletedTask;
    });
    await Test("continuing conversation sends prior context and appends within the same topic", async () => {
        var store = Store(); var fixture = new FixtureExecutor(); var runner = new JobRunner(fixture, store);
        var config = new HubConfig { Project = root, Agents = [Agent("codex", "codex-cli")] }; config.Agents[0].Model = "first-model";
        runner.Start(config, "First requirement", title: "Original topic"); await runner.Completion!; var previous = runner.Snapshot()!;
        config.Agents[0].Model = "second-model";
        runner.Start(config, "Follow-up", previous: previous, title: "new title must not replace topic"); await runner.Completion!; var continued = runner.Snapshot()!;
        Assert(continued.ConversationId == previous.ConversationId && continued.PreviousJobId == previous.Id && continued.Title == "Original topic");
        Assert(fixture.Calls[1].Prompt.Contains("First requirement") && fixture.Calls[1].Prompt.Contains("result:first-model") && fixture.Calls[1].Prompt.Contains("Follow-up"));
        Assert(store.Conversations().Count == 1 && store.ReadConversationJobs(previous.ConversationId).Count == 2);
        string otherProject = Path.Combine(root, "other-project"); Directory.CreateDirectory(otherProject); config.Project = otherProject;
        Throws(() => runner.Start(config, "Wrong project", previous: previous)); Assert(fixture.Calls.Count == 2 && !runner.Busy);
    });
    await Test("resumed discussion receives old report with a fresh bounded turn budget", async () => {
        var store = Store(); var fixture = new DiscussionFixture(); var runner = new JobRunner(fixture, store);
        var config = new HubConfig { Project = root, Mode = "discussion", Agents = [Agent("codex", "codex-cli")], Discussion = new() { MaxTurns = 2, PauseSeconds = 0 } };
        runner.Start(config, "First topic"); await runner.Completion!; var previous = runner.Snapshot()!;
        runner.Start(config, "Critique the earlier report", previous: previous); await runner.Completion!;
        Assert(fixture.Calls.Count == 6 && fixture.Calls[3].Prompt.Contains(previous.Report) && fixture.Calls[3].Prompt.Contains("First topic"));
        Assert(runner.Snapshot()!.Turns.Count == 3 && store.ReadConversationJobs(previous.ConversationId).Count == 2);
    });
    await Test("library retains conversations beyond recent history and imports legacy only once", () => {
        var store = Store(); var jobs = Enumerable.Range(0, 55).Select(n => new JobRecord { Project = root, Prompt = "Archived " + n, State = "completed" }).ToList();
        foreach (var job in jobs) store.SaveJob(job);
        Assert(store.History().Count == 50 && store.Conversations().Count == 55);
        var reopened = new Storage(store.DataDir); reopened.ImportHistory(); reopened.ImportHistory();
        Assert(reopened.Conversations().Count == 55 && reopened.ReadConversationJobs(jobs[0].ConversationId).Count == 1); return Task.CompletedTask;
    });
    await Test("unfinished archives reopen as interrupted without executing models", () => {
        var store = Store(); var job = new JobRecord { Project = root, Prompt = "Interrupted topic", Results = [new() { State = "waiting", HandoffPrompt = "stale prompt", Text = "saved part" }] };
        store.SaveConversationJob(job);
        var restored = new Storage(store.DataDir).ReadConversationJobs(job.ConversationId).Single();
        Assert(restored.State == "interrupted" && restored.Results[0].State == "cancelled" && restored.Results[0].HandoffPrompt == "" && restored.Results[0].Text == "saved part");
        Assert(File.ReadAllText(store.ConversationTextPath(store.Conversations().Single())).Contains("saved part")); return Task.CompletedTask;
    });
    await Test("Antigravity model list strips ANSI and CLI plan mode is enforced", () => {
        var models = AntigravityModels.Parse("Available models:\n\u001b[32mgemini-3.8-flash-high\u001b[0m\tGemini 3.8 Flash (High)\nclaude-sonnet-4-6  Claude Sonnet\n");
        Assert(models.SequenceEqual(new[] { "gemini-3.8-flash-high", "claude-sonnet-4-6" }));
        var agent = Agent("antigravity", "agy-cli"); agent.Permission = "read-only";
        var invocation = ProcessTransport.Build(agent, root, "topic", "agy.exe");
        Assert(invocation.Arguments.Contains("--mode") && invocation.Arguments.Contains("plan") && !invocation.Arguments.Contains("--dangerously-skip-permissions")); return Task.CompletedTask;
    });
    await Test("chat uses reply-only context and read-only copies without changing work permissions", async () => {
        var store = Store(); var fixture = new DiscussionFixture(); var runner = new JobRunner(fixture, store);
        var a = Agent("codex", "codex-cli", "Coder"); a.Permission = "full";
        runner.Start(new() { Project = root, Surface = "chat", Mode = "parallel", Agents = [a] }, "Giải thích một ý tưởng"); await runner.Completion!;
        Assert(fixture.Calls.Single().Permission == "read-only" && fixture.Calls.Single().Prompt.Contains("Không dùng công cụ"));
        Assert(!fixture.Calls.Single().Prompt.StartsWith("Project:") && a.Permission == "full");
        Assert(store.History()[0].Surface == "chat");
        Throws(() => Storage.Validate(new() { Surface = "unknown" }));
    });
    await Test("collaboration shares review feedback then returns to lead within bounded calls", async () => {
        var store = Store(); var fixture = new DiscussionFixture(); var runner = new JobRunner(fixture, store);
        var a = Agent("codex", "codex-cli", "Lead"); a.Permission = "workspace-write";
        var b = Agent("antigravity", "agy-cli", "Reviewer");
        runner.Start(new() { Project = root, Mode = "collaboration", Agents = [a, b] }, "Build and review"); await runner.Completion!;
        var job = runner.Snapshot()!;
        Assert(fixture.Calls.Select(c => c.Name).SequenceEqual(new[] { "Lead", "Reviewer", "Lead" }));
        Assert(fixture.Calls[1].Prompt.Contains("idea-1") && fixture.Calls[2].Prompt.Contains("idea-2"));
        Assert(fixture.Calls[0].Permission == "workspace-write" && fixture.Calls[2].Prompt.Contains("Không mở thêm vòng"));
        Assert(job.State == "completed" && job.Turns.Count == 3 && job.Report == "idea-3");
        Assert(Export.PlainText(job).Contains("idea-1") && Export.Markdown(job).Contains("idea-2"));
        Assert(store.ReadConversationJobs(job.ConversationId)[0].Turns.Count == 3);
    });
    await Test("collaboration handles quota and one-agent teams without repeated calls", async () => {
        var fixture = new DiscussionFixture { Behavior = (agent, _) => agent.Name == "bad" ? throw new AgentQuotaException() : "good" };
        var runner = new JobRunner(fixture, Store()); var disabled = new List<string>(); runner.AgentQuotaReached += disabled.Add;
        var a = Agent("codex", "codex-cli", "bad"); var b = Agent("antigravity", "agy-cli", "good");
        runner.Start(new() { Project = root, Mode = "collaboration", Agents = [a, b] }, "Work"); await runner.Completion!;
        Assert(fixture.Calls.Count == 2 && disabled.SequenceEqual(new[] { a.Id }) && runner.Snapshot()!.State == "partial");
        fixture.Calls.Clear(); runner.Start(new() { Project = root, Mode = "collaboration", Agents = [b] }, "Solo"); await runner.Completion!;
        Assert(fixture.Calls.Count == 1 && runner.Snapshot()!.Turns.Count == 1);
    });
    await Test("pinned renamed archived conversations survive reload and preserve transcript", () => {
        var store = Store(); var job = new JobRecord { Project = root, Prompt = "Original", State = "completed", Results = [new() { Text = "Saved answer", State = "success" }] };
        store.SaveJob(job); store.UpdateConversation(job.ConversationId, "Tên mới", true, true);
        var reloaded = new Storage(store.DataDir); var info = reloaded.Conversations().Single();
        Assert(info.Title == "Tên mới" && info.Pinned && info.Archived);
        Assert(File.ReadAllText(reloaded.ConversationTextPath(info)).Contains("Tên mới") && File.ReadAllText(reloaded.ConversationTextPath(info)).Contains("Saved answer"));
        store.SaveJob(job); Assert(store.Conversations().Single().Title == "Tên mới" && store.Conversations().Single().Archived);
        store.UpdateConversation(job.ConversationId, archived:false); Assert(!store.Conversations().Single().Archived);
        Throws(() => store.UpdateConversation("../outside", "bad")); return Task.CompletedTask;
    });
    await ConsensusTests.Run(Test, root);
    Console.WriteLine($"{passed} native tests passed");
}
catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
finally { Directory.Delete(root, true); }

sealed class FixtureExecutor : IAgentExecutor
{
    public bool Fail { get; set; }
    public string Reject { get; set; } = "";
    public HashSet<string> QuotaNames { get; } = [];
    public void Validate(AgentConfig agent) { if (agent.Name == Reject) throw new ArgumentException("fixture preflight"); }
    public List<(string Model, string Prompt)> Calls { get; } = [];
    public async Task<string> Execute(AgentConfig a, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
    { await Task.Delay(10, token); lock (Calls) Calls.Add((a.Model, prompt)); if (QuotaNames.Contains(a.Name)) throw new AgentQuotaException(); if (Fail) throw new Exception("fixture error"); return "result:" + a.Model; }
}
sealed class DiscussionFixture : IAgentExecutor
{
    public List<(string Name, string Model, string Permission, string Prompt)> Calls { get; } = [];
    public Func<AgentConfig, int, string>? Behavior { get; set; }
    public async Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
    {
        await Task.Delay(1, token); Calls.Add((agent.Name, agent.Model, agent.Permission, prompt));
        string answer = Behavior?.Invoke(agent, Calls.Count) ?? "idea-" + Calls.Count; text(answer); return answer;
    }
}
sealed class DiscussionClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan elapsed) => now += elapsed;
}
sealed class InteractiveDiscussionFixture : IAgentExecutor
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<string> Prompts { get; } = [];
    public CancellationToken FirstToken { get; private set; }
    public async Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
    {
        Prompts.Add(prompt);
        if (Prompts.Count > 1) return "final summary";
        FirstToken = token; Started.TrySetResult(); await Release.Task.WaitAsync(token);
        return "finished first turn";
    }
}
sealed class BlockingDiscussionFixture : IAgentExecutor
{
    public async Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
    { text("partial idea"); await Task.Delay(Timeout.Infinite, token); return "unreachable"; }
}
sealed class FakeSecrets : ISecretStore
{
    public string Read(string id) => "fixture-key"; public void Write(string id, string secret) => throw new NotSupportedException(); public void Delete(string id) => throw new NotSupportedException();
}
sealed class FakeHandler(Func<HttpRequestMessage, Task<string>> handler, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => new(status) { Content = new StringContent(await handler(request), Encoding.UTF8, "application/json") };
}
sealed class ErrorHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("fixture-key private prompt") });
}
