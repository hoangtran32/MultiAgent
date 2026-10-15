using System.Text.Json;
using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed class TeamDialog : Form
{
    public TeamOptions Result { get; private set; }
    public TeamDialog(TeamOptions current, IReadOnlyList<AgentConfig> agents, string project, bool consensus = false)
    {
        Result = current.Copy(); SuspendLayout(); AutoScaleMode = AutoScaleMode.None;
        Text = (consensus ? "Chốt từng ý" : "Bàn tròn & thực hiện") + " • phân vai và giới hạn"; Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false; Icon = Ui.AppIcon;
        var root = Ui.Table(); root.Padding = new Padding(14); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
        var tabs = new TabControl { Dock = DockStyle.Fill }; root.Controls.Add(tabs, 0, 0);
        var rolesPage = new TabPage("Nhóm & giới hạn"); var checksPage = new TabPage("Lệnh kiểm tra"); tabs.TabPages.AddRange([rolesPage, checksPage]);
        var roles = Ui.Table(3); roles.Padding = new Padding(10); roles.AutoScroll = true;
        roles.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); roles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); roles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); rolesPage.Controls.Add(roles);
        var note = Ui.Label(consensus ? "Một ý tại một thời điểm. Tất cả thành viên khả dụng xác nhận cùng phiên bản trước khi thực hiện/review rồi chuyển ý. Sửa ý làm hết hiệu lực xác nhận cũ; không ép đồng ý khi hết giới hạn." : "Các vai ngang hàng. Một model có thể đảm nhiệm nhiều vai. Để Tự phân công để Hub chia chuyên môn; khi quota/lỗi sẽ chọn kết nối còn lại phù hợp.");
        note.MaximumSize = new Size(760, 0); roles.Controls.Add(note, 0, 0); roles.SetColumnSpan(note, 3);
        roles.Controls.Add(Ui.Label("Chuyên môn", true), 0, 1); roles.Controls.Add(Ui.Label("Kết nối ưu tiên", true), 1, 1); roles.Controls.Add(Ui.Label("Model riêng (trống = theo agent)", true), 2, 1);
        var fields = new List<(string role, ComboBox agent, TextBox model)>(); int row = 2;
        foreach (var role in TeamRoles.All) {
            var binding = current.Roles.FirstOrDefault(r => r.Role == role.Id);
            var agent = Ui.Combo(); agent.Dock = DockStyle.Fill; agent.AccessibleName = "Kết nối cho " + role.Name;
            Ui.Choices(agent, new[] { new Choice("", "Tự phân công / dự phòng tự động") }.Concat(agents.Select(a => new Choice(a.Id, a.Name + (a.Enabled ? "" : " (chưa chọn)")))), binding?.AgentId);
            var model = Ui.Text(); model.Text = binding?.Model ?? ""; model.MaxLength = 120; model.AccessibleName = "Model cho " + role.Name;
            roles.Controls.Add(Ui.Label(role.Name), 0, row); roles.Controls.Add(agent, 1, row); roles.Controls.Add(model, 2, row++); fields.Add((role.Id, agent, model));
        }
        NumericUpDown Number(string title, int min, int max, int value) {
            var box = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 100, Margin = new Padding(4, 6, 4, 4), AccessibleName = title };
            roles.Controls.Add(Ui.Label(title), 0, row); roles.SetColumnSpan(roles.GetControlFromPosition(0, row)!, 2); roles.Controls.Add(box, 2, row++); return box;
        }
        var calls = Number("Tổng số lần gọi model tối đa", 2, 400, current.MaxCalls);
        var minutes = Number("Thời gian tối đa (phút)", 1, 1440, current.DurationMinutes);
        var tasks = Number("Số task tối đa", 1, 50, current.MaxTasks);
        var revisions = Number("Số vòng sửa tối đa / task", 0, 5, current.MaxRevisions);
        var execute = new CheckBox { Text = consensus ? "Thực hiện từng ý sau khi tất cả thành viên chốt" : "Thực hiện task sau khi bàn tròn thống nhất", Checked = current.ExecuteTasks, AutoSize = true, Margin = new Padding(4, 12, 4, 4) };
        roles.Controls.Add(execute, 0, row++); roles.SetColumnSpan(execute, 3);
        var permission = Ui.Label("Coder sửa project cần CLI và quyền workspace-write/full trong Agent & model. Hub không tự tăng quyền. API trả lời văn bản vẫn tham gia bàn tròn, tài liệu và review. Trong Trò chuyện, task không được thực thi.");
        permission.MaximumSize = new Size(760, 0); roles.Controls.Add(permission, 0, row); roles.SetColumnSpan(permission, 3);

        var checks = Ui.Table(); checks.Padding = new Padding(10); checks.RowStyles.Add(new RowStyle(SizeType.AutoSize)); checks.RowStyles.Add(new RowStyle(SizeType.AutoSize)); checks.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        checksPage.Controls.Add(checks);
        var verify = new CheckBox { Text = "Tự chạy các lệnh bên dưới sau mỗi task sửa code", Checked = current.RunVerification, AutoSize = true, Margin = new Padding(4, 8, 4, 8) }; checks.Controls.Add(verify, 0, 0);
        var checkNote = Ui.Label("Chỉ thêm lệnh build/test bạn tin cậy cho project đang chọn. Executable .exe; đối số là mảng JSON, ví dụ [\"build\"]. Không nhập cả chuỗi lệnh shell. Để tắt nếu bạn tự kiểm tra; kết quả sẽ ghi Chưa kiểm chứng.");
        checkNote.MaximumSize = new Size(760, 0); checks.Controls.Add(checkNote, 0, 1);
        var grid = new DataGridView { Dock = DockStyle.Fill, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToResizeRows = false, AccessibleName = "Các lệnh kiểm tra của project" };
        foreach (var item in new[] { ("name", "Tên", 100f), ("exe", "Executable", 180f), ("args", "Đối số JSON", 230f), ("timeout", "Timeout (giây)", 80f) }) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = item.Item1, HeaderText = item.Item2, FillWeight = item.Item3 });
        foreach (var command in current.Checks) grid.Rows.Add(command.Name, command.Executable, JsonSerializer.Serialize(command.Arguments), command.TimeoutSeconds);
        checks.Controls.Add(grid, 0, 2);
        var buttons = Ui.Flow(); var save = Ui.Button("Lưu", (_, _) => {
            try {
                grid.EndEdit(); var next = new TeamOptions { MaxCalls = (int)calls.Value, DurationMinutes = (int)minutes.Value, MaxTasks = (int)tasks.Value, MaxRevisions = (int)revisions.Value,
                    ExecuteTasks = execute.Checked, RunVerification = verify.Checked, VerificationProject = project,
                    Roles = fields.Select(f => new RoleBinding { Role = f.role, AgentId = Ui.Value(f.agent), Model = f.model.Text.Trim() }).ToList() };
                foreach (DataGridViewRow data in grid.Rows) {
                    if (data.IsNewRow || data.Cells.Cast<DataGridViewCell>().All(c => string.IsNullOrWhiteSpace(c.Value?.ToString()))) continue;
                    next.Checks.Add(new() { Name = data.Cells[0].Value?.ToString()?.Trim() ?? "", Executable = data.Cells[1].Value?.ToString()?.Trim() ?? "",
                        Arguments = JsonSerializer.Deserialize<List<string>>(data.Cells[2].Value?.ToString() ?? "[]") ?? [], TimeoutSeconds = int.Parse(data.Cells[3].Value?.ToString() ?? "300") });
                }
                next.Validate(); Result = next; DialogResult = DialogResult.OK;
            } catch (Exception e) { MessageBox.Show(this, e.Message, "Chưa lưu được cài đặt", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }, true);
        var cancel = Ui.Button("Hủy", (_, _) => DialogResult = DialogResult.Cancel); buttons.Controls.AddRange([save, cancel]); root.Controls.Add(buttons, 0, 1); AcceptButton = save; CancelButton = cancel;
        float scale = DeviceDpi / 96f; root.Scale(new SizeF(scale, scale)); Ui.Theme(this); AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        var area = Screen.FromControl(this).WorkingArea; Size = new Size(Math.Min((int)(900 * scale), area.Width - 40), Math.Min((int)(780 * scale), area.Height - 40)); MinimumSize = new Size((int)(640 * scale), (int)(500 * scale));
    }
}
