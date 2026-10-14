using System.Drawing.Drawing2D;

namespace MultiAgentHubApp;

internal sealed class ModernButton : Button
{
    [System.ComponentModel.DefaultValue("ghost")]
    public string Appearance { get; set; } = "ghost";
    [System.ComponentModel.DefaultValue("")]
    public string IconKind { get; set; } = "";
    private bool hovered, pressed;
    public ModernButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; DoubleBuffered = true; UseVisualStyleBackColor = false; }
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!AutoSize) return Size;
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        Size size;
        if (IsHandleCreated) {
            using var graphics = CreateGraphics();
            size = TextRenderer.MeasureText(graphics, Text, Font, new Size(int.MaxValue, int.MaxValue), flags);
        } else size = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), flags);
        int safety = (int)Math.Ceiling(4 * DeviceDpi / 96f);
        return new Size(Math.Max(MinimumSize.Width, Math.Min(MaximumSize.Width > 0 ? MaximumSize.Width : int.MaxValue, size.Width + Padding.Horizontal + safety + (IconKind.Length > 0 && Text.Length > 0 ? (int)(25 * DeviceDpi / 96f) : 0))), Math.Max(MinimumSize.Height, size.Height + Padding.Vertical + safety));
    }
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        // Nested layout/scaling passes must not enlarge an icon beyond the logical 32px slot.
        if (!AutoSize && Dock == DockStyle.None && string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(IconKind))
            width = height = (int)Math.Round(32 * DeviceDpi / 96f);
        base.SetBoundsCore(x, y, width, height, specified);
    }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        if (!AutoSize && Dock == DockStyle.None && string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(IconKind)) Size = new Size((int)Math.Round(32 * DeviceDpi / 96f), (int)Math.Round(32 * DeviceDpi / 96f));
        Parent?.PerformLayout(this, nameof(PreferredSize));
    }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Parent?.PerformLayout(this, nameof(PreferredSize)); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Ui.Background);
        var bounds = new RectangleF(1, 1, Width - 2, Height - 2); if (bounds.Width <= 0 || bounds.Height <= 0) return;
        bool primary = Appearance is "primary" or "circle";
        Color fill = primary ? Enabled ? pressed ? Color.FromArgb(35, 96, 200) : Ui.Accent : Color.FromArgb(193, 209, 233) : hovered || pressed ? Ui.Selection : Appearance == "outline" ? Color.White : Parent?.BackColor ?? Ui.Background;
        float radius = Appearance == "circle" ? Math.Min(bounds.Width, bounds.Height) / 2 : 8 * DeviceDpi / 96f;
        using var path = Shapes.Rounded(bounds, radius); using var brush = new SolidBrush(fill); g.FillPath(brush, path);
        if (Appearance == "outline") { using var border = new Pen(Ui.Border); g.DrawPath(border, path); }
        if (Focused && ShowFocusCues) { using var focus = new Pen(Color.FromArgb(140, 180, 238)); g.DrawPath(focus, path); }
        Color ink = Enabled ? primary ? Color.White : ForeColor : Ui.Muted;
        var textBounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(0, ClientSize.Width - Padding.Horizontal), Math.Max(0, ClientSize.Height - Padding.Vertical));
        if (IconKind.Length > 0) {
            float size = 17 * DeviceDpi / 96f;
            Shapes.Icon(g, IconKind, new RectangleF(Text.Length > 0 ? Padding.Left : (Width - size) / 2, (Height - size) / 2, size, size), ink);
            if (Text.Length > 0) { textBounds.X += (int)(25 * DeviceDpi / 96f); textBounds.Width -= (int)(25 * DeviceDpi / 96f); }
        }
        if (Text.Length > 0) {
            var alignment = TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft ? TextFormatFlags.Left : TextAlign is ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(g, Text, Font, textBounds, ink, alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }
}

internal static class Shapes
{
    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)); var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    public static void Icon(Graphics g, string kind, RectangleF b, Color color)
    {
        var state = g.Save(); g.TranslateTransform(b.X, b.Y); g.ScaleTransform(b.Width / 24, b.Height / 24);
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(color);
        switch (kind) {
            case "plus": g.DrawLine(pen, 12, 4, 12, 20); g.DrawLine(pen, 4, 12, 20, 12); break;
            case "up": g.DrawLine(pen, 12, 19, 12, 5); g.DrawLines(pen, [new(6, 11), new(12, 5), new(18, 11)]); break;
            case "down": g.DrawLine(pen, 12, 5, 12, 19); g.DrawLines(pen, [new(6, 13), new(12, 19), new(18, 13)]); break;
            case "stop": g.FillRectangle(fill, 6, 6, 12, 12); break;
            case "close": g.DrawLine(pen, 6, 6, 18, 18); g.DrawLine(pen, 18, 6, 6, 18); break;
            case "search": g.DrawEllipse(pen, 4, 3, 12, 12); g.DrawLine(pen, 14, 14, 20, 20); break;
            case "folder": g.DrawLines(pen, [new(3, 7), new(3, 5), new(9, 5), new(12, 8), new(21, 8), new(21, 19), new(3, 19), new(3, 7), new(21, 7)]); break;
            case "edit": g.DrawLines(pen, [new(17, 3), new(21, 7), new(10, 18), new(6, 19), new(7, 15), new(17, 3)]); g.DrawLines(pen, [new(12, 5), new(4, 5), new(4, 21), new(19, 21), new(19, 13)]); break;
            case "home": g.DrawLines(pen, [new(3, 11), new(12, 3), new(21, 11), new(19, 11), new(19, 21), new(14, 21), new(14, 14), new(10, 14), new(10, 21), new(5, 21), new(5, 11)]); break;
            case "clock": g.DrawEllipse(pen, 3, 3, 18, 18); g.DrawLines(pen, [new(12, 6), new(12, 12), new(16, 14)]); break;
            case "tasks": for (int i = 0; i < 3; i++) { g.DrawEllipse(pen, 3, 4 + i * 7, 3, 3); g.DrawLine(pen, 10, 5.5f + i * 7, 21, 5.5f + i * 7); } break;
            case "settings": g.DrawEllipse(pen, 5, 5, 14, 14); g.DrawEllipse(pen, 9, 9, 6, 6); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, 12 + 7 * (float)Math.Cos(a), 12 + 7 * (float)Math.Sin(a), 12 + 10 * (float)Math.Cos(a), 12 + 10 * (float)Math.Sin(a)); } break;
            case "panel": g.DrawRectangle(pen, 3, 4, 18, 16); g.DrawLine(pen, 10, 4, 10, 20); break;
            case "more": foreach (int x in new[] { 5, 12, 19 }) g.FillEllipse(fill, x - 1, 11, 2, 2); break;
            case "chat": g.DrawLines(pen, [new(3, 4), new(21, 4), new(21, 17), new(9, 17), new(4, 21), new(4, 17), new(3, 17), new(3, 4)]); break;
        }
        g.Restore(state);
    }
}

internal sealed class LightMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
    public override Color MenuItemSelected => Ui.Selection;
    public override Color MenuItemBorder => Ui.Selection;
    public override Color MenuBorder => Ui.Border;
}
