using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed partial class MainWindow
{
    private void ConfigureChatGptPlan() => Protect(() => {
        if (runner.Busy) return;
        using var dialog = new ChatGptPlanDialog(chatGptPlan);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedAgent is not { } selected) { Populate(Selected?.Id); return; }
        var next = CopyConfig();
        var existing = next.Agents.FirstOrDefault(a => a.Connection == "chatgpt-plan" && a.PlanAccountId == selected.PlanAccountId && a.Model == selected.Model);
        if (existing == null) {
            if (next.Agents.Count >= Catalog.MaxAgents) throw new InvalidOperationException($"Tối đa {Catalog.MaxAgents} agent.");
            next.Agents.Add(selected); existing = selected;
        }
        next.SelectAgent(existing, true); Commit(next, existing.Id);
        status.Text = "Đã chọn model từ API tài khoản ChatGPT. Gửi tin nhắn để trò chuyện; chưa gọi suy luận để kiểm tra.";
    });
}
