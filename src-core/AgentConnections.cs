using System.Diagnostics;
using System.Text;

namespace MultiAgentHub.Core;

public sealed record AgentHealth(string State, string Detail);

public static class AgentConnections
{
    public static async Task<AgentHealth> Inspect(AgentConfig agent, ISecretStore secrets, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try {
            if (agent.Connection == "chatgpt-plan") return new("api", "Đăng nhập riêng trong Tài khoản ChatGPT • API. Dùng quyền và model API do gói ChatGPT cho phép; không dùng key tính phí hay phiên Codex CLI.");
            if (agent.Connection == "gateway-web") return new("browser", "Phiên API web quản lý trong Gateway & tài khoản web. Đăng nhập/xác minh trên trình duyệt; không dùng quota CLI hay API key dịch vụ.");
            if (Catalog.IsManual(agent.Connection)) return new("manual", "Kết nối sao chép/dán; đăng nhập được quản lý trong ứng dụng ngoài, Hub không xác minh tài khoản.");
            if (Catalog.IsBrowserChat(agent.Connection)) return new("browser", "Dùng tài khoản trong tab trình duyệt bạn kết nối qua extension; kiểm tra tab trong Chatbot từ trình duyệt. Hub không đọc token hoặc xác minh quota/model phía máy chủ.");
            if (Catalog.IsApi(agent.Connection)) {
                var models = await new ApiTransport(secrets).Models(agent, timeout.Token);
                return new("ready", $"API trả danh sách {models.Count} model. Chưa gửi yêu cầu suy luận hoặc kiểm tra quota.");
            }
            string? binary = ProcessTransport.Find(agent);
            if (binary == null) return new("missing", "Chưa tìm thấy executable. Cài CLI hoặc chọn đường dẫn trong Thiết lập / model.");
            if (agent.Connection == "agy-cli") {
                var models = await AntigravityModels.Load(agent, timeout.Token);
                bool selected = agent.Model.Length == 0 || models.Contains(agent.Model);
                return new(selected ? "ready" : "model", $"Antigravity trả {models.Count} model; {(selected ? "model đã chọn có trong danh sách" : "model đang chọn không có trong danh sách")}. Chưa kiểm tra quota suy luận hoặc quyền thực thi công cụ.\n" +
                    "Quyền default/Plan/Accept edits không tự cho phép mọi lệnh terminal khi chạy ngầm. Đọc file bằng công cụ tích hợp; lệnh cần permissions.allow hoặc toàn quyền trong lượt làm việc.\n" + string.Join("\n", models));
            }
            if (agent.Connection is "codex-cli" or "claude-cli") {
                var output = new StringBuilder();
                var outputGate = new object();
                void Append(string value) { lock (outputGate) if (output.Length < 16000) output.Append(value); }
                await ProcessTransport.Run(new(binary, agent.Connection == "codex-cli" ? ["login", "status"] : ["auth", "status"], ""), Path.GetTempPath(),
                    Append, Append, timeout.Token);
                // Status commands may include identity data. Only expose a summary, never their raw output.
                string method = agent.Connection == "codex-cli" && output.ToString().Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) ? "ChatGPT" : "cấu hình CLI hiện tại";
                return new("ready", $"CLI xác nhận đã đăng nhập qua {method}. Chưa gửi yêu cầu suy luận hoặc kiểm tra quota.");
            }
            return new("unknown", "Đã tìm thấy executable; CLI tùy chỉnh chưa khai báo lệnh xác minh đăng nhập.");
        } catch (OperationCanceledException) {
            token.ThrowIfCancellationRequested(); return new("unknown", "Kiểm tra quá 30 giây; chưa xác nhận được kết nối.");
        } catch (CliDiagnosticException e) {
            return new(e.State, e.Message);
        } catch (Exception) {
            return new("unknown", "Chưa xác nhận được đăng nhập/kết nối. Mở CLI chính thức để kiểm tra tài khoản, hoặc kiểm tra endpoint, key và mạng trong Thiết lập / model.");
        }
    }

    public static void OpenLogin(AgentConfig agent)
    {
        if (agent.Connection == "chatgpt-plan") throw new InvalidOperationException("Mở Agent & model → Tài khoản ChatGPT • API để đăng nhập riêng cho Hub.");
        if (Catalog.IsApi(agent.Connection)) throw new InvalidOperationException("Kết nối API dùng key trong Thiết lập / model; không dùng đăng nhập CLI.");
        if (agent.Connection is not ("codex-cli" or "claude-cli" or "agy-cli")) throw new InvalidOperationException("Mở ứng dụng ngoài hoặc executable tùy chỉnh để đăng nhập.");
        string binary = ProcessTransport.Find(agent) ?? throw new FileNotFoundException("Chưa cài CLI hoặc chưa chọn executable.");
        var start = new ProcessStartInfo(binary) { UseShellExecute = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), WindowStyle = ProcessWindowStyle.Normal };
        if (agent.Connection == "codex-cli") start.ArgumentList.Add("login");
        else if (agent.Connection == "claude-cli") { start.ArgumentList.Add("auth"); start.ArgumentList.Add("login"); }
        // agy exposes sign-in through its interactive session, not a documented login subcommand.
        Process.Start(start)?.Dispose();
    }
}
