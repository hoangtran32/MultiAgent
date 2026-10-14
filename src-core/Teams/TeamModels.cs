using System.Text.Json;

namespace MultiAgentHub.Core;

public static class TeamRoles
{
    public static readonly Choice[] All = [new("product", "Product Manager"), new("architect", "Architect"),
        new("coder", "Coder"), new("tester", "Tester"), new("reviewer", "Reviewer")];
    public static string Name(string id) => All.FirstOrDefault(r => r.Id == id)?.Name ?? id;
    public static bool CanWrite(AgentConfig agent) => agent.Connection is "codex-cli" or "claude-cli" or "agy-cli" or "custom-cli"
        && agent.Permission is "workspace-write" or "full";
}

public sealed class RoleBinding
{
    public string Role { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string Model { get; set; } = "";
}

public sealed class VerificationCommand
{
    public string Name { get; set; } = "Build / test";
    public string Executable { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 300;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || string.IsNullOrWhiteSpace(Executable) || Executable.Length > 2048 || Executable.Any(char.IsControl)) throw new ArgumentException("Tên / executable kiểm tra không hợp lệ.");
        if (Path.GetExtension(Executable).ToLowerInvariant() is ".cmd" or ".bat") throw new ArgumentException("Dùng executable .exe; nhập script trong đối số nếu cần.");
        if (Arguments.Count > 64 || Arguments.Any(a => a.Length > 4000 || a.Contains('\0')) || TimeoutSeconds is < 5 or > 3600) throw new ArgumentException("Đối số hoặc timeout kiểm tra không hợp lệ.");
    }
}

public sealed class TeamOptions
{
    public int MaxCalls { get; set; } = 30;
    public int DurationMinutes { get; set; } = 120;
    public int MaxTasks { get; set; } = 12;
    public int MaxRevisions { get; set; } = 2;
    public bool ExecuteTasks { get; set; } = true;
    public bool RunVerification { get; set; }
    public string VerificationProject { get; set; } = "";
    public List<RoleBinding> Roles { get; set; } = TeamRoles.All.Select(r => new RoleBinding { Role = r.Id }).ToList();
    public List<VerificationCommand> Checks { get; set; } = [];
    public TeamOptions Copy() => JsonSerializer.Deserialize<TeamOptions>(JsonSerializer.Serialize(this, Json.Options), Json.Options)!;
    public void Validate()
    {
        if (MaxCalls is < 2 or > 400 || DurationMinutes is < 1 or > 1440 || MaxTasks is < 1 or > 50 || MaxRevisions is < 0 or > 5) throw new ArgumentException("Giới hạn nhóm không hợp lệ.");
        if (Roles.Count > 5 || Roles.Select(r => r.Role).Distinct().Count() != Roles.Count || Roles.Any(r => !TeamRoles.All.Any(a => a.Id == r.Role) || r.Model.Length > 120 || r.Model.Any(char.IsControl))) throw new ArgumentException("Phân vai không hợp lệ.");
        if (Checks.Count > 8) throw new ArgumentException("Tối đa 8 lệnh kiểm tra.");
        if (Checks.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Checks.Count) throw new ArgumentException("Tên mỗi lệnh kiểm tra cần khác nhau.");
        foreach (var command in Checks) command.Validate();
        if (RunVerification && Checks.Count == 0) throw new ArgumentException("Thêm lệnh kiểm tra trước khi bật tự kiểm tra.");
    }
}

public sealed class TeamTask
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Acceptance { get; set; } = "";
    public List<string> DependsOn { get; set; } = [];
    public bool RequiresWorkspace { get; set; } = true;
    public string State { get; set; } = "proposed";
    public string ProposedBy { get; set; } = "";
    public string AssignedTo { get; set; } = "";
    public int Revisions { get; set; }
    public string Outcome { get; set; } = "";
    public string Feedback { get; set; } = "";
    public bool IndependentReview { get; set; }
    public string Verification { get; set; } = "not_run";
}

public sealed class TeamArtifact
{
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "note";
    public string Content { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public int Turn { get; set; }
}

public sealed class VerificationEvidence
{
    public string TaskId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Executable { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public int? ExitCode { get; set; }
    public string State { get; set; } = "running";
    public string Log { get; set; } = "";
    public bool Current { get; set; } = true;
}

public sealed class TeamRun
{
    public int SchemaVersion { get; set; } = 1;
    public TeamOptions Options { get; set; } = new();
    public string Phase { get; set; } = "roundtable";
    public string ActiveTaskId { get; set; } = "";
    public string NextAgentId { get; set; } = "";
    public bool StopAfterStep { get; set; }
    public int Calls { get; set; }
    public List<TeamTask> Tasks { get; set; } = [];
    public List<TeamArtifact> Artifacts { get; set; } = [];
    public List<VerificationEvidence> Evidence { get; set; } = [];
    public List<string> Directives { get; set; } = [];
    public List<string> PendingDirectives { get; set; } = [];
    public List<string> Decisions { get; set; } = [];
}
