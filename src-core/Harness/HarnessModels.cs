using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed class HarnessOptions
{
    public bool Enabled { get; set; } = true;
    public bool AllowWeb { get; set; } = true;
    public bool AllowCommands { get; set; } = true;
    public int MaxModelCalls { get; set; } = 24;
    public int MaxToolCalls { get; set; } = 80;
    public int ContextChars { get; set; } = 240000;
    public int ToolOutputChars { get; set; } = 16000;
    public int CommandTimeoutSeconds { get; set; } = 120;
    public string SearchUrlTemplate { get; set; } = "";
    public HarnessOptions Copy() => JsonSerializer.Deserialize<HarnessOptions>(JsonSerializer.Serialize(this, Json.Options), Json.Options)!;
    public void Validate()
    {
        if (MaxModelCalls is < 0 or > 10000 || MaxToolCalls is < 0 or > 10000 || ContextChars is < 32768 or > 800000 || ToolOutputChars is < 2000 or > 64000 || CommandTimeoutSeconds is < 5 or > 3600)
            throw new ArgumentException("Harness: giới hạn lượt 0–10.000 (0 = không giới hạn), ngữ cảnh 32.768–800.000 ký tự, đầu ra công cụ 2.000–64.000 ký tự, lệnh 5–3.600 giây.");
        if (SearchUrlTemplate.Length > 2000 || SearchUrlTemplate.Length > 0 && (!SearchUrlTemplate.Contains("{query}", StringComparison.Ordinal) || !Uri.TryCreate(SearchUrlTemplate.Replace("{query}", "test"), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0))
            throw new ArgumentException("URL tìm kiếm cần HTTPS và chứa {query}; không đặt key trong URL.");
    }
}

public sealed record HarnessBinding(Storage Storage, string ConversationId, string JobId, string Surface,
    string Project, HarnessOptions Options, Action<HarnessRun> Observe)
{
    public bool AllowChanges { get; init; } = true;
    public bool WriterHeld { get; init; }
    public Func<string> UserUpdates { get; init; } = () => "";
    public Func<bool> TryAdditionalModelCall { get; init; } = () => true;
}

public sealed class HarnessRun
{
    private static readonly DateTimeOffset CurrentOwnerStartedAt = OwnerStart();
    private static DateTimeOffset OwnerStart() { using var process = System.Diagnostics.Process.GetCurrentProcess(); return process.StartTime.ToUniversalTime(); }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string State { get; set; } = "running";
    public string Activity { get; set; } = "Đang chuẩn bị";
    public string Error { get; set; } = "";
    public int ModelCalls { get; set; }
    public int ToolCalls { get; set; }
    public int CompactedExchanges { get; set; }
    public List<string> Plan { get; set; } = [];
    public List<string> Artifacts { get; set; } = [];
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public int OwnerProcessId { get; set; } = Environment.ProcessId;
    public DateTimeOffset OwnerStartedAt { get; set; } = CurrentOwnerStartedAt;
    public DateTimeOffset? FinishedAt { get; set; }
}
public sealed record HarnessTool(string Name, string Description, JsonElement Parameters);
public sealed record HarnessCall(string Id, string Name, JsonElement Arguments);
public sealed record HarnessResult(string CallId, string Name, string Output, bool Error = false);
public sealed record HarnessTurn(string Text, IReadOnlyList<HarnessCall> Calls, JsonElement Native);
public sealed record HarnessExchange(HarnessTurn Turn, IReadOnlyList<HarnessResult> Results);
public sealed record HarnessInput(string Instructions, string Prompt, IReadOnlyList<HarnessExchange> History,
    IReadOnlyList<HarnessTool> Tools, IReadOnlyList<ChatAttachment> Files);
public interface IHarnessModel
{
    Task<HarnessTurn> Turn(AgentConfig agent, HarnessInput input, Action<string> text, CancellationToken token);
}
public sealed class HarnessLimitException(string message) : InvalidOperationException(message);
