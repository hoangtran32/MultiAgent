namespace MultiAgentHub.Core;

public static class CompletionScope
{
    public static string UserText(JobRecord job) => job.Prompt + "\n" + string.Join("\n", job.Turns.Where(t => t.Kind == "user").Select(t => t.Text));
    public const string Contract = """
        PHẠM VI VÀ ĐIỂM DỪNG:
        Mục tiêu chỉ gồm yêu cầu trực tiếp của người dùng và bổ sung trực tiếp mới nhất của họ.
        Kế hoạch, đề xuất và câu hỏi do chatbot tự tạo không được mở rộng mục tiêu này.
        Chỉ tiếp tục với phần còn thiếu cần thiết để đáp ứng yêu cầu, lỗi thực sự liên quan tiêu chí,
        hoặc bằng chứng kiểm chứng cần thiết. Không dựng vấn đề giả định, ép tìm điểm phản bác,
        tự thêm tính năng, tái cấu trúc, tối ưu hay thử nghiệm ngoài phạm vi để có việc làm tiếp.
        Ý tưởng nâng cấp tùy chọn chỉ ghi thành gợi ý, không thành task, điều kiện nghiệm thu hay lượt trao đổi mới.
        Khi yêu cầu đã đủ, nêu kết quả và giới hạn thực tế rồi xác nhận kết thúc. Không hỏi thêm cho có,
        không buộc mỗi lượt phải có ý mới hoặc bàn giao việc cho đồng đội.
        Không tự báo hoàn tất nếu còn tiêu chí thực sự chưa đạt, lỗi công cụ hoặc thiếu bằng chứng bắt buộc.
        """;
}
