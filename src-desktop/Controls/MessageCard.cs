using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MultiAgentHubApp;

internal sealed class MessageBody : ConversationTranscript
{
    public event Action<int>? Wheel;
    public event Action<Keys>? Navigate;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    public void MeasureContent() { if (IsHandleCreated) SendMessage(Handle, 0x0441, IntPtr.Zero, IntPtr.Zero); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); MeasureContent(); }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x020A && (ModifierKeys & Keys.Control) == 0) {
            Wheel?.Invoke((short)(message.WParam.ToInt64() >> 16)); return;
        }
        base.WndProc(ref message);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Shift && (e.KeyCode is Keys.PageUp or Keys.PageDown || e.Control && e.KeyCode is Keys.Home or Keys.End)) {
            Navigate?.Invoke(e.KeyCode); e.SuppressKeyPress = true; return;
        }
        base.OnKeyDown(e);
    }
}

/// <summary>One persistent native card; streaming never replaces earlier messages.</summary>
internal sealed class MessageCard : Panel
{
    private readonly Label title = Ui.Label("", true), meta = Ui.Label("");
    private readonly MessageBody body = new() { BorderStyle = BorderStyle.None, ReadOnly = true, DetectUrls = false,
        WordWrap = true, ScrollBars = RichTextBoxScrollBars.None, Font = new Font("Segoe UI", 9.5f) };
    private readonly Button copy, detail;
    private readonly ContextMenuStrip menu = new() { Renderer = new ToolStripProfessionalRenderer(new LightMenuColors()), ShowImageMargin = false };
    private List<ChatLink> links = [];
    private ChatSection section = new("", "", "");
    private int bodyHeight = 24, measuredWidth;
    private bool placing;
    public string Key => section.Key;
    public bool User => section.User;
    public event Action? HeightRequested, Interacting;
    public event Action<int>? Wheel;
    public event Action<Keys>? Navigate;
    private int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    private Color Fill => User ? Color.FromArgb(232, 243, 255) : Ui.Background;
    private bool HasActions => section.Text.Length + section.Logs.Length + section.Prompt.Length > 0;
    private bool HasBody => section.Text.Length > 0;
    private bool ShowActions => !User && HasActions;
    public MessageCard()
    {
        DoubleBuffered = true; ResizeRedraw = true; Margin = Padding.Empty;
        title.AutoSize = meta.AutoSize = false; title.AutoEllipsis = meta.AutoEllipsis = true;
        title.TextAlign = ContentAlignment.MiddleLeft; meta.TextAlign = ContentAlignment.TopLeft;
        copy = Ui.Button("Sao chép", (_, _) => {
            try { if (section.Text.Length > 0) Clipboard.SetText(section.Text); }
            catch (ExternalException) { MessageBox.Show(FindForm(), "Clipboard đang bận. Thử sao chép lại.", "Multi-Agent Hub"); }
        });
        detail = Ui.Button("Chi tiết", (_, _) => ShowDetails());
        Controls.AddRange([title, meta, body, copy, detail]);
        menu.Items.Add("Sao chép phần đã chọn", null, (_, _) => CopyText(body.SelectedText));
        menu.Items.Add("Sao chép toàn bộ tin nhắn", null, (_, _) => CopyText(section.Text));
        menu.Items.Add("Chi tiết / nội dung đầy đủ", null, (_, _) => ShowDetails());
        menu.Opening += (_, _) => menu.Items[0].Enabled = body.SelectionLength > 0;
        ContextMenuStrip = body.ContextMenuStrip = menu;
        Disposed += (_, _) => menu.Dispose();
        body.ContentsResized += (_, e) => {
            int height = Math.Clamp(e.NewRectangle.Height + D(4), D(24), Math.Min(30000, D(16000)));
            if (height == bodyHeight) return; bodyHeight = height; HeightRequested?.Invoke();
        };
        body.MouseDown += (_, _) => Interacting?.Invoke();
        body.Wheel += delta => Wheel?.Invoke(delta); body.Navigate += key => Navigate?.Invoke(key);
        body.MouseMove += (_, e) => {
            int index = body.GetCharIndexFromPosition(e.Location);
            var cursor = links.Any(l => index >= l.Start && index < l.Start + l.Length) ? Cursors.Hand : Cursors.IBeam;
            if (body.Cursor != cursor) body.Cursor = cursor;
        };
        body.MouseUp += (_, e) => {
            if (e.Button != MouseButtons.Left || body.SelectionLength > 0) return;
            int index = body.GetCharIndexFromPosition(e.Location);
            var link = links.FirstOrDefault(l => index >= l.Start && index < l.Start + l.Length);
            if (link != null && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose(); } catch (Exception) { }
        };
    }
    public bool Apply(ChatSection value)
    {
        bool contentChanged = section.Text != value.Text, styleChanged = section.User != value.User;
        bool actionsChanged = ShowActions != (!value.User && value.Text.Length + value.Logs.Length + value.Prompt.Length > 0);
        section = value;
        if (title.Text != value.Title) title.Text = value.Title;
        if (meta.Text != value.Meta) meta.Text = value.Meta;
        title.Visible = !User; copy.Enabled = value.Text.Length > 0; copy.Visible = detail.Visible = ShowActions;
        body.Visible = value.Text.Length > 0; ApplyTheme();
        if (contentChanged || styleChanged) {
            string shown = value.Text.Length > 24000 ? value.Text[..24000] + (User ? "\n[…nhấp phải → Chi tiết để đọc toàn bộ]" : "\n[…bấm Chi tiết để đọc toàn bộ]") : value.Text;
            var markdown = new NativeMarkdown(0);
            body.SetDocument(markdown.Render([value with { Text = shown, User = false }], includeHeader: false), false);
            links = markdown.Links; body.MeasureContent();
        }
        return contentChanged || styleChanged || actionsChanged;
    }
    public void ApplyTheme()
    {
        BackColor = Ui.Background; title.BackColor = meta.BackColor = body.BackColor = Fill;
        if (body.ForeColor != Ui.Ink) body.ForeColor = Ui.Ink;
    }
    public int Measure(int width)
    {
        if (Width != width) Width = width;
        PlaceContent();
        return (User ? D(14 + 16 + 8 + 14) : D(18 + 22 + 32 + 14 + 16)) + (HasBody ? bodyHeight : 0) + (ShowActions ? D(46) : 0);
    }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); PlaceContent(); }
    private void PlaceContent()
    {
        if (placing || copy == null) return; placing = true;
        try {
            int padding = D(User ? 14 : 18), width = Math.Max(D(60), ClientSize.Width - padding * 2), y = padding;
            if (!User) { title.SetBounds(padding, y, width, D(22)); y += D(24); }
            meta.SetBounds(padding, y, width, D(User ? 16 : 32)); y += D(User ? 24 : 42);
            body.SetBounds(padding, y, width, bodyHeight);
            if (measuredWidth != width) { measuredWidth = width; body.MeasureContent(); }
            y += HasBody ? bodyHeight : 0;
            y += D(14);
            if (ShowActions) {
                int x = padding;
                foreach (var button in new[] { copy, detail }) {
                    Size preferred = button.GetPreferredSize(Size.Empty);
                    button.SetBounds(x, y, preferred.Width, Math.Max(D(30), preferred.Height)); x += preferred.Width + D(8);
                }
            }
        } finally { placing = false; }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (ClientSize.Width < 4 || ClientSize.Height < 4) return;
        using var path = Shapes.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), D(14));
        using var fill = new SolidBrush(Fill); using var border = new Pen(User ? Color.FromArgb(216, 232, 249) : Ui.Border);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        int left = D(18), right = Width - D(18);
        using var divider = new Pen(User ? Color.FromArgb(216, 232, 249) : Ui.Border);
        if (!User) e.Graphics.DrawLine(divider, left, meta.Bottom + D(5), right, meta.Bottom + D(5));
        if (ShowActions) e.Graphics.DrawLine(divider, left, copy.Top - D(7), right, copy.Top - D(7));
    }
    private void CopyText(string value)
    {
        try { if (value.Length > 0) Clipboard.SetText(value); }
        catch (ExternalException) { MessageBox.Show(FindForm(), "Clipboard đang bận. Thử sao chép lại.", "Multi-Agent Hub"); }
    }
    private void ShowDetails()
    {
        using var dialog = new Form { Text = section.Title + " • Chi tiết", StartPosition = FormStartPosition.CenterParent,
            Font = Font, BackColor = Ui.Background, ShowInTaskbar = false, Icon = Ui.AppIcon, AutoScaleMode = AutoScaleMode.None };
        var area = Screen.FromControl(this).WorkingArea;
        dialog.Size = new Size(Math.Min(D(900), area.Width - 40), Math.Min(D(680), area.Height - 40));
        var tabs = new TabControl { Dock = DockStyle.Fill }; dialog.Controls.Add(tabs);
        foreach (var item in new[] { ("Nội dung đầy đủ", section.Text), ("Nhật ký agent trong phiên", section.Logs), ("Prompt gần nhất của agent", section.Prompt) }) {
            if (item.Item2.Length == 0 && tabs.TabPages.Count > 0) continue;
            var page = new TabPage(item.Item1) { Padding = new Padding(12), BackColor = Ui.Background };
            page.Controls.Add(new ConversationTranscript { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
                BackColor = Ui.Background, Font = new Font("Consolas", 10), DetectUrls = false, Text = item.Item2, ScrollBars = RichTextBoxScrollBars.Vertical });
            tabs.TabPages.Add(page);
        }
        dialog.ShowDialog(FindForm());
    }
}
