namespace MultiAgentHub.Core;

/// <summary>Presentation of observed workflow state, never an estimate of unfinished work.</summary>
public sealed record RunProgress(string Title, string Detail, string Reason, string Progress, string Agents,
    string Timing, string Tone, int Done, int Total, bool IsRunning, bool CanContinue)
{
    public static string Activity(JobRecord job, bool active)
    {
        if (!active || job.State != "running") return "";
        string names = string.Join(" + ", job.Results.Where(r => r.State == "running").Select(r => r.Name).Distinct());
        return names.Length > 0 ? names + " đang xử lý…" : job.Team?.Phase == "verifying" ? "Đang chạy kiểm tra…" : "";
    }
    public static RunProgress From(JobRecord job, bool active, DateTimeOffset now)
    {
        var team = job.Team;
        bool discussion = job.Mode == "discussion";
        var current = job.Turns.LastOrDefault(t => t.State == "running" && t.Kind != "user");
        var task = (active ? team?.Tasks.FirstOrDefault(t => t.Id == team.ActiveTaskId) : null) ?? team?.Tasks.FirstOrDefault(t => t.State is not ("done" or "planned"));
        bool unfinishedHistory = !active && job.State == "completed" && team?.Tasks.Any(t => t.State is not ("done" or "planned")) == true;
        bool waitingOnly = job.Results.Any(r => r.State == "waiting") && !job.Results.Any(r => r.State == "running") && current == null;
        bool planning = team != null && (job.Surface == "chat" || !team.Options.ExecuteTasks);
        int total = team?.Tasks.Count ?? (job.Mode == "discussion" ? 0 : job.Results.Count);
        int done = team?.Tasks.Count(t => t.State == "done" || planning && t.State == "planned") ?? job.Results.Count(r => r.State == "success");
        string progress = team != null ? total == 0 ? "Đang xác định các task" : $"{done}/{total} {(planning ? "ý đã chốt kế hoạch" : "task đã review")} · {total - done} chưa xong" :
            job.Mode == "discussion" ? $"{job.Turns.Count(t => t.Kind == "discussion" && t.State == "success")} lượt trao đổi đã xong" : $"{done}/{total} agent đã trả lời";
        string title, tone, detail;
        bool canContinue = unfinishedHistory || !active && job.State is "partial" or "failed" or "cancelled" or "interrupted" or "quota" or "running" or "unverified";
        string reason = "";
        if (active) {
            tone = "active";
            title = job.State != "running" || team?.Phase == "finished" || discussion && job.DiscussionPhase == "finished" ? "Đang lưu kết quả" :
                waitingOnly ? "Đang chờ bạn dán kết quả" :
                team?.Phase == "work" ? "Đang thực hiện task" :
                team?.Phase == "verifying" ? "Đang chạy kiểm tra" :
                current?.Role == "Kiểm chứng vướng mắc" ? "Đang kiểm chứng vướng mắc" :
                team?.Phase == "review" ? "Đang phản biện kết quả" :
                team?.Phase == "completion-review" ? "Đang đối chiếu điểm dừng" :
                team?.Phase == "roundtable" ? job.Mode == "consensus" ? "Đang thống nhất ý" : "Đang bàn tròn" :
                discussion && job.DiscussionPhase == "summarizing" ? "Đang tổng hợp" :
                discussion && job.DiscussionFinishRequested ? "Đang kết thúc sau lượt hiện tại" :
                discussion && job.DiscussionPhase == "awaiting-user" ? "Chờ bạn" :
                discussion && job.DiscussionPhase == "resting" ? "Đang nghỉ giữa lượt" :
                discussion ? "Đang trao đổi" : "Đang làm việc";
            string speaker = current?.Name ?? string.Join(", ", job.Results.Where(r => r.State == "running").Select(r => r.Name));
            detail = task != null ? $"{task.Id} · {task.Title}" : speaker.Length > 0 ? speaker + " đang xử lý yêu cầu" : "Đang chuẩn bị bước tiếp theo";
            if (task != null && speaker.Length > 0) detail = speaker + " — " + detail;
            if (waitingOnly) detail = "Mở Chi tiết và dán kết quả cho " + string.Join(", ", job.Results.Where(r => r.State == "waiting").Select(r => r.Name));
            if (discussion && job.NextTurnAt is { } next && job.DiscussionPhase == "resting") detail = $"Lượt tiếp theo sau {Math.Max(0, (int)Math.Ceiling((next - now).TotalSeconds))} giây; có thể gửi bổ sung để chạy ngay";
            if (discussion && job.DiscussionPhase == "awaiting-user") {
                tone = "idle";
                detail = "Các chatbot đã nói xong phần hiện tại. Gửi tin nhắn để tiếp tục cuộc trò chuyện.";
                reason = string.Join(" · ", job.Results.Where(r => r.State is "error" or "quota").Select(r => r.Name + ": " + (r.State == "quota" ? "hết quota" : "mất kết nối/lỗi")));
            }
            if (team?.StopAfterStep == true) reason = "Đã nhận yêu cầu dừng sau bước đang làm.";
            else if (team?.PendingDirectives.Count > 0) reason = $"Đã nhận {team.PendingDirectives.Count} bổ sung; áp dụng sau bước đang chạy.";
            else if (!waitingOnly && job.Results.Any(r => r.State == "waiting")) reason = "Một agent vẫn đang làm; mở Chi tiết để dán kết quả cho " + string.Join(", ", job.Results.Where(r => r.State == "waiting").Select(r => r.Name));
        } else {
            tone = unfinishedHistory ? "warning" : job.State == "completed" ? "success" : job.State is "failed" or "quota" || job.Turns.Any(t => t.State is "timeout" or "error") && job.State == "partial" ? "error" : "warning";
            title = unfinishedHistory ? "Phiên đã kết thúc — còn task chưa xong" : job.State switch {
                "completed" => job.CompletionConfirmed ? "Đã hoàn thành yêu cầu" : discussion ? "Đã kết thúc trao đổi" : "Hoàn tất phiên",
                "unverified" => "Đã có kết quả — chưa kiểm chứng",
                "planned" => "Đã lưu kế hoạch — chưa thực hiện",
                "cancelled" => "Đã dừng theo yêu cầu",
                "interrupted" or "running" => "Phiên đã gián đoạn — chưa xong",
                "quota" => "Đã dừng — hết quota",
                "failed" => "Đã dừng do lỗi — chưa xong",
                _ => team?.Tasks.Any(t => t.State == "blocked") == true ? "Bị chặn — chưa xong" : "Đã dừng — chưa xong"
            };
            detail = task != null ? $"Còn {task.Id} · {task.Title} — {TaskState(task)}" :
                job.State == "completed" ? "Phiên không còn chạy. Bạn có thể đọc kết quả hoặc gửi yêu cầu mới." :
                job.State == "unverified" ? "Đã kết thúc lượt chạy; xem bằng chứng và phần chưa được kiểm chứng." :
                job.State == "planned" ? "Đã kết thúc lập kế hoạch. Chưa thực hiện thay đổi trong project." : "Phiên không còn chạy; kết quả đã nhận vẫn được giữ.";
            var failure = job.Turns.LastOrDefault(t => t.State is "timeout" or "error" or "invalid" || job.State == "partial" && t.State == "cancelled" && t.Error.Length > 0);
            var failedResult = job.Results.LastOrDefault(r => r.State is "timeout" or "error");
            reason = unfinishedHistory ? "Lịch sử còn task chưa được review; cần tiếp tục từ kết quả đã lưu." : job.State is "completed" or "planned" ? "" : job.State == "cancelled" ? job.StopReason : failure is { Error.Length: > 0 } ? failure.Name + ": " + failure.Error :
                failedResult is { Error.Length: > 0 } ? failedResult.Name + ": " + failedResult.Error :
                task?.State == "blocked" && task.RequiredAction.Length > 0 ? "Cần để tiếp tục: " + task.RequiredAction : job.StopReason;
        }
        string agents = string.Join("   ·   ", job.Results.Select(r => {
            string state = active ? r.State switch {
                "running" => "đang làm", "waiting" => "chờ bạn dán kết quả", "queued" => "chờ lượt",
                "success" when discussion && job.DiscussionPhase == "awaiting-user" => "chờ bạn",
                "success" when team != null || job.Mode == "discussion" => "đã góp ý, chờ lượt",
                "success" => "đã trả lời", "quota" => "hết quota", "error" => "lỗi", "timeout" => "quá thời gian", _ => "chờ điều phối"
            } : r.State switch { "success" => "đã trả lời", "quota" => "hết quota", "error" => "lỗi", "timeout" => "quá thời gian", "cancelled" => "đã dừng", _ => "không chạy" };
            return r.Name + ": " + state;
        }));
        double seconds = active ? Math.Max(0, (now - job.Timestamp).TotalSeconds) : Math.Max(0, job.Seconds);
        string timing = $"{(int)(seconds / 60)} phút {(int)seconds % 60:00} giây" + (team != null ? " · " + team.Options.CallUsage(team.Calls) : "");
        return new(title, detail, reason, progress, agents, timing, tone, done, total, active, canContinue);
    }
    public static string TaskState(TeamTask task) => task.NeedsScopeReview ? "cần cập nhật phạm vi" : task.State switch {
        "proposed" => "đang chờ các agent chốt ý", "ready" => "chờ thực hiện", "running" => "đang thực hiện",
        "verifying" => "đang kiểm tra", "reviewing" => "đang phản biện", "done" => "đã review", "planned" => "đã lưu kế hoạch",
        "blocked" => "bị chặn", "interrupted" => "chưa làm xong", _ => task.State
    };
}
