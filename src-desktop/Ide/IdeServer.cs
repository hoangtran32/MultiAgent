using System.Text;
using System.Text.Json;
using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal static class IdeServer
{
    public static async Task<int> Run(string project, string? dataDir)
    {
        var global = new Storage(dataDir);
        var store = new Storage(IdeSession.WorkspaceProfile(global.DataDir, project));
        // File ownership is per workspace; independent editor windows never share a runner/checkpoint.
        using var ownership = new FileStream(Path.Combine(store.DataDir, "backend.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var secrets = new CredentialSecrets(); var accounts = new ChatGptPlan(new ChatGptPlanStore(global.DataDir));
        var session = new IdeSession(project, store, global.LoadConfig, new AgentExecutor(new ApiTransport(secrets), chatGptPlan: accounts));
        var known = store.History().Select(j => j.Id).ToHashSet();
        foreach (var old in global.History().Where(j => j.Surface == "work" && Path.IsPathFullyQualified(j.Project) && string.Equals(Path.GetFullPath(j.Project), session.Project, StringComparison.OrdinalIgnoreCase) && !known.Contains(j.Id))) store.SaveJob(old);
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true));
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var outputGate = new SemaphoreSlim(1); int dirty = 1;
        var metadata = new List<Task>();
        session.Runner.ActivityChanged += () => Interlocked.Exchange(ref dirty, 1);
        using var lifetime = new CancellationTokenSource();
        async Task Write(object message) { string line = IdeWire.Serialize(message); await outputGate.WaitAsync(); try { await output.WriteLineAsync(line); } finally { outputGate.Release(); } }
        var updates = Task.Run(async () => {
            try {
                int tick = 0;
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(180));
                while (await timer.WaitForNextTickAsync(lifetime.Token)) {
                    tick++;
                    bool changed = Interlocked.Exchange(ref dirty, 0) != 0;
                    if (changed || tick % 6 == 0 && session.Runner.Busy) await Write(new { @event = "state", data = session.Snapshot() });
                    if (tick % 12 == 0 && session.Runner.Snapshot() is { } checkpoint) { if (session.Runner.Busy) store.Checkpoint(checkpoint); else store.ClearCheckpoint(checkpoint.Id); }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { lifetime.Cancel(); session.Runner.Cancel(); }
        });
        try {
            while (!lifetime.IsCancellationRequested) {
                string? line = await input.ReadLineAsync(lifetime.Token); if (line == null) break;
                if (line.Length > 2_000_000) throw new InvalidDataException("IDE request quá lớn.");
                string id = "";
                try {
                    using var doc = JsonDocument.Parse(line); var request = doc.RootElement;
                    id = request.GetProperty("id").GetString() ?? "";
                    if (id.Length is < 1 or > 80) throw new ArgumentException("ID yêu cầu không hợp lệ.");
                    string method = request.GetProperty("method").GetString() ?? "";
                    var data = request.TryGetProperty("params", out var p) ? p : JsonSerializer.SerializeToElement(new { });
                    object result;
                    switch (method) {
                        case "initialize":
                            var config = session.Config();
                            result = new { project = session.Project, coding = session.Coding(),
                                agents = config.Agents.Where(a => !Catalog.IsManual(a.Connection) && !Catalog.IsBrowserChat(a.Connection) && a.Connection != "gateway-web").Select(a => new { a.Id, a.Name, a.Model, a.Permission, a.Enabled, a.Connection, models = LocalModels.Read(a) }),
                                history = store.History().Select(j => new { j.Id, j.Title, j.State, j.Timestamp }), state = session.Snapshot() };
                            break;
                        case "start":
                            var start = data.Deserialize<IdeStart>(Json.Options) ?? throw new ArgumentException("Thiếu yêu cầu.");
                            var files = new List<ChatAttachment>();
                            if (data.TryGetProperty("attachments", out var paths)) foreach (var path in paths.EnumerateArray()) {
                                if (files.Count >= ChatAttachments.MaxFiles) throw new ArgumentException("Quá nhiều tệp.");
                                files.Add(await Task.Run(() => ChatAttachments.Read(path.GetString() ?? ""), lifetime.Token));
                                ChatAttachments.ValidateSet(files);
                            }
                            session.Start(start, files); result = session.Snapshot(); Interlocked.Exchange(ref dirty, 1); break;
                        case "stop": session.Runner.Cancel(); result = new { accepted = true }; break;
                        case "steer": session.Steer(data.GetProperty("text").GetString() ?? ""); result = new { accepted = true }; break;
                        case "coding": session.SaveCoding(data.Deserialize<CodingOptions>(Json.Options) ?? throw new ArgumentException("Thiếu phân công.")); result = session.Coding(); break;
                        case "open": result = session.Snapshot(session.ReadJob(data.GetProperty("id").GetString() ?? "") ?? throw new ArgumentException("Không tìm thấy phiên.")); break;
                        case "models":
                            metadata.RemoveAll(t => t.IsCompleted);
                            if (metadata.Count >= 4) throw new InvalidOperationException("Đang dò model; đợi các yêu cầu hiện tại kết thúc.");
                            var agent = session.Config().Agents.FirstOrDefault(a => a.Id == data.GetProperty("agentId").GetString()) ?? throw new ArgumentException("Không tìm thấy agent.");
                            string modelRequest = id;
                            metadata.Add(Task.Run(async () => {
                                try {
                                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                                    var models = agent.Connection == "chatgpt-plan" ? (await accounts.Models(agent.PlanAccountId, timeout.Token)).Select(c => c.Id).ToList() : (await AgentDiscovery.Models(agent, secrets, timeout.Token)).Models.ToList();
                                    await Write(new { id = modelRequest, result = models });
                                } catch (Exception error) when (error is not OutOfMemoryException) { if (!lifetime.IsCancellationRequested) await Write(new { id = modelRequest, error = error.Message }); }
                            }));
                            continue;
                        case "snapshot": result = session.Snapshot(); break;
                        case "logs":
                            var logJob = session.Runner.Snapshot();
                            result = logJob == null ? "Chưa có nhật ký." : string.Join("\n\n", logJob.Results.Select(r => r.Name + " / " + r.Model + "\n" + r.Logs)) + "\n\n" + string.Join("\n\n", logJob.Team?.Evidence.Select(e => e.Name + " / " + e.State + "\n" + e.Log) ?? []);
                            if (result is string logText && logText.Length > 200000) result = "[…rút gọn]\n" + logText[^200000..];
                            break;
                        default: throw new ArgumentException("Lệnh IDE chưa hỗ trợ.");
                    }
                    await Write(new { id, result });
                } catch (Exception error) when (error is not OutOfMemoryException) { await Write(new { id, error = error.Message }); }
            }
        } catch (OperationCanceledException) { }
        finally {
            session.Runner.Cancel();
            if (session.Runner.Completion is { } completion) { try { await completion.WaitAsync(TimeSpan.FromSeconds(15)); } catch (TimeoutException) { } }
            lifetime.Cancel(); await updates;
            try { await Task.WhenAll(metadata).WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception error) when (error is TimeoutException or OperationCanceledException or IOException) { }
            if (!session.Runner.Busy && session.Runner.Snapshot() is { } last) store.ClearCheckpoint(last.Id);
        }
        return 0;
    }
}
