namespace MultiAgentHub.Core;

/// <summary>A blocked execution is evidence to discuss, never completed work.</summary>
public static class TaskBlocker
{
    private static string Clip(string text) => text.Length <= 3000 ? text : text[..3000] + "…";
    public static void Capture(TeamTask task, TeamReply reply, string source)
    {
        task.State = "blocked"; task.BlockedBy = source;
        task.BlockedReason = Clip(string.IsNullOrWhiteSpace(reply.BlockedReason) ? reply.Message : reply.BlockedReason);
        task.RequiredAction = Clip(reply.RequiredAction); task.BlockReviewedBy = ""; task.BlockConfirmed = false; task.NeedsScopeReview = false;
        task.Feedback = reply.Message;
    }
    public static void Reopen(TeamTask task)
    {
        if (task.BlockedBy.Length == 0) task.BlockedBy = task.AssignedTo;
        if (task.BlockedReason.Length == 0) task.BlockedReason = Clip(task.Outcome.Length > 0 ? task.Outcome : task.Feedback);
        task.BlockReviewedBy = ""; task.BlockConfirmed = false;
    }
    public static void ValidateReview(TeamReply reply)
    {
        if (reply.Outcome is not ("revise" or "blocked")) throw new InvalidDataException("Kiểm chứng vướng mắc chỉ dùng revise hoặc blocked; không nghiệm thu task chưa thực hiện được.");
        if (reply.Outcome == "blocked" && (string.IsNullOrWhiteSpace(reply.BlockedReason) || string.IsNullOrWhiteSpace(reply.RequiredAction)))
            throw new InvalidDataException("Vướng mắc đã xác nhận cần lý do và điều kiện/thao tác người dùng phải làm để tiếp tục.");
    }
    public static void ApplyReview(TeamTask task, TeamReply reply, string reviewer, int maxRevisions)
    {
        ValidateReview(reply);
        if (task.State != "blocked") throw new InvalidDataException("Ý không còn bị chặn; cần đọc lại trạng thái hiện tại.");
        task.Feedback = reply.Message;
        if (reply.Outcome == "revise" && task.Revisions < maxRevisions) {
            task.Revisions++; IdeaConsensus.Invalidate(task);
            task.NeedsScopeReview = true;
            task.BlockReviewedBy = reviewer; task.BlockConfirmed = false;
            task.RequiredAction = "";
            task.Feedback = "Đồng đội tìm được hướng xử lý trong phạm vi; cần chốt lại phương án.\n" + reply.Message;
        } else {
            task.BlockReviewedBy = reviewer; task.BlockConfirmed = true;
            if (reply.BlockedReason.Length > 0) task.BlockedReason = Clip(reply.BlockedReason);
            task.RequiredAction = reply.Outcome == "revise" ? "Đã hết số vòng sửa của ý này. Tăng giới hạn hoặc điều chỉnh phạm vi rồi tiếp tục; chưa có lần thử lại." : Clip(reply.RequiredAction);
        }
    }
    public static string Describe(TeamTask task) => $"Ý {task.Id} v{task.ProposalVersion} bị chặn: " +
        (task.BlockedReason.Length > 0 ? task.BlockedReason : task.Outcome.Length > 0 ? task.Outcome : task.Feedback) +
        (task.RequiredAction.Length > 0 ? "\nCần để tiếp tục: " + task.RequiredAction : "") +
        (task.BlockConfirmed ? "\nĐã kiểm chứng vướng mắc; chưa hoàn tất task." : "\nVướng mắc chưa được đồng đội xác nhận.");
}
