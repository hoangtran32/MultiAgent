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
        var limitsPage = new TabPage("Trao đổi & giới hạn"); var rolesPage = new TabPage("Phân vai & model"); var checksPage = new TabPage("Lệnh kiểm tra"); tabs.TabPages.AddRange([limitsPage, rolesPage, checksPage]);
        TableLayoutPanel Content(TabPage page, int columns) {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) }; page.Controls.Add(scroll);
            var table = Ui.Table(columns); table.Dock = DockStyle.Top; table.AutoSize = true; table.AutoSizeMode = AutoSizeMode.GrowAndShrink; scroll.Controls.Add(table); return table;
        }
        var limits = Content(limitsPage, 2);
        limits.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); limits.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        var continuous = new CheckBox { Text = "Trao đổi liên tục — không giới hạn lượt hoặc thời gian", AutoSize = true,
            Checked = current.MaxCalls == 0 && current.DurationMinutes == 0 && current.ExchangeTimeoutMinutes == 0 && current.WorkTimeoutMinutes == 0, Margin = new Padding(4, 8, 4, 12) };
        limits.Controls.Add(continuous, 0, 0); limits.SetColumnSpan(continuous, 2);
        var limitNote = Ui.Label("0 = không giới hạn. Một lần gọi là một lượt của một model; A nói rồi B phản hồi tính 2 lần. Nhóm vẫn dừng khi hoàn tất, bạn bấm Dừng, CLI lỗi/hết quota hoặc có vướng mắc cần xử lý.");
        limitNote.MaximumSize = new Size(690, 0); limits.Controls.Add(limitNote, 0, 1); limits.SetColumnSpan(limitNote, 2); int limitRow = 2;
        NumericUpDown Number(TableLayoutPanel table, string title, int min, int max, int value, ref int targetRow) {
            var box = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 130, Margin = new Padding(4, 8, 4, 8), AccessibleName = title };
            var label = Ui.Label(title); label.AutoSize = false; label.Dock = DockStyle.Fill; label.Height = 52; label.MinimumSize = new Size(0, 52); label.TextAlign = ContentAlignment.MiddleLeft;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(label, 0, targetRow); table.Controls.Add(box, 1, targetRow++); return box;
        }
        var calls = Number(limits, "Tổng số lần gọi model tối đa (0 = không giới hạn)", 0, 100000, current.MaxCalls, ref limitRow);
        var minutes = Number(limits, "Thời gian cả phiên, phút (0 = không giới hạn)", 0, 10080, current.DurationMinutes, ref limitRow);
        var exchangeTimeout = Number(limits, "Thời gian mỗi lượt trao đổi/review, phút (0 = không giới hạn)", 0, 10080, current.ExchangeTimeoutMinutes, ref limitRow);
        var workTimeout = Number(limits, "Thời gian mỗi lượt thực hiện, phút (0 = không giới hạn)", 0, 10080, current.WorkTimeoutMinutes, ref limitRow);
        var taskNote = Ui.Label("Giới hạn phạm vi task và vòng sửa độc lập với số lần gọi. Nếu còn bất đồng hoặc task chưa đạt tiêu chí, Hub giữ trạng thái chưa xong.");
        taskNote.MaximumSize = new Size(690, 0); limits.Controls.Add(taskNote, 0, limitRow++); limits.SetColumnSpan(taskNote, 2);
        var tasks = Number(limits, "Số task tối đa", 1, 50, current.MaxTasks, ref limitRow);
        var revisions = Number(limits, "Số vòng sửa tối đa / task", 0, 5, current.MaxRevisions, ref limitRow);
        void UpdateContinuous() { foreach (var box in new[] { calls, minutes, exchangeTimeout, workTimeout }) { if (continuous.Checked) box.Value = 0; box.Enabled = !continuous.Checked; } }
        continuous.CheckedChanged += (_, _) => UpdateContinuous(); UpdateContinuous();
        var roles = Content(rolesPage, 3);
        roles.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); roles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); roles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
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
        var execute = new CheckBox { Text = consensus ? "Thực hiện từng ý sau khi tất cả thành viên chốt" : "Thực hiện task sau khi bàn tròn thống nhất", Checked = current.ExecuteTasks, AutoSize = true, Margin = new Padding(4, 12, 4, 4) };
        roles.Controls.Add(execute, 0, row++); roles.SetColumnSpan(execute, 3);
        var permission = Ui.Label("Thực hiện project cần CLI hoặc API có harness và quyền workspace-write/full. Luồng IDE Đầu não → Thợ code được cấu hình riêng tại Phân công code; khi bật, Hub chốt và review toàn bộ giải pháp trước khi triển khai. Trong Trò chuyện, task không được thực thi.");
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
                grid.EndEdit(); var next = new TeamOptions { MaxCalls = continuous.Checked ? 0 : (int)calls.Value, DurationMinutes = continuous.Checked ? 0 : (int)minutes.Value,
                    ExchangeTimeoutMinutes = continuous.Checked ? 0 : (int)exchangeTimeout.Value, WorkTimeoutMinutes = continuous.Checked ? 0 : (int)workTimeout.Value,
                    MaxTasks = (int)tasks.Value, MaxRevisions = (int)revisions.Value,
                    ExecuteTasks = execute.Checked, RunVerification = verify.Checked, VerificationProject = project, Coding = current.Coding.Copy(),
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
