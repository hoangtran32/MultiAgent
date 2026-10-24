using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed class CodingRolesDialog : Form
{
    public CodingOptions Result { get; private set; }
    private readonly CancellationTokenSource lifetime = new();
    private bool lifetimeDisposed;
    public CodingRolesDialog(CodingOptions current, IReadOnlyList<AgentConfig> agents, ISecretStore secrets, ChatGptPlan plan, bool live)
    {
        Result = current.Copy(); SuspendLayout(); Text = "Luồng IDE • Đầu não & Thợ code"; Font = new("Segoe UI", 10); ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; Icon = Ui.AppIcon;
        var root = Ui.Table(); root.Padding = new(16); root.AutoScroll = true; Controls.Add(root); int row = 0;
        void Add(Control control) { root.RowStyles.Add(new(SizeType.AutoSize)); root.Controls.Add(control, 0, row++); control.Margin = new(0, 5, 0, 10); }
        var enabled = new CheckBox { Text = "Chốt/review toàn bộ giải pháp, sau đó tự chuyển sang Đầu não → Thợ code", Checked = current.Enabled, Enabled = !live, AutoSize = true }; Add(enabled);
        var explanation = Ui.Label("Đầu não đọc code và đưa hướng thuật toán, cấu trúc dữ liệu, bước triển khai. Thợ code nhận bàn giao, sửa project, sau đó qua kiểm tra và reviewer. Model riêng cho vai có thể khác model dùng khi thảo luận. Không tự đổi vai sang model khác nếu kết nối được chọn lỗi/hết quota."); explanation.MaximumSize = new(750, 0); Add(explanation);
        var fields = new List<(string Role, ComboBox Agent, ComboBox Model)>();
        foreach (var item in new[] { ("brain", "Đầu não • thuật toán & hướng triển khai", current.Brain), ("implementer", "Thợ code • triển khai & sửa lỗi", current.Implementer) }) {
            Add(Ui.Label(item.Item2, true));
            var line = Ui.Table(3); line.AutoSize = true; line.ColumnStyles.Add(new(SizeType.Percent, 45)); line.ColumnStyles.Add(new(SizeType.Percent, 55)); line.ColumnStyles.Add(new(SizeType.AutoSize));
            var connection = Ui.Combo(); connection.Dock = DockStyle.Fill;
            Ui.Choices(connection, new[] { new Choice("", "Chọn agent tham gia phiên…") }.Concat(agents.Select(a => new Choice(a.Id, a.Name))), item.Item3.AgentId);
            var model = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 120, AccessibleName = "Model " + item.Item2 };
            void Fill() {
                var selected = agents.FirstOrDefault(a => a.Id == Ui.Value(connection));
                model.Items.Clear(); if (selected == null) return;
                model.Items.AddRange(LocalModels.Read(selected).Append(selected.Model).Where(s => s.Length > 0).Distinct().Cast<object>().ToArray());
                model.Text = selected.Id == item.Item3.AgentId && item.Item3.Model.Length > 0 ? item.Item3.Model : selected.Model;
            }
            connection.SelectedIndexChanged += (_, _) => Fill(); Fill();
            var discover = Ui.Button("Dò model");
            discover.Click += async (_, _) => {
                var selected = agents.FirstOrDefault(a => a.Id == Ui.Value(connection)); if (selected == null) return;
                string id = selected.Id, previousText = model.Text; discover.Enabled = false;
                try {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var choices = selected.Connection == "chatgpt-plan" ? (await plan.Models(selected.PlanAccountId, timeout.Token)).Select(c => c.Id).ToArray() : (await AgentDiscovery.Models(selected, secrets, timeout.Token)).Models.ToArray();
                    if (IsDisposed || Ui.Value(connection) != id) return;
                    string currentText = model.Text; model.Items.Clear(); model.Items.AddRange(choices.Distinct().Cast<object>().ToArray()); model.Text = currentText;
                } catch (Exception error) when (error is not OutOfMemoryException) { if (!IsDisposed && !lifetime.IsCancellationRequested) MessageBox.Show(this, error is OperationCanceledException ? "Đã hủy/quá thời gian dò metadata; chưa gọi suy luận." : error.Message, Text); }
                finally { if (!IsDisposed) discover.Enabled = true; }
            };
            line.Controls.Add(connection, 0, 0); line.Controls.Add(model, 1, 0); line.Controls.Add(discover, 2, 0); Add(line); fields.Add((item.Item1, connection, model));
        }
        var roleNote = Ui.Label(live ? "Phân công mới áp dụng sau bước đang chạy; không ngắt thao tác hiện tại hoặc chạy lại task đã xong. Trong phiên chỉ chọn agent đang tham gia; thêm kết nối ở Agent & model cho phiên sau." : "Hai agent được chọn sẽ tham gia phiên. Để trống model riêng để dùng model của agent. Luồng IDE dùng trong Hub Code → Chốt từng ý và bật Thực hiện task; chưa bật thì giữ cách phối hợp hiện tại."); roleNote.MaximumSize = new(750, 0); Add(roleNote);
        var actions = Ui.Flow(); actions.Controls.Add(Ui.Button(live ? "Áp dụng sau bước hiện tại" : "Lưu phân công", (_, _) => {
            try {
                RoleBinding Binding(string role) { var field = fields.First(f => f.Role == role); return new() { Role = role, AgentId = Ui.Value(field.Agent), Model = field.Model.Text.Trim() }; }
                Result = new() { Enabled = enabled.Checked, Brain = Binding("brain"), Implementer = Binding("implementer") }; Result.Validate(); DialogResult = DialogResult.OK;
            } catch (ArgumentException error) { MessageBox.Show(this, error.Message, Text); }
        }, true)); actions.Controls.Add(Ui.Button("Hủy", (_, _) => DialogResult = DialogResult.Cancel)); Add(actions);
        FormClosed += (_, _) => lifetime.Cancel(); float scale = DeviceDpi / 96f; root.Scale(new SizeF(scale, scale)); Ui.Theme(this); AutoScaleDimensions = new(DeviceDpi, DeviceDpi); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        var area = Screen.FromControl(this).WorkingArea; Size = new(Math.Min((int)(840 * scale), area.Width - 40), Math.Min((int)(640 * scale), area.Height - 40));
    }
    protected override void Dispose(bool disposing) { if (disposing && !lifetimeDisposed) { lifetimeDisposed = true; lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
