using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MultiAgentHub.Core;

public static class CodingWorkflow
{
    public static bool Active(JobRecord job) => job.Surface == "work" && job.Mode == "consensus" && job.Team is { Options.ExecuteTasks: true, Options.Coding.Enabled: true };
    public static bool Planning(JobRecord job) => Active(job) && job.Team!.CodingStage != "implementing";
    public static void Validate(CodingOptions options, IReadOnlyList<AgentConfig> agents, bool harnessEnabled)
    {
        options.Validate(); if (!options.Enabled) return;
        foreach (var binding in new[] { options.Brain, options.Implementer }) {
            var agent = agents.FirstOrDefault(a => a.Id == binding.AgentId) ?? throw new ArgumentException("Agent cho " + TeamRoles.Name(binding.Role) + " chưa được chọn tham gia phiên.");
            if (Catalog.IsManual(agent.Connection)) throw new ArgumentException("Vai code cần kết nối tự động; không dùng sao chép/dán.");
        }
        var worker = agents.First(a => a.Id == options.Implementer.AgentId);
        bool write = (worker.Connection is "codex-cli" or "claude-cli" or "agy-cli" or "custom-cli" || harnessEnabled && ProviderHarnessModel.Supported(worker.Connection)) && worker.Permission is "workspace-write" or "full";
        if (!write) throw new ArgumentException("Thợ code cần CLI hoặc API có harness và quyền ghi workspace/full. Chatbot web chưa có công cụ sửa project.");
    }
    public static string PlanHash(TeamRun team) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(team.Tasks.Select(t => new { t.Id, t.ProposalVersion, t.Title, t.Description, t.Acceptance, t.DependsOn, t.RequiresWorkspace }), Json.Options))));
    public const string PlanningContract = """
        LUỒNG IDE: CHỐT TOÀN BỘ GIẢI PHÁP TRƯỚC KHI CODE.
        Mỗi lần vẫn đề xuất/phản hồi một ý nhỏ và chốt đúng phiên bản. Ý đã chốt được lưu planned,
        chưa thực hiện. Có thể đề xuất ý tiếp theo phụ thuộc ý planned. Không sửa project lúc này.
        Khi kế hoạch bao phủ đủ yêu cầu, finished=true là sẵn sàng review kế hoạch, KHÔNG phải đã code.
        Chỉ sau khi mọi thành viên review toàn bộ giải pháp đồng ý, Hub mới chuyển sang Đầu não → Thợ code.
        """;
    public const string DesignContract = """
        ĐẦU NÃO THUẬT TOÁN — CHỈ THIẾT KẾ, KHÔNG SỬA FILE.
        Dựa trên giải pháp đã được review và task được giao, đọc code liên quan, chỉ dẫn thuật toán,
        cấu trúc dữ liệu, giao diện/hàm cần đổi, các bước triển khai và tiêu chí kiểm tra cụ thể cho Thợ code.
        Giữ phạm vi đã chốt, không mở thêm task. Kết thúc khối hub với outcome="ready" và implementationPlan
        là hướng dẫn triển khai đầy đủ (tối đa 16.000 ký tự); hoặc outcome="blocked" với lý do/điều kiện.
        Không báo done vì vai này chưa thực hiện. tasks/taskReviews/taskAmendments để rỗng.
        """;
}
