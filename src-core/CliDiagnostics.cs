namespace MultiAgentHub.Core;

/// <summary>Known CLI diagnostics safe to show without exposing raw authentication output.</summary>
public sealed class CliDiagnosticException(string state, string message) : InvalidOperationException(message)
{
    public string State { get; } = state;
}

public static class CliDiagnostics
{
    public static CliDiagnosticException? Explain(AgentConfig agent, string stderr)
    {
        if (agent.Connection != "agy-cli") return null;
        bool Has(string value) => stderr.Contains(value, StringComparison.OrdinalIgnoreCase);
        if (Has("headless") && (Has("auto-denied") || Has("cannot prompt")))
            return new("permission", $"Antigravity không có quyền thực hiện công cụ trong chế độ chạy ngầm (quyền hiệu lực: {agent.Permission}). " +
                "CLI không thể hiện hộp xin phép nên thao tác đã bị từ chối. " +
                "Đọc project: dùng công cụ đọc/tìm file có sẵn. Nếu cần terminal, cấu hình permissions.allow cho lệnh cần dùng trong settings.json của Antigravity CLI; " +
                "hoặc chọn Agent & model → Thiết lập / model → Quyền CLI → Tự phê duyệt • toàn quyền để cho phép mọi công cụ ở lượt làm việc. " +
                "Hub Code dùng quyền đã chọn cả trong thảo luận/review. Đây là lỗi quyền công cụ, không phải lỗi đăng nhập.");
        if (Has("authentication required") || Has("not authenticated") || Has("please log in") || Has("please sign in"))
            return new("auth", "Antigravity CLI yêu cầu đăng nhập. Mở Tài khoản & kết nối agent → chọn Antigravity CLI → Đăng nhập agent đã chọn, hoàn tất trong CLI/trình duyệt rồi kiểm tra lại.");
        return null;
    }
}
