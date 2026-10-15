using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MultiAgentHub.Core;

public sealed partial class Storage
{
    private readonly object conversationGate = new();
    public string ConversationsDir => Path.Combine(DataDir, "conversations");
    public static string Topic(string prompt)
    {
        string first = prompt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Cuộc trò chuyện";
        return first[..Math.Min(100, first.Length)];
    }
    private static string Key(string value) => Regex.IsMatch(value, "^[a-zA-Z0-9-]{1,80}$") ? value : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];
    private string ConversationDir(string id)
    {
        if (!Regex.IsMatch(id, "^[a-zA-Z0-9-]{1,80}$")) throw new ArgumentException("ID hội thoại không hợp lệ.");
        string root = Path.GetFullPath(ConversationsDir), dir = Path.Combine(root, id);
        // Personal archives must never redirect writes through directory links.
        foreach (string path in new[] { root, dir })
            if (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget != null) throw new IOException("Thư mục hội thoại không được là liên kết.");
        return dir;
    }
    private static void AtomicText(string path, string text)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text, new UTF8Encoding(true)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void ImportHistory()
    {
        foreach (var job in History()) {
            string id = job.ConversationId.Length > 0 ? job.ConversationId : Key(job.Id);
            if (!File.Exists(Path.Combine(ConversationDir(id), "job-" + Key(job.Id) + ".json"))) SaveConversationJob(job);
        }
    }
    public void SaveConversationJob(JobRecord job)
    {
        lock (conversationGate) {
            string id = job.ConversationId.Length > 0 ? job.ConversationId : Key(job.Id);
            string dir = ConversationDir(id); Directory.CreateDirectory(dir);
            string metadata = Path.Combine(dir, "conversation.json");
            var info = File.Exists(metadata) ? JsonSerializer.Deserialize<ConversationInfo>(File.ReadAllText(metadata), Json.Options)! : new ConversationInfo {
                Id = id, Title = job.Title.Length > 0 ? Topic(job.Title) : Topic(job.Prompt), Project = job.Project, CreatedAt = job.Timestamp
            };
            if (!string.Equals(info.Project, job.Project, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Hội thoại thuộc project khác. Tạo cuộc trò chuyện mới.");
            job.ConversationId = id; job.Title = info.Title;
            if (info.TextFile.Length == 0) {
                string safe = Regex.Replace(info.Title, "[<>:\"/\\\\|?*\\x00-\\x1f]", " ").Trim().TrimEnd('.', ' ');
                if (safe.Length == 0) safe = "Cuộc trò chuyện";
                info.TextFile = safe[..Math.Min(75, safe.Length)] + "_" + info.CreatedAt.LocalDateTime.ToString("dd-MM-yyyy_HH-mm-ss") + ".txt";
            }
            if (Path.GetFileName(info.TextFile) != info.TextFile) throw new InvalidDataException("Tên file hội thoại không hợp lệ.");
            info.UpdatedAt = DateTimeOffset.Now;
            AtomicText(Path.Combine(dir, "job-" + Key(job.Id) + ".json"), JsonSerializer.Serialize(job, Json.Options));
            AtomicText(metadata, JsonSerializer.Serialize(info, Json.Options));
            AtomicText(Path.Combine(dir, info.TextFile), Export.ConversationText(info, ReadConversationJobs(id, false)));
        }
    }
    public List<ConversationInfo> Conversations()
    {
        lock (conversationGate) {
            if (!Directory.Exists(ConversationsDir)) return [];
            var list = new List<ConversationInfo>();
            foreach (string dir in Directory.GetDirectories(ConversationsDir)) {
                string id = Path.GetFileName(dir);
                try {
                    string file = Path.Combine(ConversationDir(id), "conversation.json");
                    if (File.Exists(file) && JsonSerializer.Deserialize<ConversationInfo>(File.ReadAllText(file), Json.Options) is { } info && info.Id == id) list.Add(info);
                } catch (Exception e) when (e is IOException or JsonException or ArgumentException) { /* A damaged conversation does not hide the rest of the library. */ }
            }
            return list.OrderByDescending(c => c.Pinned).ThenByDescending(c => c.UpdatedAt).ToList();
        }
    }
    public void UpdateConversation(string id, string? title = null, bool? pinned = null, bool? archived = null)
    {
        lock (conversationGate) {
            string dir = ConversationDir(id), metadata = Path.Combine(dir, "conversation.json");
            var info = JsonSerializer.Deserialize<ConversationInfo>(File.ReadAllText(metadata), Json.Options) ?? throw new InvalidDataException("Không đọc được topic.");
            if (title != null) {
                if (string.IsNullOrWhiteSpace(title) || title.Length > 100) throw new ArgumentException("Tên topic cần có 1–100 ký tự.");
                info.Title = Topic(title);
            }
            if (pinned.HasValue) info.Pinned = pinned.Value;
            if (archived.HasValue) info.Archived = archived.Value;
            AtomicText(metadata, JsonSerializer.Serialize(info, Json.Options));
            AtomicText(ConversationTextPath(info), Export.ConversationText(info, ReadConversationJobs(id)));
        }
    }
    public List<JobRecord> ReadConversationJobs(string id, bool recover = true, CancellationToken token = default)
    {
        lock (conversationGate) {
            token.ThrowIfCancellationRequested(); string dir = ConversationDir(id); if (!Directory.Exists(dir)) return [];
            var jobs = new List<JobRecord>();
            foreach (string file in Directory.GetFiles(dir, "job-*.json")) {
                token.ThrowIfCancellationRequested();
                var job = JsonSerializer.Deserialize<JobRecord>(File.ReadAllText(file), Json.Options) ?? throw new InvalidDataException("Không đọc được lượt hội thoại.");
                jobs.Add(recover ? Recover(job) : job);
            }
            token.ThrowIfCancellationRequested(); return jobs.OrderBy(j => j.Timestamp).ToList();
        }
    }
    private static JobRecord Recover(JobRecord job)
    {
        if (job.State != "running") return job;
        job.State = "interrupted"; job.NextTurnAt = null; job.DiscussionPhase = "interrupted";
        job.StopReason = "Phiên chưa kết thúc trước khi Hub đóng; có thể xem lại hoặc tiếp tục với yêu cầu mới.";
        foreach (var turn in job.Turns.Where(t => t.State == "running")) turn.State = "cancelled";
        foreach (var result in job.Results.Where(r => r.State is "running" or "queued" or "waiting")) { result.State = "cancelled"; result.HandoffPrompt = ""; }
        if (job.Team is { } team) {
            team.Phase = "interrupted";
            foreach (var task in team.Tasks.Where(t => t.State is "running" or "verifying" or "reviewing")) task.State = "interrupted";
            foreach (var evidence in team.Evidence) { evidence.Current = false; if (evidence.State == "running") evidence.State = "interrupted"; }
            job.Report = TeamEngine.Report(job);
        }
        return job;
    }
    public string ConversationTextPath(ConversationInfo info)
    {
        if (Path.GetFileName(info.TextFile) != info.TextFile || info.TextFile.Length == 0) throw new InvalidDataException("Tên file hội thoại không hợp lệ.");
        return Path.Combine(ConversationDir(info.Id), info.TextFile);
    }
    public string ConversationContext(string id, string throughJob)
    {
        var jobs = ReadConversationJobs(id); int index = jobs.FindIndex(j => j.Id == throughJob);
        if (index < 0) throw new InvalidOperationException("Không tìm thấy cuộc trò chuyện cần tiếp tục.");
        var selected = jobs.Take(index + 1).TakeLast(8).ToList(); var pieces = new List<string>(); int remaining = 18000;
        foreach (var job in selected.AsEnumerable().Reverse()) {
            string output = job.Report.Length > 0 ? job.Report : string.Join("\n", job.Results.Select(r => $"{r.Name} ({r.Model}, {r.State}): {r.Text}\n{r.Error}"));
            string piece = $"[Trao đổi {job.Timestamp:O}]\nNgười dùng: {job.Prompt[..Math.Min(2000, job.Prompt.Length)]}\nKết quả: {output[..Math.Min(6000, output.Length)]}\n";
            if (piece.Length > remaining) piece = piece[..remaining];
            pieces.Insert(0, piece); remaining -= piece.Length; if (remaining == 0) break;
        }
        return "NGỮ CẢNH HỘI THOẠI CŨ (rút gọn, dùng làm tham khảo):\n" + string.Join("\n", pieces);
    }
}
