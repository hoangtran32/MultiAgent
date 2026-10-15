using System.Text;
namespace MultiAgentHub.Core;
public static class Export
{
    public static string ConversationText(ConversationInfo info, IEnumerable<JobRecord> jobs) =>
        $"{info.Title}\nNgày trò chuyện: {info.CreatedAt.LocalDateTime:dd/MM/yyyy HH:mm:ss}\nProject: {info.Project}\n\n" + string.Join("\n\n============================================================\n\n", jobs.Select(PlainText));
    public static string PlainText(JobRecord job)
    {
        var text = new StringBuilder($"{job.Timestamp.LocalDateTime:dd/MM/yyyy HH:mm:ss} • {job.Mode} • {job.State}\n\nBẠN\n{job.Prompt}\n");
        if (job.Mode is "collaboration" or "team") {
            foreach (var turn in job.Turns) text.Append($"\nLƯỢT {turn.Number} • {turn.Name} • {turn.Model} • {turn.Role} • {turn.TaskId} • {turn.State}\n{turn.Text}\n{turn.Error}\n");
            if (job.Team != null) text.Append("\n" + TeamText(job));
        } else if (job.Mode == "discussion") {
            foreach (var turn in job.Turns.Where(t => t.Kind != "summary" || t.State != "success")) {
                if (turn.Kind == "user") text.Append($"\nBẠN BỔ SUNG • {turn.Timestamp:O} • {(turn.State == "queued" ? "Chưa áp dụng" : "Đã gửi vào lượt " + turn.Number)}\n{turn.Text}\n");
                else text.Append($"\nLƯỢT {turn.Number} • {turn.Name} • {turn.Model} • {turn.State}{(turn.Kind == "summary" ? " • TỔNG HỢP" : "")}\n{turn.Text}\n{turn.Error}\n");
            }
            text.Append($"\nTỔNG HỢP\n{job.Report}\n{job.StopReason}\n");
        } else foreach (var result in job.Results) text.Append($"\n{result.Name} • {result.Model} • {result.State}\n{result.Text}\n{result.Error}\n");
        return text.ToString();
    }
    public static string Markdown(JobRecord job)
    {
        var text = new StringBuilder($"# Multi-Agent Hub\n\n{job.Timestamp:O}\n\nProject: {job.Project}\n\nChế độ: {job.Mode} • Trạng thái: {job.State} • {job.Seconds:F1}s\n\n## Yêu cầu\n\n{job.Prompt}\n");
        if (job.Mode is "collaboration" or "team") {
            foreach (var turn in job.Turns) text.Append($"\n## Lượt {turn.Number} • {turn.Name} • {turn.Model} • {turn.Role} • {turn.TaskId} • {turn.State}\n\n{turn.Text}\n\n{turn.Error}\n");
            if (job.Team != null) text.Append("\n## Công việc và đầu ra\n\n" + TeamText(job));
            return text.ToString();
        }
        if (job.Mode == "discussion") {
            text.Append($"\n## Tổng hợp\n\n{(job.ReportByModel ? "Do model tổng hợp; cần kiểm chứng ý tưởng." : "Bản ghi tự động, chưa có tổng hợp từ model.")}\n\n{job.Report}\n\nKết thúc: {job.StopReason}\n");
            if (job.Discussion is { } options) text.Append($"\nGiới hạn: {options.MaxTurns} lượt thảo luận • {options.DurationMinutes} phút • nghỉ {options.PauseSeconds}s\n");
            text.Append("\n## Cuộc trò chuyện\n");
            foreach (var turn in job.Turns) {
                if (turn.Kind == "user") text.Append($"\n### Bạn bổ sung\n\n{turn.Timestamp:O} • {(turn.State == "queued" ? "Chưa áp dụng" : "Đã gửi vào lượt " + turn.Number)}\n\n{turn.Text}\n");
                else text.Append($"\n### Lượt {turn.Number} • {turn.Name} • {turn.Model}{(turn.Kind == "summary" ? " • tổng hợp" : "")}\n\n{turn.Timestamp:O} • {turn.State}\n\n{turn.Text}\n{(turn.Error.Length > 0 ? "\nLỗi: " + turn.Error + "\n" : "")}");
            }
            return text.ToString();
        }
        foreach (var result in job.Results)
        {
            text.Append($"\n## {result.Name}\n\n{Catalog.ProviderName(result.Provider)} • {result.Model} • {result.State}\n\n{result.Text}\n");
            if (result.Error.Length > 0) text.Append($"\nLỗi: {result.Error}\n");
        }
        return text.ToString();
    }
    private static string TeamText(JobRecord job)
    {
        var team = job.Team!;
        return TeamEngine.Report(job) + "\n\nBỔ SUNG TỪ NGƯỜI DÙNG\n" + string.Join("\n\n", team.Directives) +
            "\n\nĐẦU RA\n" + string.Join("\n\n", team.Artifacts.Select(a => $"{a.Title} • {a.Kind} • {a.TaskId} • lượt {a.Turn}\n{a.Content}"));
    }
}
