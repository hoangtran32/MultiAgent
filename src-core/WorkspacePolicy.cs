namespace MultiAgentHub.Core;

/// <summary>Workspace access follows the user's CLI permission; workflow roles do not downgrade it.</summary>
public static class WorkspacePolicy
{
    public static string Instructions(JobRecord job, AgentConfig agent, bool allowChanges = false)
    {
        if (Catalog.IsBrowserChat(agent.Connection)) return (job.Surface == "work" ? "HỖ TRỢ CÔNG VIỆC QUA VĂN BẢN: tư vấn thiết kế, đề xuất code hoặc phản biện dựa trên nội dung được gửi và kết quả đồng đội cung cấp. CLI hoặc người dùng áp dụng thay đổi và kiểm tra tại project; chatbot này không tự thực thi trên máy.\n" : "TRÒ CHUYỆN ĐỘC LẬP: không gắn project, không cần thư mục làm việc của người dùng.\n")
            + "CHATBOT TRÊN WEB: trao đổi và nghiên cứu chủ đề bằng các khả năng sẵn có của chatbot, gồm tìm kiếm web nếu giao diện hiện tại hỗ trợ. Không có quyền đọc/sửa thư mục hoặc chạy terminal trên máy qua Hub. Phân biệt bằng chứng đã xác minh với ý tưởng; dẫn nguồn cho nhận định nghiên cứu. Không giả là đã chạy code hay đọc project chỉ từ đường dẫn.\n";
        if (job.Surface == "chat") return "Chế độ TRÒ CHUYỆN: trả lời bằng văn bản. Không dùng công cụ, không đọc/sửa file hoặc chạy lệnh. Muốn đọc project cần chuyển sang Hub Code.\n";
        string project = $"PROJECT ĐANG CHỌN: {job.Project}\nThư mục làm việc của CLI: {job.Project}\n";
        if (Catalog.IsApi(agent.Connection)) return project + "Kết nối này chỉ nhận văn bản qua API, không có công cụ đọc filesystem trên máy. Dùng nội dung file được đính kèm hoặc đồng đội CLI cung cấp; nếu thiếu, nêu rõ giới hạn. Không khẳng định đã đọc file chỉ từ đường dẫn.\n";
        string access = "Dùng công cụ file và terminal theo quyền CLI đã chọn để làm yêu cầu trong project. " +
            "Khi yêu cầu liên quan project, hãy thực sự đọc các file liên quan trước khi kết luận, dẫn đường dẫn cụ thể. Không yêu cầu người dùng dán code chỉ vì hội thoại chưa chứa nội dung file; CLI đang chạy tại project có thể đọc trực tiếp. " +
            "Nhận xét cũ chưa đọc được code hoặc bị giới hạn chỉ đọc không thay thế quyền hiệu lực trong lượt hiện tại. Nếu công cụ thật sự lỗi, ghi rõ đường dẫn và thông báo lỗi thay vì suy đoán folder trống hoặc không có quyền.\n";
        string permission = agent.Permission switch {
            "full" => "Người dùng đã chọn TOÀN QUYỀN cho CLI: được đọc/sửa file và chạy terminal theo yêu cầu. Hub Code giữ nguyên quyền này; không tự coi lượt thảo luận/review là sandbox chỉ đọc.\n",
            "workspace-write" => "Người dùng chọn quyền ghi workspace: được đọc/sửa file và dùng terminal trong giới hạn CLI của quyền này.\n",
            "read-only" => "Người dùng chọn quyền CHỈ ĐỌC: đọc, tìm kiếm và phân tích; không sửa file hoặc chạy thao tác ghi.\n",
            _ => "Quyền theo cấu hình CLI hiện tại; công cụ có thể cần allow-rule riêng khi chạy ngầm. Không tự nâng quyền.\n"
        };
        string scope = allowChanges ? "Thực hiện công việc theo yêu cầu và quyền được cấp; giữ thay đổi sẵn có của người dùng.\n" :
            "Tập trung vào mục tiêu của lượt thảo luận/review. Quyền công cụ được giữ nguyên; chỉ thực hiện thao tác phù hợp yêu cầu người dùng và bước công việc hiện tại, không tự triển khai ý tưởng chưa được yêu cầu.\n";
        string tools = agent.Connection == "agy-cli" && agent.Permission != "full" ?
            "Antigravity chạy headless: ưu tiên công cụ đọc file, liệt kê thư mục và tìm kiếm tích hợp, không gọi terminal chỉ để đọc file. " +
            "Quyền đọc file trong workspace không đồng nghĩa được chạy shell. Nếu terminal bị từ chối quyền, chuyển sang công cụ đọc/tìm file; " +
            "không yêu cầu đăng nhập lại hoặc tự nâng quyền, không lặp lệnh bị chặn.\n" : "";
        return project + $"Quyền CLI hiệu lực trong lượt này: {agent.Permission}.\n" + permission + access + tools + scope;
    }
}
