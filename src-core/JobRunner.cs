using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace MultiAgentHub.Core;

public interface IAgentExecutor
{
    void Validate(AgentConfig agent) { }
    Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token);
}

public sealed class AgentExecutor(ApiTransport api, BrowserChatBridge? browser = null, WebGateway? gateway = null, ChatGptPlan? chatGptPlan = null) : IAgentExecutor
{
    public void Validate(AgentConfig agent)
    {
        if (agent.Connection == "chatgpt-plan") { (chatGptPlan ?? throw new InvalidOperationException("API dùng gói ChatGPT chưa mở.")).Validate(agent); return; }
        if (agent.Connection == "gateway-web") { (gateway ?? throw new InvalidOperationException("Gateway chưa được mở.")).Validate(agent.GatewayAccountId, agent.Model); return; }
        if (Catalog.IsManual(agent.Connection)) return;
        if (Catalog.IsBrowserChat(agent.Connection)) {
            try { (browser ?? throw new InvalidOperationException("Bridge trình duyệt chưa được mở.")).Validate(agent.Connection, agent.BrowserBindingId, agent.Model); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException) { throw new BrowserChatConnectionException(agent.Id, error.Message, error); }
            return;
        }
        if (Catalog.IsApi(agent.Connection)) api.Validate(agent);
        else ProcessTransport.RequireExecutable(agent);
    }
    public async Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
    {
        ChatAttachments.ValidateFor(agent, agent.RuntimeAttachments);
        if (agent.RuntimeHarness is { Options.Enabled: true } && ProviderHarnessModel.Supported(agent.Connection))
            return await new AgentHarness(new ProviderHarnessModel(api, chatGptPlan)).Execute(agent, prompt, text, log, token);
        if (agent.Connection == "chatgpt-plan") return await (chatGptPlan ?? throw new InvalidOperationException("API dùng gói ChatGPT chưa mở.")).Execute(agent, prompt, text, token);
        prompt = ChatAttachments.WithText(prompt, agent);
        if (agent.Connection == "gateway-web") return await (gateway ?? throw new InvalidOperationException("Gateway chưa được mở.")).Execute(agent.GatewayAccountId, agent.Model, prompt, text, log, token);
        if (Catalog.IsBrowserChat(agent.Connection)) return await (browser ?? throw new InvalidOperationException("Bridge trình duyệt chưa được mở.")).Execute(agent.Connection, agent.BrowserBindingId, agent.Model, prompt, text, log, token);
        if (Catalog.IsApi(agent.Connection)) return await api.Execute(agent, prompt, token);
        string binary = ProcessTransport.RequireExecutable(agent);
        var parser = new CliParser(agent.Connection, text, log);
        var errors = new System.Text.StringBuilder();
        try { await ProcessTransport.Run(ProcessTransport.Build(agent, project, prompt, binary), project, value => { parser.Feed(value); parser.ThrowIfQuota(); }, value => {
            errors.Append(value); if (errors.Length > 64000) errors.Remove(0, errors.Length - 64000); log(value);
        }, token, parser.Completion); }
        catch (Exception e) when (e is not OperationCanceledException) {
            if (Quota.Exhausted(errors.ToString())) throw new AgentQuotaException();
            var diagnostic = CliDiagnostics.Explain(agent, errors.ToString());
            try { parser.Finish(); }
            catch (AgentQuotaException) { throw; }
            catch (InvalidDataException) { /* Empty output does not replace the process failure. */ }
            catch (InvalidOperationException failure) { diagnostic ??= CliDiagnostics.Explain(agent, failure.Message); if (diagnostic != null) throw diagnostic; throw; }
            if (diagnostic != null) throw diagnostic;
            throw;
        }
        try { return parser.Finish(); }
        catch (Exception failure) when (failure is InvalidDataException or InvalidOperationException) {
            var diagnostic = CliDiagnostics.Explain(agent, errors + "\n" + failure.Message);
            if (diagnostic != null) throw diagnostic;
            throw;
        }
    }
}

public sealed class JobRunner(IAgentExecutor executor, Storage storage, TimeProvider? time = null)
{
    private readonly object gate = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> replies = new();
    private readonly ConcurrentQueue<string> teamInstructions = new();
    private DiscussionControl? discussionControl;
    private CancellationTokenSource? cancellation;
    private JobRecord? current;
    public Task? Completion { get; private set; }
    public bool Busy { get { lock (gate) return cancellation != null; } }
    public event Action<JobRecord>? Changed;
    public event Action<string>? AgentQuotaReached;
    public JobRecord? Snapshot() { lock (gate) return current == null ? null : JsonSerializer.Deserialize<JobRecord>(JsonSerializer.Serialize(current, Json.Options), Json.Options); }
    private void Notify() { if (Snapshot() is { } snapshot) Changed?.Invoke(snapshot); }
    private void Change(Action update) { lock (gate) update(); Notify(); }
    private static TeamOptions SessionTeamOptions(HubConfig config)
    {
        var options = config.Team.Copy();
        if (config.Surface == "chat") {
            options.ExecuteTasks = false; options.RunVerification = false;
            options.VerificationProject = ""; options.Checks.Clear();
        }
        return options;
    }
    public void Start(HubConfig config, string prompt, string context = "", JobRecord? previous = null, string title = "", IReadOnlyList<ChatAttachment>? attachments = null)
    {
        Storage.Validate(config);
        bool chat = config.Surface == "chat";
        if (!chat && !Directory.Exists(config.Project)) throw new DirectoryNotFoundException("Project không tồn tại.");
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 200000) throw new ArgumentException("Yêu cầu cần có 1–200.000 ký tự.");
        var agents = config.SelectedAgents().Select(a => a.Copy()).ToList();
        var files = attachments ?? [];
        ChatAttachments.ValidateSet(files);
        foreach (var agent in agents) { ChatAttachments.ValidateFor(agent, files); agent.RuntimeAttachments = files; }
        if (config.Surface == "chat") foreach (var agent in agents) agent.Permission = "read-only";
        if (agents.Count == 0) throw new ArgumentException("Chọn ít nhất một agent.");
        if (config.Mode is "discussion" or "team" or "consensus") DiscussionEngine.ValidateAgents(agents);
        if (Catalog.IsTeamMode(config.Mode) && config.Team.RunVerification && config.Surface == "work" && !string.Equals(Path.GetFullPath(config.Team.VerificationProject.Length > 0 ? config.Team.VerificationProject : "."), Path.GetFullPath(config.Project), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Lệnh kiểm tra chưa được cấu hình cho project này. Mở Agent & model → Phân vai & giới hạn nhóm để kiểm tra và lưu lại.");
        foreach (var agent in agents.Where(a => !Catalog.IsManual(a.Connection))) executor.Validate(agent);
        if (previous != null) {
            if (previous.Surface != config.Surface) throw new ArgumentException("Hội thoại thuộc không gian khác. Tạo hội thoại mới khi chuyển giữa Trò chuyện và Làm việc.");
            if (!chat && !string.Equals(Path.GetFullPath(previous.Project), Path.GetFullPath(config.Project), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Chọn đúng project của cuộc trò chuyện trước khi tiếp tục.");
            string id = previous.ConversationId.Length > 0 ? previous.ConversationId : previous.Id;
            context = storage.ConversationContext(id, previous.Id) + "\n\n" + context;
        }
        CancellationToken token;
        lock (gate)
        {
            if (cancellation != null) throw new InvalidOperationException("Đang có tác vụ chạy.");
            storage.SaveAttachments(files);
            string conversationId = previous == null ? Guid.NewGuid().ToString("N") : previous.ConversationId.Length > 0 ? previous.ConversationId : previous.Id;
            var browserAgents = agents.Where(a => Catalog.IsBrowserChat(a.Connection)).ToList();
            if (browserAgents.Count > 0) {
                foreach (var agent in browserAgents) {
                    if (config.BrowserTopics.TryGetValue(agent.BrowserBindingId, out string? owner) && owner != conversationId)
                        throw new ArgumentException($"{agent.Name}: tab này đang thuộc chủ đề Hub khác. Mở chat mới trên website và kết nối lại qua Chatbot từ trình duyệt để tránh trộn ngữ cảnh. Lịch sử chủ đề cũ vẫn được giữ.");
                }
                var reserved = JsonSerializer.Deserialize<HubConfig>(JsonSerializer.Serialize(config, Json.Options), Json.Options)!;
                var currentBindings = reserved.Agents.Where(a => Catalog.IsBrowserChat(a.Connection)).Select(a => a.BrowserBindingId).ToHashSet();
                foreach (string oldBinding in reserved.BrowserTopics.Keys.Where(id => !currentBindings.Contains(id)).ToArray()) reserved.BrowserTopics.Remove(oldBinding);
                foreach (var agent in browserAgents) reserved.BrowserTopics[agent.BrowserBindingId] = conversationId;
                Storage.Validate(reserved); storage.Write("native-config", reserved); config.BrowserTopics = reserved.BrowserTopics;
            }
            string workingDirectory = chat && agents.Any(a => a.Connection != "gateway-web" && !Catalog.IsTextApi(a.Connection) && !Catalog.IsBrowserChat(a.Connection) && !Catalog.IsManual(a.Connection))
                ? storage.ChatWorkingDirectory(conversationId) : chat ? "" : config.Project;
            cancellation = new(); token = cancellation.Token;
            teamInstructions.Clear();
            discussionControl = config.Mode == "discussion" ? new() : null;
            current = new() { Surface = config.Surface, Prompt = prompt, Project = chat ? "" : config.Project,
                WorkingDirectory = workingDirectory,
                Mode = config.Mode, Discussion = config.Mode == "discussion" ? config.Discussion.Copy() : null,
                Team = Catalog.IsTeamMode(config.Mode) ? new() { Options = SessionTeamOptions(config) } : null,
                ConversationId = conversationId,
                Attachments = files.Select(f => f.Info).ToList(),
                Title = previous == null ? Storage.Topic(title.Length > 0 ? title : prompt) : previous.Title.Length > 0 ? previous.Title : Storage.Topic(previous.Prompt), PreviousJobId = previous?.Id ?? "",
                Results = agents.Select(a => new AgentResult { AgentId = a.Id, Name = a.Name, Provider = a.Provider, Connection = a.Connection, Model = a.Model }).ToList() };
            var harnessJob = current;
            if (config.Harness.Enabled) foreach (var agent in agents.Where(a => ProviderHarnessModel.Supported(a.Connection)))
                agent.RuntimeHarness = new(storage, conversationId, current.Id, config.Surface, chat ? "" : config.Project, config.Harness.Copy(), run => {
                    Change(() => {
                        int index = harnessJob.HarnessRuns.FindIndex(r => r.Id == run.Id);
                        if (index >= 0) harnessJob.HarnessRuns[index] = run; else { harnessJob.HarnessRuns.Add(run); if (harnessJob.HarnessRuns.Count > 200) harnessJob.HarnessRuns.RemoveAt(0); }
                    });
                    CheckpointCurrent();
                }) { WriterHeld = !chat && Catalog.IsTeamMode(config.Mode) && config.Team.ExecuteTasks,
                    TryAdditionalModelCall = () => { lock (gate) { if (harnessJob.Team is not { } team) return true; if (!team.Options.HasCallCapacity(team.Calls)) return false; team.Calls++; return true; } },
                    UserUpdates = () => { lock (gate) return string.Join("\n\n", harnessJob.Turns.Where(t => t.Kind == "user").Select(t => t.Text)); } };
            if (Catalog.IsTeamMode(config.Mode) && previous?.Team is { } earlier) {
                var resumed = JsonSerializer.Deserialize<TeamRun>(JsonSerializer.Serialize(earlier, Json.Options), Json.Options)!;
                resumed.Options = SessionTeamOptions(config); resumed.Calls = 0; resumed.Phase = "roundtable"; resumed.ActiveTaskId = resumed.NextAgentId = ""; resumed.StopAfterStep = false;
                foreach (var task in resumed.Tasks.Where(t => t.State != "done")) {
                    if (config.Mode == "consensus") {
                        bool blocked = task.State == "blocked";
                        IdeaConsensus.Invalidate(task);
                        // A new user request must reconcile the old scope before an old blocker can stop it again.
                        if (blocked) { TaskBlocker.Reopen(task); task.NeedsScopeReview = true; }
                    } else task.State = "proposed";
                    task.Revisions = 0; task.Feedback += "\nTiếp tục phiên: cần xác nhận lại task và đọc workspace hiện tại.";
                    var attempt = previous.Turns.LastOrDefault(t => t.TaskId == task.Id && t.Role == TeamRoles.Name("coder") && t.State is ("cancelled" or "timeout" or "error") && t.Text.Length > 0);
                    if (attempt != null) task.PreviousAttempt = attempt.Text[..Math.Min(attempt.Text.Length, 12000)];
                }
                foreach (var evidence in resumed.Evidence) evidence.Current = false;
                foreach (var task in resumed.Tasks.Where(t => t.Verification == "passed")) task.Verification = "stale";
                resumed.Directives.Add("Yêu cầu khi tiếp tục: " + prompt); current.Team = resumed;
                foreach (string pending in resumed.PendingDirectives) { teamInstructions.Enqueue(pending); current.Turns.Add(new() { Kind = "user", Name = "Bạn", State = "queued", Text = pending }); }
                if (resumed.Tasks.Count > config.Team.MaxTasks) { cancellation.Dispose(); cancellation = null; current = null; throw new ArgumentException("Giới hạn task mới nhỏ hơn số task đang lưu. Tăng giới hạn trong Nhóm & giới hạn."); }
            }
        }
        Notify(); Completion = Run(agents, context, token);
    }
    private async Task Run(List<AgentConfig> agents, string context, CancellationToken token)
    {
        var watch = Stopwatch.StartNew(); var job = current!;
        async Task Stage(int index, string previous, string purpose = "")
        {
            var agent = agents[index]; var result = job.Results[index];
            if (token.IsCancellationRequested) { Change(() => result.State = "cancelled"); return; }
            string prompt = WorkspacePolicy.Instructions(job, agent, true) + CompletionScope.Contract + "\n" + purpose + (agent.Instructions.Length > 0 ? $"Vai trò / hướng dẫn cho {agent.Name}:\n{agent.Instructions}\n\n" : "") + job.Prompt +
                (context.Length > 0 ? "\n\n" + context : "") + (previous.Length > 0 ? "\n\nKết quả các bước trước:\n" + previous + "\nRà soát và hoàn thiện giải pháp dựa trên yêu cầu gốc." : "");
            DiscussionTurn? turn = job.Mode == "collaboration" ? new() { Number = job.Turns.Count + 1, AgentId = agent.Id, Name = agent.Name, Model = agent.Model, Kind = "work" } : null;
            if (turn != null) Change(() => { job.Turns.Add(turn); result.Text = ""; });
            try
            {
                if (Catalog.IsManual(agent.Connection))
                {
                    prompt = ChatAttachments.WithText(prompt, agent);
                    prompt = $"Model mong muốn: {(agent.Model.Length > 0 ? agent.Model : "Theo lựa chọn trong ứng dụng AI")}\nChọn model và quyền trong ứng dụng AI.\n\n" + prompt;
                    var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    replies[agent.Id] = reply;
                    Change(() => { result.State = "waiting"; result.HandoffPrompt = prompt; result.SentPrompt = prompt; });
                    using var registration = token.Register(() => reply.TrySetCanceled(token));
                    string answer = await reply.Task;
                    Change(() => { result.Text = answer; result.State = "success"; result.HandoffPrompt = ""; });
                }
                else
                {
                    Change(() => { result.State = "running"; result.SentPrompt = prompt; });
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(30));
                    string answer = await executor.Execute(agent, job.WorkingDirectory, prompt,
                        value => Change(() => { result.Text = CliParser.Limit(value); if (turn != null) turn.Text = result.Text; }),
                        value => Change(() => result.Logs = (result.Logs + value)[Math.Max(0, (result.Logs + value).Length - 64000)..]), timeout.Token);
                    Change(() => { result.Text = answer; result.State = "success"; });
                }
            }
            catch (AgentQuotaException e) {
                AgentQuotaReached?.Invoke(agent.Id);
                Change(() => { result.State = "quota"; result.Error = e.Message + " Đã bỏ chọn; bật lại khi hạn mức được khôi phục."; result.HandoffPrompt = ""; });
            }
            catch (OperationCanceledException) { Change(() => { result.State = token.IsCancellationRequested ? "cancelled" : "error"; result.Error = token.IsCancellationRequested ? "Đã dừng." : "Quá thời gian xử lý (30 phút hoặc giới hạn API)."; }); }
            catch (Exception e) { Change(() => { result.State = "error"; result.Error = e.Message; }); }
            finally {
                if (turn != null) Change(() => { turn.Text = result.Text; turn.State = result.State; turn.Error = result.Error; });
                replies.TryRemove(agent.Id, out _);
                try { if (job.Mode != "discussion") storage.SaveConversationJob(Snapshot()!); }
                catch (Exception e) { Change(() => result.Logs += "\nKhông lưu được hội thoại: " + e.Message); }
            }
        }
        try
        {
            try {
            storage.SaveConversationJob(Snapshot()!);
            if (Catalog.IsTeamMode(job.Mode)) await Task.Run(() => new TeamEngine(executor, storage, teamInstructions).Run(job, agents, context, Change, id => AgentQuotaReached?.Invoke(id), token, CheckpointCurrent));
            else if (job.Mode == "discussion") await new DiscussionEngine(executor, storage, time).RunInteractive(job, agents, context, Change, id => AgentQuotaReached?.Invoke(id), token, discussionControl!, () => Snapshot()!, CheckpointCurrent);
            else if (job.Mode == "parallel") await Task.WhenAll(agents.Select((a, index) => Stage(index, "")));
            else
            {
                var previous = new List<string>();
                for (int i = 0; i < agents.Count; i++)
                {
                    string purpose = job.Mode != "collaboration" ? "" : job.Surface == "chat"
                        ? i == 0 ? "Đề xuất câu trả lời và nêu giả định cần kiểm chứng.\n\n" : "Phản biện câu trả lời trước, bổ sung lập luận và các điểm còn thiếu. Chỉ trao đổi văn bản.\n\n"
                        : i == 0 ? "Bạn là agent thực hiện chính. Xử lý yêu cầu và ghi rõ việc đã làm, kết quả kiểm tra, điều còn cần xác minh.\n\n" : "Bạn là đồng đội rà soát. Kiểm tra kết quả trước, tìm lỗi và thực hiện phần còn thiếu trong quyền được cấp. Nêu rõ các sửa đổi cần agent chính hoàn tất.\n\n";
                    await Stage(i, string.Join("\n\n", previous), purpose);
                    if (job.Results[i].State == "quota") continue;
                    if (job.Results[i].State != "success") break;
                    previous.Add($"[{job.Results[i].Name} • {job.Results[i].Model}]\n{job.Results[i].Text[..Math.Min(12000, job.Results[i].Text.Length)]}");
                }
                if (job.Mode == "collaboration" && !token.IsCancellationRequested && job.Results.Count(r => r.State == "success") > 1 && !job.Results.Any(r => r.State == "error")) {
                    int lead = job.Results.FindIndex(r => r.State == "success");
                    string feedback = string.Join("\n\n", previous); if (feedback.Length > 48000) feedback = feedback[^48000..];
                    await Stage(lead, feedback, job.Surface == "chat" ? "Hoàn thiện câu trả lời dựa trên phản biện, nêu điểm thống nhất và điều cần kiểm chứng. Chỉ trả lời văn bản, không mở thêm vòng thảo luận.\n\n"
                        : "Đây là lượt hoàn thiện sau phản biện của đồng đội. Kiểm chứng các góp ý, sửa phần còn thiếu nếu cần, chạy kiểm tra phù hợp và trả lời kết quả cuối cho người dùng. Không mở thêm vòng thảo luận.\n\n");
                    if (job.Results[lead].State == "success") Change(() => { job.Report = job.Results[lead].Text; job.ReportByModel = true; });
                }
            }
            } catch (Exception e) {
                if (Catalog.IsTeamMode(job.Mode)) Change(() => { job.State = "failed"; if (job.Team != null) job.Team.Phase = "finished"; });
                Change(() => { job.Results[0].State = "error"; job.Results[0].Error = "Phiên bị gián đoạn: " + e.Message; job.StopReason = e.Message;
                    if (job.Mode == "discussion" && job.Report.Length == 0) job.Report = DiscussionEngine.LocalReport(job); });
            }
            Change(() => {
                if (!Catalog.IsTeamMode(job.Mode)) job.State = token.IsCancellationRequested ? "cancelled" : job.Results.Any(r => r.State == "error") ? "failed" :
                    job.Results.Any(r => r.State == "quota") ? job.Results.Any(r => r.State == "success") || job.Turns.Any(t => t.State == "success") ? "partial" : "quota" : "completed";
                job.Seconds = watch.Elapsed.TotalSeconds;
                foreach (var result in job.Results) { if (result.State == "queued") result.State = token.IsCancellationRequested ? "cancelled" : "skipped"; result.HandoffPrompt = ""; }
            });
            try { storage.SaveJob(Snapshot()!); if (job.Mode is "discussion" or "team" or "consensus") storage.ClearCheckpoint(job.Id); }
            catch (Exception e) { Change(() => job.Results[0].Logs += "\nKhông lưu được lịch sử: " + e.Message); }
        }
        finally
        {
            lock (gate) { discussionControl?.Dispose(); discussionControl = null; cancellation?.Dispose(); cancellation = null; }
            foreach (string file in Directory.Exists(Path.Combine(storage.DataDir, "handoffs")) ? Directory.GetFiles(Path.Combine(storage.DataDir, "handoffs"), "native-" + job.Id + "-*.txt") : [])
                try { File.Delete(file); } catch (IOException) { }
            Notify();
        }
    }
    public void Reply(string agentId, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000000) throw new ArgumentException("Câu trả lời cần có 1–1.000.000 ký tự.");
        if (!replies.TryGetValue(agentId, out var reply) || !reply.TrySetResult(text.Trim())) throw new InvalidOperationException("Agent không chờ câu trả lời.");
    }
    public void ReportQuota(string agentId)
    {
        if (!replies.TryGetValue(agentId, out var reply) || !reply.TrySetException(new AgentQuotaException())) throw new InvalidOperationException("Agent không chờ kết quả.");
    }
    public void Cancel() { lock (gate) cancellation?.Cancel(); }
    private void CheckpointCurrent() { lock (gate) storage.Checkpoint(Snapshot()!); }
    private void RequireDiscussion()
    {
        if (cancellation == null || cancellation.IsCancellationRequested || current?.Mode != "discussion" || discussionControl == null ||
            current.DiscussionFinishRequested || current.DiscussionPhase is "summarizing" or "finished")
            throw new InvalidOperationException("Bàn luận đang kết thúc hoặc không còn chạy. Giữ yêu cầu trong ô nhập để gửi tiếp sau khi phiên hoàn tất.");
    }
    public void AddDiscussionInstruction(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 12000) throw new ArgumentException("Bổ sung cần có 1–12.000 ký tự.");
        lock (gate) {
            RequireDiscussion();
            if (current!.Turns.Count(t => t.Kind == "user") >= 100) throw new InvalidOperationException("Đã đạt 100 bổ sung của phiên. Kết thúc bàn luận rồi gửi yêu cầu mới.");
            var input = new DiscussionTurn { Kind = "user", Name = "Bạn", State = "queued", Text = text.Trim(), Timestamp = (time ?? TimeProvider.System).GetUtcNow() };
            var accepted = Snapshot()!; accepted.Turns.Add(input);
            // Persist before acknowledging, under the same gate as engine checkpoints.
            storage.Checkpoint(accepted); current.Turns.Add(input); discussionControl!.Wake();
        }
        Notify();
    }
    public void NextDiscussionTurn()
    {
        lock (gate) { RequireDiscussion(); if (current!.DiscussionPhase != "resting") throw new InvalidOperationException("Chỉ bỏ thời gian nghỉ khi bàn luận đang chờ lượt tiếp."); discussionControl!.Wake(); }
    }
    public void FinishDiscussion()
    {
        lock (gate) {
            RequireDiscussion();
            var accepted = Snapshot()!; accepted.DiscussionFinishRequested = true; storage.Checkpoint(accepted);
            current!.DiscussionFinishRequested = true; discussionControl!.Finish();
        }
        Notify();
    }
    public void AddTeamInstruction(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 12000) throw new ArgumentException("Bổ sung cần có 1–12.000 ký tự.");
        lock (gate) {
            if (cancellation == null || current == null || current.State != "running" || !Catalog.IsTeamMode(current.Mode) || current.Team == null || current.Team.Phase == "finished") throw new InvalidOperationException("Phiên nhóm đã kết thúc hoặc không còn chạy. Giữ yêu cầu để gửi tiếp.");
            if (teamInstructions.Count >= 20 || current.Team!.Directives.Count + teamInstructions.Count >= 100) throw new InvalidOperationException("Đã đạt giới hạn bổ sung của phiên.");
            var input = new DiscussionTurn { Kind = "user", Name = "Bạn", State = "queued", Text = text.Trim() };
            var accepted = Snapshot()!; accepted.Team!.PendingDirectives.Add(text.Trim()); accepted.Turns.Add(input); storage.Checkpoint(accepted);
            current.Team.PendingDirectives.Add(text.Trim());
            current.Turns.Add(input);
            teamInstructions.Enqueue(text.Trim());
        }
        Notify();
    }
    public void StopTeamAfterStep() => Change(() => {
        if (cancellation == null || current?.Team == null) throw new InvalidOperationException("Không có nhóm đang chạy.");
        current.Team.StopAfterStep = true;
    });
}

public static class ProjectContext
{
    private static string RealPath(string path)
    {
        string full = Path.GetFullPath(path), root = Path.GetPathRoot(full)!;
        foreach (string part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            root = Path.Combine(root, part);
            FileSystemInfo item = Directory.Exists(root) ? new DirectoryInfo(root) : new FileInfo(root);
            if (item.LinkTarget != null) root = item.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Không đọc được liên kết file.");
        }
        return root;
    }
    public static string Read(string project, string file)
    {
        string root = RealPath(project), full = RealPath(file), relative = Path.GetRelativePath(root, full);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative)) throw new ArgumentException("File đính kèm phải nằm trong project.");
        if (new FileInfo(full).Length > 512 * 1024) throw new ArgumentException("File đính kèm tối đa 512 KB.");
        string text = File.ReadAllText(full);
        if (text.Contains('\0')) throw new ArgumentException("Chỉ hỗ trợ file văn bản.");
        return $"[FILE: {relative}]\n{text}";
    }
    public static string ReadAttachment(string file)
    {
        string full = RealPath(file);
        if (new FileInfo(full).Length > 512 * 1024) throw new ArgumentException("File đính kèm tối đa 512 KB.");
        string text = File.ReadAllText(full);
        if (text.Contains('\0')) throw new ArgumentException("Chỉ hỗ trợ file văn bản.");
        return $"[FILE ĐƯỢC NGƯỜI DÙNG ĐÍNH KÈM: {Path.GetFileName(full)}]\n{text}";
    }
}

public static class LocalModels
{
    public static List<string> Efforts(AgentConfig agent)
    {
        if (agent.Connection == "claude-cli") return ["low", "medium", "high", "xhigh", "max"];
        if (agent.Connection == "agy-cli") return ["low", "medium", "high"];
        if (agent.Connection != "codex-cli") return [];
        try
        {
            using var doc = Cache();
            var model = doc.RootElement.GetProperty("models").EnumerateArray().FirstOrDefault(m => m.Str("slug") == agent.Model);
            if (model.ValueKind == JsonValueKind.Object && model.TryGetProperty("supported_reasoning_levels", out var levels)) return levels.EnumerateArray().Select(l => l.Str("effort")).Where(l => l.Length > 0).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { }
        return ["low", "medium", "high", "xhigh", "max", "ultra"];
    }
    private static JsonDocument Cache()
    {
        string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "models_cache.json")));
    }
    public static List<string> Read(AgentConfig agent)
    {
        if (agent.Connection == "claude-cli") {
            var models = new List<string>();
            string home = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            foreach (string name in new[] { "settings.json", "settings.local.json" }) {
                try {
                    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, name)));
                    string model = doc.RootElement.Str("model"); if (model.Length > 0) models.Add(model);
                    if (doc.RootElement.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
                        foreach (string key in new[] { "ANTHROPIC_MODEL", "ANTHROPIC_DEFAULT_SONNET_MODEL", "ANTHROPIC_DEFAULT_OPUS_MODEL", "ANTHROPIC_DEFAULT_HAIKU_MODEL" })
                            if (env.Str(key) is { Length: > 0 } configured) models.Add(configured);
                } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
            }
            foreach (string variable in new[] { "ANTHROPIC_MODEL", "ANTHROPIC_DEFAULT_SONNET_MODEL", "ANTHROPIC_DEFAULT_OPUS_MODEL", "ANTHROPIC_DEFAULT_HAIKU_MODEL" })
                if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } configured) models.Add(configured);
            return models.Where(m => m.Length <= 120 && !m.Any(char.IsControl)).Distinct().Order().ToList();
        }
        if (agent.Connection != "codex-cli") return [];
        try
        {
            using var doc = Cache();
            return doc.RootElement.GetProperty("models").EnumerateArray().Where(m => m.Str("visibility") == "list").Select(m => m.Str("slug")).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { return []; }
    }
}
