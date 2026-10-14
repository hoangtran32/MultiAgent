using System.Text.Json;
using System.Text.RegularExpressions;

namespace MultiAgentHub.Core;

public sealed class TaskProposal
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Acceptance { get; set; } = "";
    public List<string> DependsOn { get; set; } = [];
    public bool RequiresWorkspace { get; set; } = true;
}

public sealed class TaskReview
{
    public string TaskId { get; set; } = "";
    public bool Accept { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class TeamReply
{
    public string Message { get; set; } = "";
    public string NextAgentId { get; set; } = "";
    public bool Finished { get; set; }
    public string Outcome { get; set; } = "";
    public List<TaskProposal> Tasks { get; set; } = [];
    public List<TaskReview> TaskReviews { get; set; } = [];
    public List<TeamArtifact> Artifacts { get; set; } = [];
    public List<string> Decisions { get; set; } = [];
    public List<string> Files { get; set; } = [];
}

/// <summary>Only validated structured fields can change workflow state; prose is never a command.</summary>
public static class TeamProtocol
{
    public const string Contract = """
        Trả lời tiếng Việt rõ ràng và kết thúc bằng đúng một khối ```hub chứa JSON như dưới.
        message là phần giải thích đầy đủ để người dùng đọc. Không ghi token/mật khẩu vào đầu ra.
        {
          "message": "Đề xuất, phần hữu ích từ đồng đội, bất đồng và bằng chứng",
          "nextAgentId": "ID agent phù hợp tiếp theo hoặc chuỗi rỗng",
          "finished": false,
          "outcome": "",
          "tasks": [{"id":"T1","title":"Tên task","description":"Phạm vi cụ thể","acceptance":"Tiêu chí nghiệm thu","dependsOn":[],"requiresWorkspace":true}],
          "taskReviews": [{"taskId":"ID task đã được đề xuất trước lượt này","accept":true,"reason":"Vì sao task đủ rõ và cần làm"}],
          "artifacts": [{"title":"Thiết kế / PRD / báo cáo","kind":"design","content":"Nội dung tài liệu"}],
          "decisions": ["Quyết định hoặc bất đồng cần ghi nhận"],
          "files": ["đường/dẫn/tương/đối/file/đã/sửa"]
        }
        Chỉ tạo tasks và taskReviews ở bàn tròn. Mỗi task phải nhỏ, có tiêu chí kiểm chứng;
        không đề xuất lại task đã tồn tại. requiresWorkspace=false dành cho phân tích/tài liệu trả về trong artifacts.
        Khi thực hiện task: outcome="done" hoặc "blocked"; mô tả trung thực thay đổi và giới hạn.
        Khi review: outcome="approve", "revise" hoặc "blocked". Không sửa file trong lượt review.
        Không tự chấp nhận task mới của mình trong cùng lượt. Không dùng finished=true khi còn việc chưa giải quyết.
        Không coi model đồng ý hoặc model báo PASS là bằng chứng thực thi. Trường không dùng để [] hoặc "".
        """;

    public static TeamReply Parse(string answer, string phase)
    {
        string json = answer.Trim();
        int marker = json.LastIndexOf("```hub", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) {
            int end = json.IndexOf("```", marker + 6, StringComparison.Ordinal);
            if (end < 0) throw new InvalidDataException("Thiếu dấu kết thúc khối hub.");
            json = json[(marker + 6)..end].Trim();
        } else if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase) && json.EndsWith("```")) json = json[7..^3].Trim();
        var reply = JsonSerializer.Deserialize<TeamReply>(json, Json.Options) ?? throw new InvalidDataException("Đầu ra hub rỗng.");
        if (string.IsNullOrWhiteSpace(reply.Message) || reply.Message.Length > 48000 || reply.NextAgentId == null || reply.Outcome == null ||
            reply.Tasks == null || reply.TaskReviews == null || reply.Artifacts == null || reply.Decisions == null || reply.Files == null)
            throw new InvalidDataException("Thiếu message hoặc trường dữ liệu nhóm không hợp lệ.");
        if (reply.Tasks.Count > 12 || reply.TaskReviews.Count > 50 || reply.Artifacts.Count > 6 || reply.Decisions.Count > 12 || reply.Files.Count > 30) throw new InvalidDataException("Đầu ra nhóm vượt giới hạn.");
        if (phase == "work" && reply.Outcome is not ("done" or "blocked") || phase == "review" && reply.Outcome is not ("approve" or "revise" or "blocked")) throw new InvalidDataException("Thiếu outcome hợp lệ cho bước đang chạy.");
        if (reply.Tasks.Any(t => t == null || !Regex.IsMatch(t.Id ?? "", "^[a-zA-Z0-9_-]{1,40}$") || string.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 160 ||
            string.IsNullOrWhiteSpace(t.Description) || t.Description.Length > 6000 || string.IsNullOrWhiteSpace(t.Acceptance) || t.Acceptance.Length > 3000 ||
            t.DependsOn == null || t.DependsOn.Count > 50 || t.DependsOn.Any(d => d == null))) throw new InvalidDataException("Task thiếu ID, mô tả hoặc tiêu chí nghiệm thu.");
        if (reply.TaskReviews.Any(r => r == null || r.TaskId == null || r.Reason == null || r.Reason.Length > 3000) ||
            reply.Artifacts.Any(a => a == null || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 160 || a.Kind == null || a.Kind.Length > 40 || a.Content == null || a.Content.Length > 20000) ||
            reply.Decisions.Any(d => d == null || d.Length > 3000) || reply.Files.Any(f => f == null || f.Length > 500)) throw new InvalidDataException("Tài liệu, nhận xét hoặc file không hợp lệ.");
        return reply;
    }

    public static void ValidateProposals(TeamRun team, IEnumerable<TaskProposal> proposals)
    {
        var incoming = proposals.ToList();
        if (team.Tasks.Count + incoming.Count > team.Options.MaxTasks) throw new InvalidDataException("Đã đạt giới hạn task.");
        var edges = team.Tasks.ToDictionary(t => t.Id, t => t.DependsOn, StringComparer.Ordinal);
        foreach (var task in incoming) if (!edges.TryAdd(task.Id, task.DependsOn)) throw new InvalidDataException($"Task {task.Id} đã tồn tại; dùng phản biện để bổ sung.");
        foreach (var pair in edges) if (pair.Value.Any(d => !edges.ContainsKey(d))) throw new InvalidDataException($"Task {pair.Key} phụ thuộc ID chưa tồn tại.");
        var visiting = new HashSet<string>(); var done = new HashSet<string>();
        void Visit(string id) {
            if (done.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidDataException("Task có phụ thuộc vòng tròn.");
            foreach (string next in edges[id]) Visit(next);
            visiting.Remove(id); done.Add(id);
        }
        foreach (string id in edges.Keys) Visit(id);
    }
}
