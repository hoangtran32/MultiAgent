using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiAgentHub.Core;

public record Choice(string Id, string Name)
{
    public override string ToString() => Name;
}

public static class Catalog
{
    public static readonly string[] Modes = ["parallel", "pipeline", "collaboration", "discussion", "team"];
    public const int MaxAgents = 32;
    public static readonly Choice[] Providers = [new("codex", "Codex"), new("claude", "Claude / Anthropic"),
        new("antigravity", "Google Antigravity"), new("gemini", "Google Gemini"),
        new("compatible", "API tương thích OpenAI / local AI"), new("custom", "Công cụ AI khác")];
    public static Choice[] Connections(string provider) => provider switch
    {
        "codex" => [new("codex-cli", "Codex CLI • tự động"), new("manual", "Sao chép / dán kết quả")],
        "claude" => [new("claude-cli", "Claude Code CLI • tự động"), new("anthropic-api", "Anthropic API • trực tiếp"), new("manual", "Claude app / web • dán kết quả")],
        "antigravity" => [new("antigravity-ide", "Antigravity IDE • dán kết quả"), new("antigravity-desktop", "Antigravity 2.0 desktop • dán kết quả"), new("agy-cli", "Antigravity CLI (agy) • tự động")],
        "gemini" => [new("gemini-api", "Gemini API • trực tiếp"), new("manual", "Gemini app / web • dán kết quả")],
        "compatible" => [new("chat-api", "Chat Completions API • trực tiếp")],
        "custom" => [new("custom-cli", "Executable / script CLI • tự động"), new("manual", "Ứng dụng ngoài • dán kết quả")],
        _ => throw new ArgumentException("Công cụ AI không hợp lệ.")
    };
    public static string ProviderName(string id) => Providers.FirstOrDefault(p => p.Id == id)?.Name ?? id;
    public static string ConnectionName(AgentConfig agent) => Connections(agent.Provider).First(c => c.Id == agent.Connection).Name;
    public static bool IsManual(string connection) => connection is "manual" or "antigravity-ide" or "antigravity-desktop";
    public static bool IsApi(string connection) => connection is "anthropic-api" or "gemini-api" or "chat-api";
    public static string Endpoint(string connection) => connection switch
    {
        "anthropic-api" => "https://api.anthropic.com/v1/", "gemini-api" => "https://generativelanguage.googleapis.com/v1beta/",
        "chat-api" => "https://api.openai.com/v1/", _ => ""
    };
    public static void Validate(AgentConfig agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Name) || agent.Name.Length > 80) throw new ArgumentException("Tên cần có 1–80 ký tự.");
        if (!Connections(agent.Provider).Any(c => c.Id == agent.Connection)) throw new ArgumentException("Kết nối không thuộc công cụ AI đã chọn.");
        if (agent.Model.Length > 120 || agent.Model.Any(c => char.IsControl(c))) throw new ArgumentException("Model ID không hợp lệ.");
        if (agent.Instructions.Length > 20000) throw new ArgumentException("Hướng dẫn tối đa 20.000 ký tự.");
        if (agent.Arguments.Count > 64 || agent.Arguments.Any(a => a.Length > 4000 || a.Contains('\0'))) throw new ArgumentException("Đối số CLI không hợp lệ.");
        if (agent.Command.Length > 2048 || agent.Command.Any(char.IsControl)) throw new ArgumentException("Executable không hợp lệ.");
        if (!new[] { "read-only", "workspace-write", "full", "default" }.Contains(agent.Permission)) throw new ArgumentException("Quyền không hợp lệ.");
        if (!new[] { "", "low", "medium", "high", "xhigh", "max", "ultra" }.Contains(agent.Reasoning)) throw new ArgumentException("Mức suy luận không hợp lệ.");
        if (IsApi(agent.Connection)) ApiTransport.BaseUri(agent);
    }
}

public sealed class AgentConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Agent";
    public string Provider { get; set; } = "codex";
    public string Connection { get; set; } = "codex-cli";
    public string Model { get; set; } = "";
    public string Reasoning { get; set; } = "";
    public string Permission { get; set; } = "read-only";
    public string Instructions { get; set; } = "";
    public string Command { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public string Endpoint { get; set; } = "";
    public string KeyEnvironment { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public AgentConfig Copy(bool newId = false)
    {
        var copy = JsonSerializer.Deserialize<AgentConfig>(JsonSerializer.Serialize(this, Json.Options), Json.Options)!;
        if (newId) copy.Id = Guid.NewGuid().ToString("N");
        return copy;
    }
}

public sealed class HubConfig
{
    public int Version { get; set; } = 3;
    public string Surface { get; set; } = "work";
    public string LastConversationId { get; set; } = "";
    public string Project { get; set; } = Directory.GetCurrentDirectory();
    public List<string> RecentProjects { get; set; } = [];
    public List<ProjectInfo> Projects { get; set; } = [];
    public List<string> ProjectSections { get; set; } = [];
    public string Mode { get; set; } = "parallel";
    public string WorkMode { get; set; } = "team";
    public string ChatMode { get; set; } = "parallel";
    public DiscussionOptions Discussion { get; set; } = new();
    public TeamOptions Team { get; set; } = new();
    public List<AgentConfig> Agents { get; set; } = [];
}

public sealed class AgentResult
{
    public string AgentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Connection { get; set; } = "";
    public string Model { get; set; } = "";
    public string State { get; set; } = "queued";
    public string Text { get; set; } = "";
    public string Logs { get; set; } = "";
    public string Error { get; set; } = "";
    public string HandoffPrompt { get; set; } = "";
    public string SentPrompt { get; set; } = "";
}

public sealed class JobRecord
{
    public string Surface { get; set; } = "work";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string Title { get; set; } = "";
    public string PreviousJobId { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Project { get; set; } = "";
    public string Mode { get; set; } = "parallel";
    public string State { get; set; } = "running";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public double Seconds { get; set; }
    public List<AgentResult> Results { get; set; } = [];
    public DiscussionOptions? Discussion { get; set; }
    public List<DiscussionTurn> Turns { get; set; } = [];
    public string Report { get; set; } = "";
    public bool ReportByModel { get; set; }
    public string DiscussionPhase { get; set; } = "";
    public bool DiscussionFinishRequested { get; set; }
    public string StopReason { get; set; } = "";
    public DateTimeOffset? Deadline { get; set; }
    public DateTimeOffset? NextTurnAt { get; set; }
    public TeamRun? Team { get; set; }
    public override string ToString() => $"{Timestamp.LocalDateTime:dd/MM HH:mm} • {Prompt.ReplaceLineEndings(" ")[..Math.Min(Prompt.ReplaceLineEndings(" ").Length, 65)]}";
}

public sealed class ConversationInfo
{
    public bool Pinned { get; set; }
    public bool Archived { get; set; }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Project { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string TextFile { get; set; } = "";
}

public sealed class DiscussionOptions
{
    public int MaxTurns { get; set; } = 48;
    public int DurationMinutes { get; set; } = 480;
    public int PauseSeconds { get; set; } = 0;
    public void Validate()
    {
        if (MaxTurns is < 1 or > 400 || DurationMinutes is < 1 or > 1440 || PauseSeconds is < 0 or > 3600)
            throw new ArgumentException("Thảo luận: 1–400 lượt, 1–1.440 phút, nghỉ 0–3.600 giây giữa các lượt.");
    }
    public DiscussionOptions Copy() => new() { MaxTurns = MaxTurns, DurationMinutes = DurationMinutes, PauseSeconds = PauseSeconds };
}

public sealed class DiscussionTurn
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string ProtocolData { get; set; } = "";
    public int Number { get; set; }
    public string AgentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Kind { get; set; } = "discussion";
    public string State { get; set; } = "running";
    public string Text { get; set; } = "";
    public string Error { get; set; } = "";
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static string Str(this JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
}
