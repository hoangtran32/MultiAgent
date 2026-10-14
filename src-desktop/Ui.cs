namespace MultiAgentHubApp;
internal static class Ui
{
    public static readonly Color Background = Color.White, Ink = Color.FromArgb(43, 45, 44), Accent = Color.FromArgb(55, 125, 239), Muted = Color.FromArgb(130, 135, 132);
    public static readonly Color Sidebar = Color.FromArgb(249, 251, 248), Surface = Color.FromArgb(245, 247, 245), Border = Color.FromArgb(231, 234, 230);
    public static readonly Color Selection = Color.FromArgb(234, 239, 234), Rail = Color.FromArgb(243, 245, 240);
    private static readonly ToolTip hints = new() { AutomaticDelay = 450 };
    public static readonly Icon? AppIcon = LoadAppIcon();
    private static Icon? LoadAppIcon()
    {
        try
        {
            var stream = typeof(Program).Assembly.GetManifestResourceStream("app.ico");
            if (stream != null) return new Icon(stream);
        }
        catch { }
        try
        {
            string? exe = Environment.ProcessPath ?? Application.ExecutablePath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return Icon.ExtractAssociatedIcon(exe);
        }
        catch { }
        try
        {
            string local = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(local)) return new Icon(local);
        }
        catch { }
        return null;
    }
    public static Button Button(string text, EventHandler? handler = null, bool primary = false)
    {
        var button = new ModernButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(64, 30), Padding = new Padding(10, 4, 10, 4),
            BackColor = primary ? Accent : Surface, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, AccessibleName = text, Margin = new Padding(3), Tag = primary ? "primary" : null, Appearance = primary ? "primary" : "outline" };
        if (handler != null) button.Click += handler;
        return button;
    }
    public static Button Ghost(string text, EventHandler? handler = null)
    {
        var button = (ModernButton)Button(text, handler); button.Appearance = "ghost"; return button;
    }
    public static Button IconButton(string icon, string description, EventHandler? handler = null, bool primary = false)
    {
        var button = (ModernButton)Button("", handler, primary); button.AutoSize = false; button.Size = new Size(32, 32); button.MinimumSize = Size.Empty;
        button.IconKind = icon; button.Appearance = primary ? "circle" : "ghost"; button.AccessibleName = description; Hint(button, description); return button;
    }
    public static void Hint(Control control, string text) { hints.SetToolTip(control, text); control.AccessibleDescription = text; }
    public static Button ChoiceButton(ComboBox source, string description, Func<MultiAgentHub.Core.Choice, string>? caption = null)
    {
        var button = Ghost(""); var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new LightMenuColors()), Font = new Font("Segoe UI", 9), ShowImageMargin = false };
        void Update() { button.Text = (source.SelectedItem is MultiAgentHub.Core.Choice choice ? caption?.Invoke(choice) ?? choice.Name : description) + "  ⌄"; button.Enabled = source.Enabled; }
        source.SelectedIndexChanged += (_, _) => Update(); source.EnabledChanged += (_, _) => Update();
        button.Click += (_, _) => {
            menu.Items.Clear(); foreach (MultiAgentHub.Core.Choice choice in source.Items) {
                var item = new ToolStripMenuItem(choice.Name) { Checked = choice.Id == Value(source) };
                item.Click += (_, _) => source.SelectedItem = choice; menu.Items.Add(item);
            }
            menu.Show(button, new Point(0, button.Height));
        };
        button.Disposed += (_, _) => menu.Dispose(); button.AccessibleName = description; Update(); return button;
    }
    public static Label Label(string text, bool bold = false) => new() { Text = text, AutoSize = true, ForeColor = bold ? Ink : Muted,
        Font = new Font("Segoe UI", bold ? 10 : 8.5f, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(4, 6, 6, 4) };
    public static ComboBox Combo(int width = 200) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(4, 6, 4, 4), FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Ink };
    public static TextBox Text(bool multiline = false) => new() { Dock = DockStyle.Fill, Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
        BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4), BackColor = Surface, ForeColor = Ink, Font = new Font("Segoe UI", 9.5f) };
    public static FlowLayoutPanel Flow() => new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0), Margin = new Padding(0) };
    public static TableLayoutPanel Table(int columns = 1)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, Margin = new Padding(0), Padding = new Padding(0) };
        if (columns == 1) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }
    public static void Choices(ComboBox box, IEnumerable<MultiAgentHub.Core.Choice> values, string? id = null)
    {
        box.Items.Clear(); box.Items.AddRange(values.Cast<object>().ToArray());
        box.SelectedItem = box.Items.Cast<MultiAgentHub.Core.Choice>().FirstOrDefault(c => c.Id == id) ?? box.Items.Cast<object>().FirstOrDefault();
    }
    public static string Value(ComboBox box) => (box.SelectedItem as MultiAgentHub.Core.Choice)?.Id ?? "";
    public static void Theme(Control root)
    {
        foreach (Control control in root.Controls) Theme(control);
        if (root is Button button) { bool primary = Equals(button.Tag, "primary"); button.BackColor = primary ? Accent : Surface; button.ForeColor = primary ? Color.White : Ink; }
        else if (root is Label) { /* Keep status and emphasis colors. */ }
        else { root.BackColor = root is TextBoxBase or ComboBox or NumericUpDown && !Equals(root.Tag, "composer-input") ? Surface : Background; root.ForeColor = Ink; }
        if (root is TreeView tree) { tree.BackColor = Sidebar; tree.ForeColor = Ink; tree.LineColor = Border; }
        if (root is DataGridView grid) {
            grid.BackgroundColor = Background; grid.GridColor = Border;
            grid.DefaultCellStyle.BackColor = Background; grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = Surface; grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Surface; grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.BorderStyle = BorderStyle.None; grid.EnableHeadersVisualStyles = false; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        }
    }
    public static string AgentState(MultiAgentHub.Core.JobRecord job, MultiAgentHub.Core.AgentResult result)
    {
        if (result.State != "queued") return State(result.State);
        var active = job.Results.FirstOrDefault(r => r.State is "running" or "waiting");
        return active == null ? "Chưa đến lượt" : "Đợi " + active.Name;
    }
    public static string State(string value) => value switch {
        "roundtable" => "Bàn tròn", "work" => "Thực hiện", "review" or "reviewing" => "Phản biện", "verifying" => "Kiểm tra", "finished" => "Kết thúc",
        "proposed" => "Đề xuất", "ready" => "Sẵn sàng", "done" => "Đã review", "blocked" => "Cần can thiệp", "not_run" => "Chưa chạy", "passed" => "Lệnh đạt", "stale" => "Hết hiệu lực", "timeout" => "Quá thời gian", "invalid" => "Sai định dạng", "planned" => "Đã lưu kế hoạch", "unverified" => "Chưa kiểm chứng",
        "discussing" => "Đang thảo luận", "resting" => "Nghỉ giữa lượt", "summarizing" => "Đang tổng hợp", "interrupted" => "Phiên bị gián đoạn", "queued" => "Chờ", "running" => "Đang chạy", "waiting" => "Chờ dán kết quả", "success" => "Hoàn thành", "quota" => "Hết quota", "partial" => "Chưa hoàn tất", "error" => "Lỗi", "completed" => "Hoàn tất", "failed" => "Có lỗi", "cancelled" => "Đã dừng", "skipped" => "Bỏ qua", _ => value };
}

internal sealed class ComposerPanel : Panel
{
    public ComposerPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float diameter = 32 * DeviceDpi / 96f; var bounds = new RectangleF(1, 1, Width-3, Height-4);
        if (bounds.Width < diameter || bounds.Height < diameter) return;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90); path.AddArc(bounds.Right-diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right-diameter, bounds.Bottom-diameter, diameter, diameter, 0, 90); path.AddArc(bounds.X, bounds.Bottom-diameter, diameter, diameter, 90, 90); path.CloseFigure();
        using var brush = new SolidBrush(Color.White); using var pen = new Pen(Ui.Border); e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path);
    }
}
