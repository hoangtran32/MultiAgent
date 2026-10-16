using MultiAgentHub.Core;
namespace MultiAgentHubApp;

/// <summary>Always-visible session state above the composer; does not rebuild the message feed.</summary>
internal sealed class SessionProgressView : UserControl
{
    private readonly Label heading = Ui.Label("", true), detail = Ui.Label(""), counts = Ui.Label(""), agents = Ui.Label(""), reason = Ui.Label("");
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 4, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Margin = Padding.Empty };
    private readonly Button board = Ui.Ghost("Công việc"), resume = Ui.Button("Tiếp tục"), inspect = Ui.Ghost("Chi tiết");
    public Button StopButton { get; } = Ui.Button("Dừng");
    public event Action? BoardRequested, ContinueRequested, DetailsRequested, StopRequested;
    public SessionProgressView()
    {
        DoubleBuffered = true; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Fill;
        Margin = Padding.Empty; Padding = new Padding(24, 4, 24, 2); Visible = false;
        var root = Ui.Table(2); root.AutoSize = true; root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.RowCount = 5; for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
        heading.Font = new Font("Segoe UI", 11, FontStyle.Bold); heading.Margin = new Padding(0, 4, 8, 2);
        heading.AutoSize = false; heading.AutoEllipsis = true; heading.Dock = DockStyle.Fill; heading.MinimumSize = new Size(0, 30);
        var actions = Ui.Flow(); actions.Dock = DockStyle.Right; actions.WrapContents = false;
        actions.Controls.AddRange([board, inspect, resume, StopButton]); root.Controls.Add(heading, 0, 0); root.Controls.Add(actions, 1, 0);
        foreach (var label in new[] { detail, counts, agents, reason }) {
            label.AutoSize = false; label.AutoEllipsis = true; label.Dock = DockStyle.Fill; label.Margin = new Padding(0, 1, 0, 1); label.UseMnemonic = false;
        }
        detail.Height = 21; counts.Height = 19; agents.Height = 19; reason.Height = 32;
        foreach (var label in new[] { detail, counts, agents, reason }) label.MinimumSize = new Size(0, label.Height);
        root.Controls.Add(detail, 0, 1); root.SetColumnSpan(detail, 2);
        var countRow = Ui.Table(2); countRow.AutoSize = true; countRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); countRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        countRow.Controls.Add(counts, 0, 0); var track = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 7), Height = 19 }; track.Controls.Add(progress); countRow.Controls.Add(track, 1, 0);
        root.Controls.Add(countRow, 0, 2); root.SetColumnSpan(countRow, 2); root.Controls.Add(agents, 0, 3); root.SetColumnSpan(agents, 2);
        root.Controls.Add(reason, 0, 4); root.SetColumnSpan(reason, 2);
        board.Click += (_, _) => BoardRequested?.Invoke(); resume.Click += (_, _) => ContinueRequested?.Invoke(); inspect.Click += (_, _) => DetailsRequested?.Invoke(); StopButton.Click += (_, _) => StopRequested?.Invoke();
        Ui.Hint(board, "Xem toàn bộ task, phạm vi, kết quả và phần đang chờ review."); Ui.Hint(resume, "Tiếp tục phần còn dang dở với kết quả đã lưu. Nếu đã soạn tin nhắn, bạn tự gửi nội dung đó.");
        Paint += (_, e) => { using var pen = new Pen(Ui.Border); e.Graphics.DrawLine(pen, Padding.Left, 0, Width - Padding.Right, 0); };
    }
    private static void Set(Label label, string value) { if (label.Text == value) return; label.Text = value; Ui.Hint(label, value); }
    public void Clear() => Visible = false;
    public void Update(JobRecord job, bool active)
    {
        var state = RunProgress.From(job, active, DateTimeOffset.Now);
        Set(heading, state.Title); Set(detail, state.Detail); Set(counts, state.Progress + "   ·   " + state.Timing); Set(agents, state.Agents); Set(reason, state.Reason);
        heading.ForeColor = state.Tone switch { "active" => Ui.Accent, "success" => Color.FromArgb(34, 128, 83), "error" => Color.FromArgb(181, 55, 55), _ => Color.FromArgb(148, 99, 29) };
        reason.ForeColor = state.Tone == "error" ? Color.FromArgb(181, 55, 55) : Ui.Muted;
        reason.Visible = state.Reason.Length > 0; board.Visible = job.Team != null; resume.Visible = state.CanContinue;
        string boardText = $"Công việc {state.Done}/{state.Total}"; if (board.Text != boardText) board.Text = boardText;
        StopButton.Visible = StopButton.Enabled = state.IsRunning;
        progress.Visible = state.Total > 0 && job.Mode != "discussion";
        int value = state.Total == 0 ? 0 : (int)(100L * state.Done / state.Total); if (progress.Value != value) progress.Value = value;
        Ui.Hint(progress, "Phần task/agent đã hoàn tất trong danh sách hiện có; không phải ước tính phần trăm toàn bộ project.");
        if (!Visible) Visible = true;
    }
}
