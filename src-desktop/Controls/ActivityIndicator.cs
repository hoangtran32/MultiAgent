using System.Drawing.Drawing2D;
namespace MultiAgentHubApp;

/// <summary>Paint-only typing dots: animation never changes layout or conversation scroll.</summary>
internal sealed class ActivityIndicator : Control
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 120 };
    private int frame;
    public ActivityIndicator()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Font = new Font("Segoe UI", 9.5f); ForeColor = Ui.Muted; BackColor = Ui.Background; Visible = false;
        animation.Tick += (_, _) => { frame = (frame + 1) % 18; Invalidate(); };
    }
    public bool SetActivity(string value)
    {
        bool sizeChanged = (Text.Length == 0) != (value.Length == 0);
        if (Text != value) { Text = value; AccessibleName = value; Invalidate(); }
        Visible = value.Length > 0; UpdateTimer(); return sizeChanged;
    }
    private void UpdateTimer() { if (Visible && Text.Length > 0 && IsHandleCreated) animation.Start(); else animation.Stop(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateTimer(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateTimer(); }
    protected override void OnHandleDestroyed(EventArgs e) { animation.Stop(); base.OnHandleDestroyed(e); }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f, diameter = 5 * scale, middle = Height / 2f;
        for (int i = 0; i < 3; i++) {
            float wave = (float)((Math.Sin((frame / 18d - i / 6d) * Math.PI * 2) + 1) / 2);
            using var brush = new SolidBrush(Color.FromArgb((int)(75 + wave * 175), Ui.Accent));
            e.Graphics.FillEllipse(brush, i * 10 * scale, middle - diameter / 2 - wave * 2 * scale, diameter, diameter);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(38 * scale), 0, Math.Max(0, Width - (int)(38 * scale)), Height), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
