using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed class AgentConnectionsDialog : Form
{
    private readonly CancellationTokenSource lifetime = new();
    private bool lifetimeDisposed;
    public AgentConnectionsDialog(IEnumerable<AgentConfig> configured, ISecretStore secrets)
    {
        var agents = configured.Select(a => a.Copy()).ToList();
        SuspendLayout(); AutoScaleMode = AutoScaleMode.None; Text = "Tài khoản & kết nối agent"; Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; Icon = Ui.AppIcon;
        var root = Ui.Table(); root.Padding = new Padding(14); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
        var note = Ui.Label("Kiểm tra trạng thái CLI hoặc danh sách model API; không gửi tác vụ suy luận. Hub dùng tài khoản đã đăng nhập trong từng công cụ, không sao chép token."); note.MaximumSize = new Size(850, 0); root.Controls.Add(note, 0, 0);
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        grid.Columns.Add("name", "Agent"); grid.Columns.Add("connection", "Kết nối"); grid.Columns.Add("state", "Trạng thái");
        foreach (var agent in agents) grid.Rows.Add(agent.Name, Catalog.ConnectionName(agent), "Chưa kiểm tra"); root.Controls.Add(grid, 0, 1);
        var detail = Ui.Text(true); detail.ReadOnly = true; root.Controls.Add(detail, 0, 2);
        var states = new Dictionary<string, AgentHealth>();
        void ShowDetail() { int index = grid.CurrentRow?.Index ?? -1; detail.Text = index >= 0 && states.TryGetValue(agents[index].Id, out var state) ? state.Detail.ReplaceLineEndings() : "Chọn agent rồi bấm Kiểm tra tất cả."; }
        grid.SelectionChanged += (_, _) => ShowDetail();
        var buttons = Ui.Flow(); var inspect = Ui.Button("Kiểm tra tất cả"); var login = Ui.Button("Đăng nhập agent đã chọn", (_, _) => {
            try { int index = grid.CurrentRow?.Index ?? -1; if (index < 0) return; AgentConnections.OpenLogin(agents[index]); detail.Text = "Đã mở luồng chính thức. Hoàn thành đăng nhập trong cửa sổ CLI/trình duyệt, rồi bấm Kiểm tra tất cả."; }
            catch (Exception e) { detail.Text = e.Message; }
        });
        inspect.Click += async (_, _) => {
            inspect.Enabled = login.Enabled = false;
            try {
                for (int i = 0; i < agents.Count; i++) {
                    grid.Rows[i].Cells[2].Value = "Đang kiểm tra…";
                    var state = await AgentConnections.Inspect(agents[i], secrets, lifetime.Token);
                    if (IsDisposed) return; states[agents[i].Id] = state;
                    grid.Rows[i].Cells[2].Value = state.State switch { "ready" => "Đã xác nhận", "missing" => "Thiếu CLI", "manual" => "Thủ công", "browser" => "Kiểm tra tab trong bridge", "model" => "Kiểm tra model", "permission" => "Thiếu quyền công cụ", "auth" => "Cần đăng nhập", _ => "Chưa xác nhận" }; ShowDetail();
                }
            } catch (OperationCanceledException) { }
            finally { if (!IsDisposed) inspect.Enabled = login.Enabled = true; }
        };
        buttons.Controls.AddRange([inspect, login, Ui.Button("Đóng", (_, _) => Close())]); root.Controls.Add(buttons, 0, 3);
        FormClosed += (_, _) => lifetime.Cancel();
        float scale = DeviceDpi / 96f; root.Scale(new SizeF(scale, scale)); Ui.Theme(this); AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        var area = Screen.FromControl(this).WorkingArea; Size = new Size(Math.Min((int)(950 * scale), area.Width - 40), Math.Min((int)(650 * scale), area.Height - 40));
    }
    protected override void Dispose(bool disposing) { if (disposing && !lifetimeDisposed) { lifetimeDisposed = true; lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
