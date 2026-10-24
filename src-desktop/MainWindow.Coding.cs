using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed partial class MainWindow
{
    internal void ShowAgentSettings()
    {
        if (agentPanel.FindForm() is { } dialog && dialog != this && dialog.Visible) { dialog.Activate(); return; }
        Activate(); OpenAgents();
    }
    private void OpenCodeIde()
    {
        if (runner.Busy) { status.Text = "Phiên hiện tại vẫn đang chạy. Đợi hoặc dừng phiên trước khi chuyển sang IDE."; return; }
        using var launch = new IdeLaunchWindow(storage, config.Project); launch.ShowDialog(this);
        if (launch.Launched) { config.Surface = "work"; Protect(Save); Close(); }
    }
    private void ConfigureCodingRoles() => Protect(() => {
        var active = runner.Snapshot(); bool live = runner.Busy && active != null && CodingWorkflow.Active(active);
        if (runner.Busy && !live) throw new InvalidOperationException("Phiên hiện tại chưa dùng luồng IDE; cấu hình phân công khi phiên này kết thúc.");
        var choices = live ? config.Agents.Where(a => active!.Results.Any(r => r.AgentId == a.Id)).ToList() : config.Agents;
        using var dialog = new CodingRolesDialog(live ? active!.Team!.PendingCoding ?? active.Team.Options.Coding : config.Team.Coding, choices, secrets, chatGptPlan, live);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var next = CopyConfig(); next.Team.Coding = dialog.Result;
        if (!live && dialog.Result.Enabled) {
            next.Mode = next.WorkMode = "consensus"; next.Team.ExecuteTasks = true;
            foreach (string id in new[] { dialog.Result.Brain.AgentId, dialog.Result.Implementer.AgentId }) next.SelectAgent(next.Agents.First(a => a.Id == id), true);
            CodingWorkflow.Validate(next.Team.Coding, next.SelectedAgents(), next.Harness.Enabled);
        }
        bool applyNow = live && runner.Busy && runner.Snapshot() is { State: "running" } running && running.Team?.Phase != "finished";
        if (applyNow) {
            try { runner.UpdateCodingRoles(dialog.Result); }
            catch (InvalidOperationException) when (runner.Snapshot() is not { State: "running", Team.Phase: not "finished" }) { applyNow = false; }
        }
        Storage.Validate(next); storage.Write("native-config", next); config = next;
        if (!applyNow) Populate(Selected?.Id); else UpdateSummary();
        status.Text = applyNow ? "Đã nhận phân công mới; áp dụng tại ranh giới bước, giữ nguyên thao tác và task đã xong." : "Đã lưu luồng IDE: chốt và review toàn bộ giải pháp → Đầu não → Thợ code → kiểm tra/review.";
    });
}
