using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed class AgentDiscoveryDialog : Form
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<AgentConfig> configured;
    private readonly ISecretStore secrets;
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly ConversationTranscript detail = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10) };
    private readonly Label status = Ui.Label("");
    private readonly Button refresh = Ui.Button("Dò lại"), select = Ui.Button("Chọn công cụ", primary: true);
    private List<DiscoveredAgent> found = [];
    private bool scanning, disposedLifetime;
    public DiscoveredAgent? Selected { get; private set; }

    public AgentDiscoveryDialog(IEnumerable<AgentConfig> configured, ISecretStore secrets)
    {
        this.configured = configured.Select(a => a.Copy()).ToList(); this.secrets = secrets;
        SuspendLayout(); AutoScaleMode = AutoScaleMode.None; Text = "Dò công cụ & model"; Font = new Font("Segoe UI", 10); Icon = Ui.AppIcon;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var root = Ui.Table(); root.Padding = new Padding(14); root.RowCount = 5;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
        var note = Ui.Label("Dò CLI đã cài và model từ công cụ/cache/API đã cấu hình. Không gửi tác vụ AI. Chọn một công cụ để cấu hình; chưa thêm hay bật agent tự động."); note.MaximumSize = new Size(850, 0); root.Controls.Add(note, 0, 0);
        grid.Columns.Add("tool", "Công cụ"); grid.Columns.Add("connection", "Kết nối"); grid.Columns.Add("models", "Model"); grid.Columns.Add("path", "Executable / nguồn");
        grid.Columns[3].FillWeight = 180; root.Controls.Add(grid, 0, 1); root.Controls.Add(detail, 0, 2);
        status.AutoSize = false; status.Dock = DockStyle.Fill; root.Controls.Add(status, 0, 3);
        var actions = Ui.Flow(); actions.Controls.AddRange([refresh, select, Ui.Button("Đóng", (_, _) => Close())]); root.Controls.Add(actions, 0, 4);
        grid.SelectionChanged += (_, _) => ShowSelection(); refresh.Click += async (_, _) => await Scan();
        select.Click += (_, _) => { int index = grid.CurrentRow?.Index ?? -1; if (index < 0 || index >= found.Count) return; Selected = found[index]; DialogResult = DialogResult.OK; };
        Shown += async (_, _) => await Scan(); FormClosed += (_, _) => lifetime.Cancel();
        float scale = DeviceDpi / 96f; root.Scale(new SizeF(scale, scale)); Ui.Theme(this);
        AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        var area = Screen.FromControl(this).WorkingArea; Size = new Size(Math.Min((int)(1000 * scale), area.Width - 40), Math.Min((int)(700 * scale), area.Height - 40));
    }
    private void ShowSelection()
    {
        int index = grid.CurrentRow?.Index ?? -1; select.Enabled = !scanning && index >= 0 && index < found.Count;
        if (!select.Enabled) return;
        var tool = found[index]; select.Text = tool.Existing ? "Cấu hình agent này…" : "Thêm agent từ công cụ…";
        detail.SetPlainText(tool.Inventory.Source + "\n" + tool.Inventory.Detail + "\n\n" + tool.Executable + "\n\n" +
            (tool.Inventory.Models.Count > 0 ? string.Join("\n", tool.Inventory.Models) : "Không có danh sách model để chọn; vẫn có thể dùng mặc định của CLI hoặc nhập ID."), false);
    }
    private async Task Scan()
    {
        if (scanning) return; scanning = true; refresh.Enabled = select.Enabled = false; status.Text = "Đang dò công cụ và model…";
        try {
            var results = await AgentDiscovery.Scan(configured, secrets, lifetime.Token);
            if (IsDisposed) return; found = results; grid.Rows.Clear();
            foreach (var tool in found) grid.Rows.Add(tool.Agent.Name, Catalog.ConnectionName(tool.Agent), tool.Inventory.Models.Count, tool.Executable.Length > 0 ? tool.Executable : tool.Inventory.Source);
            status.Text = found.Count > 0 ? $"Tìm thấy {found.Count} công cụ/kết nối. Chọn để xem nguồn model và cấu hình." : "Chưa tìm thấy CLI. Cài công cụ rồi Dò lại, hoặc thêm CLI/API tùy chỉnh trong Agent & model.";
            if (found.Count == 0) detail.Clear();
        } catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) status.Text = e.Message; }
        finally { if (!IsDisposed) { scanning = false; refresh.Enabled = true; ShowSelection(); } }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposedLifetime) { disposedLifetime = true; lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
