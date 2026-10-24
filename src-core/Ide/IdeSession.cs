using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed class IdeStart
{
    public string Prompt { get; set; } = "";
    public string Mode { get; set; } = "fast";
    public string AgentId { get; set; } = "";
    public string Model { get; set; } = "";
    public string PreviousId { get; set; } = "";
    public string Context { get; set; } = "";
    public CodingOptions? Coding { get; set; }
}

/// <summary>One workspace, one runner. Credentials/config stay in the native process.</summary>
public sealed class IdeSession
{
    private readonly Func<HubConfig> loadConfig;
    private IReadOnlyList<JobRecord> transcript = [];
    public Storage Store { get; }
    public string Project { get; }
    public JobRunner Runner { get; }
    public IdeSession(string project, Storage store, Func<HubConfig> loadConfig, IAgentExecutor executor)
    {
        Project = Path.GetFullPath(project);
        if (!Directory.Exists(Project)) throw new DirectoryNotFoundException("Mở một thư mục project trước khi gửi yêu cầu.");
        Store = store; this.loadConfig = loadConfig; Runner = new(executor, store);
    }
    public static string WorkspaceProfile(string profile, string project) => Path.Combine(profile, "ide", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))));
    public CodingOptions Coding()
    {
        string file = Path.Combine(Store.DataDir, "ide-coding.json");
        return File.Exists(file) ? JsonSerializer.Deserialize<CodingOptions>(File.ReadAllText(file), Json.Options) ?? new() : loadConfig().Team.Coding.Copy();
    }
    public HubConfig Config() => loadConfig();
    public void SaveCoding(CodingOptions roles)
    {
        var config = loadConfig(); CodingWorkflow.Validate(roles, config.Agents, config.Harness.Enabled);
        if (Runner.Busy) Runner.UpdateCodingRoles(roles);
        Store.Write("ide-coding", roles);
    }
    public void Start(IdeStart request, IReadOnlyList<ChatAttachment>? attachments = null)
    {
        if (Runner.Busy) throw new InvalidOperationException("Agent đang chạy. Gửi bổ sung hoặc dừng phiên hiện tại trước.");
        if (request.Mode is not ("fast" or "plan" or "review")) throw new ArgumentException("Chọn Làm ngay, Kế hoạch hoặc Review.");
        if (request.Context.Length > 100000 || request.Model.Length > 120 || request.Model.Any(char.IsControl)) throw new ArgumentException("Ngữ cảnh/model quá dài hoặc không hợp lệ.");
        var config = JsonSerializer.Deserialize<HubConfig>(JsonSerializer.Serialize(loadConfig(), Json.Options), Json.Options)!; config.Surface = "work"; config.Project = Project;
        if (request.Mode == "plan") {
            config.Mode = "consensus"; config.Team.Coding = request.Coding?.Copy() ?? Coding();
            if (!config.Team.Coding.Enabled) throw new ArgumentException("Chọn và bật phân công Đầu não / Thợ code để dùng Kế hoạch → triển khai.");
            config.Team.ExecuteTasks = true;
            foreach (var agent in config.Agents) agent.Enabled = (agent.Enabled || agent.Id == config.Team.Coding.Brain.AgentId || agent.Id == config.Team.Coding.Implementer.AgentId)
                && !Catalog.IsManual(agent.Connection) && !Catalog.IsBrowserChat(agent.Connection) && agent.Connection != "gateway-web";
        } else {
            var chosen = config.Agents.FirstOrDefault(a => a.Id == request.AgentId) ?? throw new ArgumentException("Chọn một agent hiện có trong Agent & model.");
            if (Catalog.IsManual(chosen.Connection) || Catalog.IsBrowserChat(chosen.Connection) || chosen.Connection == "gateway-web") throw new ArgumentException("Hub Code cần CLI/API có công cụ. Chatbot từ web dùng ở Hub Chat.");
            foreach (var agent in config.Agents) agent.Enabled = agent.Id == chosen.Id;
            if (request.Model.Length > 0) chosen.Model = request.Model;
            if (request.Mode == "review") chosen.Permission = "read-only";
            config.Mode = "pipeline";
        }
        JobRecord? previous = request.PreviousId.Length == 0 ? null : ReadJob(request.PreviousId) ?? throw new ArgumentException("Không tìm thấy phiên để tiếp tục.");
        transcript = previous == null ? [] : Store.ReadConversationJobs(previous.ConversationId, recover: false).TakeLast(5).ToList();
        string context = request.Context + (request.Mode == "review" ? "\nREVIEW: chỉ đọc và báo nhận xét, không sửa file/chạy lệnh thay đổi project." : "");
        Runner.Start(config, request.Prompt, context, previous, attachments: attachments);
    }
    public JobRecord? ReadJob(string id)
    {
        if (id.Length is < 1 or > 80 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new ArgumentException("ID phiên không hợp lệ.");
        return Runner.Snapshot() is { } current && current.Id == id ? current : Store.History().FirstOrDefault(j => j.Id == id);
    }
    public void Steer(string message)
    {
        if (!Runner.Busy) throw new InvalidOperationException("Phiên đã kết thúc; gửi yêu cầu mới hoặc Tiếp tục.");
        if (Runner.Snapshot()?.Team != null) Runner.AddTeamInstruction(message);
        else throw new InvalidOperationException("Làm ngay đang xử lý yêu cầu. Dừng rồi gửi bổ sung để tránh gửi lặp; Kế hoạch hỗ trợ bổ sung tại ranh giới bước.");
    }
    public object Snapshot(JobRecord? job = null)
    {
        if (job != null) transcript = Store.ReadConversationJobs(job.ConversationId.Length > 0 ? job.ConversationId : job.Id, recover: false).Where(j => j.Timestamp < job.Timestamp).TakeLast(5).ToList();
        var live = Runner.Snapshot(); job ??= live;
        if (job == null) return new { busy = false, project = Project };
        bool active = Runner.Busy && job.Id == live?.Id;
        var progress = RunProgress.From(job, active, DateTimeOffset.Now);
        return new {
            busy = active, project = Project, job.Id, job.ConversationId, prompt = Clip(job.Prompt, 32000), job.Title, job.Timestamp, job.State, stopReason = Clip(job.StopReason, 4000), job.Seconds, job.CompletionConfirmed,
            progress, activity = RunProgress.Activity(job, active),
            history = transcript.Where(j => j.Id != job.Id).Select(j => new { j.Id, prompt = Clip(j.Prompt, 8000), results = j.Results.TakeLast(4).Select(r => new { r.AgentId, r.Name, r.Model, text = Clip(r.Text, 8000), r.State }) }),
            turns = job.Turns.TakeLast(32).Select(t => new { t.Number, t.Kind, t.Name, t.Model, t.Role, t.TaskId, t.State, text = Clip(t.Text, 12000), error = Clip(t.Error, 2000) }),
            results = job.Results.Select(r => new { r.AgentId, r.Name, r.Model, r.State, text = Clip(r.Text, job.Results.Count <= 4 ? 64000 : 12000), error = Clip(r.Error, 2000) }),
            tasks = job.Team?.Tasks.Select(t => new { t.Id, t.Title, description = Clip(t.Description, 2000), acceptance = Clip(t.Acceptance, 1500), t.State, t.Verification, feedback = Clip(t.Feedback, 2000), outcome = Clip(t.Outcome, 4000), t.DesignModel, t.ImplementationModel, implementationPlan = Clip(t.ImplementationPlan, 4000) }),
            artifacts = job.Team?.Artifacts.TakeLast(8).Select(a => new { a.Title, a.Kind, content = Clip(a.Content, 8000) }),
            calls = job.Team?.Calls ?? job.HarnessModelCalls,
            phase = job.Team?.Phase ?? "work"
        };
    }
    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length] + "\n[…rút gọn]";
}
