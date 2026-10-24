using System.Text.Json;

namespace MultiAgentHub.Core;

/// <summary>Write ahead receipts. An interrupted operation is never automatically replayed.</summary>
public sealed class HarnessJournal
{
    private readonly string directory;
    internal static void ValidateScope(string id) { if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-zA-Z0-9-]{1,80}$")) throw new ArgumentException("ID hội thoại harness không hợp lệ."); }
    public HarnessJournal(HarnessBinding binding, string runId)
    {
        ValidateScope(binding.ConversationId);
        if (!Guid.TryParseExact(runId, "N", out _)) throw new ArgumentException("ID harness không hợp lệ.");
        directory = Path.Combine(binding.Storage.DataDir, "harness", binding.ConversationId, runId);
        PlainPath(directory); Directory.CreateDirectory(directory);
    }
    internal static void PlainPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Harness không truy cập đường dẫn liên kết.");
    }
    public void Save(string name, object value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-zA-Z0-9_-]{1,100}$")) throw new ArgumentException("Tên receipt không hợp lệ.");
        string file = Path.Combine(directory, name + ".json"), temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        PlainPath(file);
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Json.Options)); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static object Read(HarnessBinding binding, string run, string receipt, int offset, int count)
    {
        var journal = new HarnessJournal(binding, run);
        if (!System.Text.RegularExpressions.Regex.IsMatch(receipt, "^call-[0-9]{1,6}$")) throw new ArgumentException("Receipt phải là call-N.");
        string file = Path.Combine(journal.directory, receipt + ".json"); PlainPath(file);
        if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new IOException("Receipt quá lớn.");
        string value = File.ReadAllText(file); offset = Math.Clamp(offset, 0, value.Length); count = Math.Clamp(count, 1, binding.Options.ToolOutputChars);
        return new { run, receipt, offset, totalChars = value.Length, text = value.Substring(offset, Math.Min(count, value.Length - offset)) };
    }
    public static object Runs(HarnessBinding binding)
    {
        ValidateScope(binding.ConversationId);
        string folder = Path.Combine(binding.Storage.DataDir, "harness", binding.ConversationId); PlainPath(folder);
        if (!Directory.Exists(folder)) return Array.Empty<object>();
        return Directory.EnumerateDirectories(folder).Take(1000).Where(p => Guid.TryParseExact(Path.GetFileName(p), "N", out _))
            .OrderByDescending(Directory.GetLastWriteTimeUtc).Take(30).Select(p => {
                PlainPath(p); string file = Path.Combine(p, "state.json"); PlainPath(file);
                if (!File.Exists(file)) return null;
                if (new FileInfo(file).Length > 65536) throw new IOException("Trạng thái harness quá lớn.");
                var state = JsonSerializer.Deserialize<HarnessRun>(File.ReadAllText(file), Json.Options);
                if (state?.State == "running") {
                    bool live;
                    try { using var owner = System.Diagnostics.Process.GetProcessById(state.OwnerProcessId); live = !owner.HasExited && Math.Abs((owner.StartTime.ToUniversalTime() - state.OwnerStartedAt.UtcDateTime).TotalSeconds) < 1; }
                    catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { live = false; }
                    if (!live) { state.State = "interrupted"; state.Activity = "Gián đoạn; kiểm tra receipt trước khi tiếp tục"; }
                }
                return state;
            }).Where(r => r != null).ToArray();
    }
}
