namespace MultiAgentHub.Core;

public sealed class MissingAgentExecutableException(AgentConfig agent) : FileNotFoundException(Describe(agent))
{
    public string AgentId { get; } = agent.Id;

    private static string Describe(AgentConfig agent)
    {
        string requested = agent.Command.Length > 0 ? agent.Command : agent.Connection switch {
            "codex-cli" => "codex.exe", "claude-cli" => "claude.exe", "agy-cli" => "agy.exe", _ => "executable của agent" };
        return $"Không tìm thấy {requested}.\nAgent: {agent.Name}\nKết nối: {Catalog.ConnectionName(agent)} ({agent.Connection})\n\n"
            + (agent.Command.Length > 0
                ? "Đường dẫn đã chọn không còn tồn tại. Xóa đường dẫn để tự dò lại hoặc chọn đúng file .exe. "
                : "Đã dò thư mục cài đặt và PATH của tiến trình, người dùng, hệ thống. ")
            + "Mở thiết lập của agent để kiểm tra. Đây chưa phải lỗi đăng nhập hoặc hết quota.";
    }
}
