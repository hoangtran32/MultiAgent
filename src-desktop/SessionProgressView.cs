using MultiAgentHub.Core;
namespace MultiAgentHubApp;

/// <summary>A compact sidebar summary with deterministic heights.</summary>
internal sealed class SessionProgressView : UserControl
{
    private readonly Label heading = Ui.Label("", true), detail = Ui.Label(""), counts = Ui.Label(""), agents = Ui.Label(""), timing = Ui.Label(""), reason = Ui.Label("");
    private readonly Button board = Ui.Ghost("Công việc"), resume = Ui.Button("Tiếp tục"), inspect = Ui.Ghost("Chi tiết");
    public Button StopButton { get; } = Ui.Button("Dừng phiên");
    public event Action? BoardRequested, ContinueRequested, DetailsRequested, StopRequested;
    private int agentLines = 1;
    private bool showReason, showAction, hasTasks;
    private int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    public SessionProgressView()
    {
        DoubleBuffered = true; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Fill;
        Margin = new Padding(0, 6, 0, 4); Visible = false; BackColor = Ui.Sidebar;
        heading.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        foreach (var label in new[] { heading, detail, counts, agents, timing, reason }) {
            label.AutoSize = false; label.AutoEllipsis = true; label.UseMnemonic = false; label.Margin = Padding.Empty; Controls.Add(label);
        }
        foreach (var button in new[] { board, inspect, resume, StopButton }) {
            button.AutoSize = false; button.MinimumSize = Size.Empty; button.Padding = new Padding(6, 2, 6, 2); button.Margin = Padding.Empty;
            button.Font = new Font("Segoe UI", 8.5f); Controls.Add(button);
        }
        board.Click += (_, _) => BoardRequested?.Invoke(); resume.Click += (_, _) => ContinueRequested?.Invoke(); inspect.Click += (_, _) => DetailsRequested?.Invoke(); StopButton.Click += (_, _) => StopRequested?.Invoke();
        Ui.Hint(board, "Xem task, kết quả và phần đang chờ review."); Ui.Hint(resume, "Tiếp tục từ kết quả đã lưu; giữ bản nháp đang gõ để bạn tự gửi.");
    }
    public override Size GetPreferredSize(Size proposedSize) => new(0, D(22 + 38 + 20 + agentLines * 18 + 36 + 32 + 16 + (showReason ? 38 : 0) + (showAction ? 32 : 0)));
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (heading == null || StopButton == null) return;
        int x = D(8), width = Math.Max(D(40), ClientSize.Width - D(16)), y = D(8);
        void Place(Control control, int height) { control.SetBounds(x, y, width, D(height)); y += D(height); }
        Place(heading, 22); Place(detail, 38); Place(counts, 20); Place(agents, agentLines * 18); Place(timing, 36);
        if (showReason) Place(reason, 38);
        int half = (width - D(6)) / 2;
        board.SetBounds(x, y, hasTasks ? half : 0, D(28)); inspect.SetBounds(hasTasks ? x + half + D(6) : x, y, hasTasks ? width - half - D(6) : width, D(28)); y += D(32);
        StopButton.SetBounds(x, y, width, D(28)); resume.SetBounds(x, y, width, D(28));
    }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); PerformLayout(); Parent?.PerformLayout(this, nameof(PreferredSize)); }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var pen = new Pen(Ui.Border); e.Graphics.DrawLine(pen, D(8), 1, Width - D(8), 1); }
    private static void Set(Label label, string value, string? hint = null) { if (label.Text == value) return; label.Text = value; Ui.Hint(label, hint ?? value); }
    public void Clear() => Visible = false;
    public void Update(JobRecord job, bool active)
    {
        var state = RunProgress.From(job, active, DateTimeOffset.Now);
        int heightBefore = GetPreferredSize(Size.Empty).Height;
        var names = state.Agents.Split("   ·   ", StringSplitOptions.RemoveEmptyEntries);
        agentLines = Math.Clamp(names.Length, 1, 3); showReason = state.Reason.Length > 0; showAction = state.IsRunning || state.CanContinue; hasTasks = job.Team != null;
        string agentText = string.Join("\n", names.Take(names.Length > 3 ? 2 : 3)); if (names.Length > 3) agentText += $"\n+ {names.Length - 2} agent khác · xem Chi tiết";
        Set(heading, state.Title); Set(detail, state.Detail); Set(counts, state.Progress); Set(agents, agentText, state.Agents); Set(timing, state.Timing.Replace(" · ", "\n")); Set(reason, state.Reason);
        heading.ForeColor = state.Tone switch { "active" => Ui.Accent, "success" => Color.FromArgb(34, 128, 83), "error" => Color.FromArgb(181, 55, 55), _ => Color.FromArgb(148, 99, 29) };
        reason.ForeColor = state.Tone == "error" ? Color.FromArgb(181, 55, 55) : Ui.Muted;
        reason.Visible = showReason; board.Visible = hasTasks; resume.Visible = state.CanContinue; StopButton.Visible = StopButton.Enabled = state.IsRunning;
        if (!Visible) Visible = true;
        if (heightBefore != GetPreferredSize(Size.Empty).Height) { PerformLayout(); Parent?.PerformLayout(this, nameof(PreferredSize)); }
    }
}
