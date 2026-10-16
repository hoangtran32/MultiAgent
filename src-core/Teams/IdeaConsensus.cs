namespace MultiAgentHub.Core;

/// <summary>One open idea at a time. Agreement belongs to an exact proposal version.</summary>
public static class IdeaConsensus
{
    public const string Contract = """
        CHẾ ĐỘ CHỐT TỪNG Ý (ưu tiên quy tắc này cho phần bàn tròn):
        Mỗi lượt chỉ một ý nhỏ, 100–200 từ, message tối đa 2.400 ký tự. Phản hồi ngay ý cuối của đồng đội;
        không viết cả bài, toàn bộ thiết kế hoặc làm hết project rồi mới xin review. Không sửa file khi bàn ý.
        Chỉ có MỘT ý chưa xong. Không mở ý mới khi ý hiện tại chưa được tất cả thành viên khả dụng chốt,
        thực hiện và review (hoặc ghi nhận kế hoạch khi chỉ trò chuyện). Đề xuất là xác nhận của tác giả.
        Dùng cùng khối hub của hợp đồng cơ bản, bổ sung trường version và taskAmendments:
        - Khi chưa có ý mở: tasks=[{id:"I1",version:1,title,description,acceptance,dependsOn:[],requiresWorkspace}].
        - Đồng ý/phản đối ý đang mở: taskReviews=[{taskId:"I1",version:PHIEN_BAN_HIEN_TAI,accept:true/false,reason:"lý do cụ thể"}].
        - Sửa ý đang mở: taskAmendments=[{id:"I1",version:PHIEN_BAN_HIEN_TAI+1,title,description,acceptance,dependsOn:[],requiresWorkspace}].
          Cung cấp đầy đủ phương án mới; thay đổi làm hết hiệu lực mọi xác nhận cũ, đồng đội phải xác nhận lại.
        Mỗi lượt tối đa một đề xuất/sửa/xác nhận. Hỏi làm rõ thì để ba danh sách trên rỗng; chưa được coi là đồng ý.
        Không chuyển ý, không bỏ qua bất đồng để đạt quota. Không giả lập lời nói hay sự đồng ý của đồng đội.
        Chỉ finished=true khi không còn ý mở và yêu cầu gốc đã được đáp ứng; cả nhóm phải xác nhận kết thúc.
        Khi THỰC HIỆN: chỉ làm ý đã chốt hiện tại, không làm các ý tiếp theo. Khi REVIEW: chỉ kiểm tra ý đó.
        """;

    public static TeamTask? Active(TeamRun team) => team.Tasks.FirstOrDefault(t => t.State is not ("done" or "planned"));
    public static void Validate(TeamRun team, TeamReply reply)
    {
        var active = Active(team);
        if (reply.Message.Length > 2400 || reply.Artifacts.Count > 1 || reply.Artifacts.Any(a => a.Content.Length > 6000))
            throw new InvalidDataException("Chốt từng ý cần câu trả lời ngắn, không viết toàn bộ task.");
        if (reply.Tasks.Count + reply.TaskAmendments.Count + reply.TaskReviews.Count > 1)
            throw new InvalidDataException("Mỗi lượt chỉ đề xuất, sửa hoặc xác nhận một ý.");
        if (reply.Finished && (active != null || reply.Tasks.Count + reply.TaskAmendments.Count + reply.TaskReviews.Count > 0))
            throw new InvalidDataException("Còn ý chưa xong; chưa thể xác nhận kết thúc.");
        foreach (var proposal in reply.Tasks.Concat(reply.TaskAmendments))
            if (proposal.Description.Length > 2000 || proposal.Acceptance.Length > 1500)
                throw new InvalidDataException("Chia nhỏ ý; phạm vi tối đa 2.000 ký tự, tiêu chí tối đa 1.500 ký tự.");
        if (reply.Tasks.Count > 0) {
            if (active != null) throw new InvalidDataException("Phải chốt và xử lý xong ý hiện tại trước khi mở ý tiếp.");
            if (reply.Tasks[0].Version != 1) throw new InvalidDataException("Ý mới bắt đầu ở phiên bản 1.");
            TeamProtocol.ValidateProposals(team, reply.Tasks);
        }
        if (reply.TaskAmendments.FirstOrDefault() is { } amendment) {
            if (active == null || active.State != "proposed" || amendment.Id != active.Id || amendment.Version != active.ProposalVersion + 1)
                throw new InvalidDataException("Chỉ sửa ý đang bàn và tăng đúng một phiên bản.");
            if (amendment.DependsOn.Contains(active.Id) || amendment.DependsOn.Any(id => !team.Tasks.Any(t => t.Id == id && t.State is "done" or "planned")))
                throw new InvalidDataException("Ý chỉ phụ thuộc các phần đã hoàn tất, không phụ thuộc chính nó.");
        }
        if (reply.TaskReviews.FirstOrDefault() is { } review) {
            if (active == null || active.State != "proposed" || review.TaskId != active.Id || review.Version != active.ProposalVersion || string.IsNullOrWhiteSpace(review.Reason))
                throw new InvalidDataException("Xác nhận phải ghi đúng ý, phiên bản hiện tại và lý do; xác nhận cũ hết hiệu lực.");
        }
    }

    public static void Apply(TeamRun team, TeamReply reply, string speaker, IReadOnlyCollection<string> available)
    {
        if (!available.Contains(speaker)) throw new InvalidDataException("Chỉ thành viên đang tham gia được xác nhận ý.");
        Validate(team, reply);
        TeamTask? task = Active(team);
        if (reply.Tasks.FirstOrDefault() is { } proposal) {
            task = new() { Id = proposal.Id, Title = proposal.Title, Description = proposal.Description, Acceptance = proposal.Acceptance,
                DependsOn = proposal.DependsOn.Distinct().ToList(), RequiresWorkspace = proposal.RequiresWorkspace, ProposedBy = speaker, AgreedBy = [speaker] };
            team.Tasks.Add(task);
        }
        if (reply.TaskAmendments.FirstOrDefault() is { } amended) {
            task!.ProposalVersion = amended.Version; task.Title = amended.Title; task.Description = amended.Description; task.Acceptance = amended.Acceptance;
            task.DependsOn = amended.DependsOn.Distinct().ToList(); task.RequiresWorkspace = amended.RequiresWorkspace;
            task.AgreedBy = [speaker]; task.Feedback = speaker + " sửa phương án; cần xác nhận lại phiên bản " + task.ProposalVersion;
        }
        if (reply.TaskReviews.FirstOrDefault() is { } review) {
            task!.AgreedBy.Remove(speaker);
            if (review.Accept) task.AgreedBy.Add(speaker);
            task.Feedback = speaker + ": " + review.Reason;
        }
        if (task is { State: "proposed" } && available.Count > 0 && available.All(task.AgreedBy.Contains)) task.State = "ready";
    }
    public static void Invalidate(TeamTask task)
    {
        task.ProposalVersion++; task.AgreedBy.Clear(); task.State = "proposed";
        task.BlockReviewedBy = ""; task.BlockConfirmed = false;
    }
}
