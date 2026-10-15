using MultiAgentHub.Core;
namespace MultiAgentHubApp;

internal sealed class DiscussionDialog : Form
{
    public DiscussionOptions Result { get; private set; }
    public DiscussionDialog(DiscussionOptions options, int agentCount)
    {
        SuspendLayout(); AutoScaleMode = AutoScaleMode.None;
        Result = options.Copy(); Text = "Giới hạn bàn luận"; Font = new Font("Segoe UI", 10); BackColor = Ui.Background;
        Size = new Size(760, 720);
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false;
        if (Ui.AppIcon != null) Icon = Ui.AppIcon;
        var root = Ui.Table(2); root.Padding = new Padding(20);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(root);
        var title = Ui.Label("Cho các model phát triển và phản biện ý tưởng", true); root.Controls.Add(title, 0, 0); root.SetColumnSpan(title, 2);
        NumericUpDown Number(string label, int row, int min, int max, int value) {
            var control = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 150, MinimumSize = new Size(150, 30), Margin = new Padding(4, 8, 4, 8), AccessibleName = label };
            root.Controls.Add(Ui.Label(label), 0, row); root.Controls.Add(control, 1, row); return control;
        }
        var turns = Number("Số lượt thảo luận tối đa", 1, 1, 400, options.MaxTurns);
        var minutes = Number("Thời gian tối đa (phút)", 2, 1, 1440, options.DurationMinutes);
        var pause = Number("Nghỉ giữa lượt (giây)", 3, 0, 3600, options.PauseSeconds);
        var estimate = Ui.Label(""); estimate.AutoSize = false; estimate.Dock = DockStyle.Fill;
        root.Controls.Add(estimate, 0, 4); root.SetColumnSpan(estimate, 2);
        void UpdateEstimate() => estimate.Text = $"{agentCount} agent được chọn • tối đa {(int)turns.Value + agentCount} lần gọi (gồm lượt lỗi và thử tổng hợp).\nDừng khi chạm số lượt hoặc thời gian; dành tối đa 2 phút cuối để tổng hợp.";
        turns.ValueChanged += (_, _) => UpdateEstimate(); UpdateEstimate();
        var note = Ui.Label("Model lần lượt đọc ý kiến trước, đề xuất, phản biện và đưa cách kiểm chứng. Khi chỉ còn một model, nó tiếp tục tự phản biện.\n\nHub Code giữ nguyên quyền từng agent, kể cả toàn quyền, trong mọi lượt. Agent dùng công cụ theo yêu cầu; không tự triển khai ý tưởng chưa được yêu cầu. Hub Chat chỉ trao đổi văn bản. API thuần văn bản không tự đọc thư mục trên máy.\n\nCần CLI/API để tự động; CLI tùy chỉnh tự quản lý quyền. IDE/app dán kết quả không tham gia.\n\nGiữ Hub mở và máy không sleep. Lưu sau từng lượt; mở lại thư viện để xem hoặc tiếp tục, không tự chạy lại.\n\nGiới hạn lượt không phải giới hạn tiền/token; dùng quota hoặc chi phí của tài khoản bạn.");
        note.AutoSize = false; note.Dock = DockStyle.Fill; root.Controls.Add(note, 0, 5); root.SetColumnSpan(note, 2);
        var actions = Ui.Flow(); var save = Ui.Button("Lưu cài đặt", (_, _) => {
            Result = new() { MaxTurns = (int)turns.Value, DurationMinutes = (int)minutes.Value, PauseSeconds = (int)pause.Value };
            Result.Validate(); DialogResult = DialogResult.OK;
        }, true);
        var preset = Ui.Button("Mẫu 8 giờ", (_, _) => { turns.Value = 48; minutes.Value = 480; pause.Value = 600; });
        var cancel = Ui.Button("Hủy", (_, _) => DialogResult = DialogResult.Cancel);
        actions.Controls.AddRange([preset, save, cancel]); root.Controls.Add(actions, 0, 6); root.SetColumnSpan(actions, 2); AcceptButton = save; CancelButton = cancel;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        for (int i = 0; i < 3; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        float scale = DeviceDpi / 96f; root.Scale(new SizeF(scale, scale)); MinimumSize = new Size((int)(650 * scale), (int)(640 * scale));
        Ui.Theme(this); AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        var area = Screen.FromControl(this).WorkingArea; Size = new Size(Math.Min((int)(760 * scale), area.Width - 40), Math.Min((int)(720 * scale), area.Height - 40));
    }
}

internal sealed class DiscussionView
{
    public TabPage Page { get; } = new("Hội thoại & tổng hợp") { BackColor = Color.White, Padding = new Padding(8) };
    private readonly ConversationTranscript report = Box("Bản tổng hợp buổi sáng"), transcript = Box("Toàn bộ cuộc trò chuyện");
    private readonly Label status = Ui.Label("");
    public DiscussionView(Action<Action> protect, float scale)
    {
        var root = Ui.Table(); Page.Controls.Add(root); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = Ui.Flow(); actions.Controls.AddRange([status, Ui.Button("Sao chép tổng hợp", (_, _) => protect(() => { if (report.TextLength > 0) Clipboard.SetText(report.Text); }))]); root.Controls.Add(actions, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        foreach (var item in new[] { ("Tổng hợp buổi sáng", report), ("Cuộc trò chuyện", transcript) }) {
            var page = new TabPage(item.Item1) { BackColor = Color.White, Padding = new Padding(8) }; page.Controls.Add(item.Item2); tabs.TabPages.Add(page);
        }
        root.Controls.Add(tabs, 0, 1); root.Scale(new SizeF(scale, scale));
    }
    private static ConversationTranscript Box(string name) => new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Segoe UI", 10), BackColor = Color.White, BorderStyle = BorderStyle.None, DetectUrls = false, AccessibleName = name };
    private static void Replace(ConversationTranscript box, string text) => box.SetPlainText(text);
    public void Update(JobRecord job)
    {
        status.Text = $"{job.Turns.Count(t => t.Kind == "discussion")}/{job.Discussion?.MaxTurns} lượt • {(job.ReportByModel ? "Đã có tổng hợp từ model" : "Lưu sau từng lượt")}";
        Replace(report, job.Report.Length > 0 ? job.Report : "Bản tổng hợp sẽ xuất hiện khi kết thúc phiên.\n\nChủ đề: " + job.Prompt + "\n\nMở tab Cuộc trò chuyện để theo dõi từng lượt.");
        Replace(transcript, string.Join("\n\n────────────────────────\n\n", job.Turns.Select(t => $"Lượt {t.Number} • {t.Name} • {t.Model}{(t.Kind == "summary" ? " • tổng hợp" : "")}\n{t.Timestamp.LocalDateTime:dd/MM HH:mm:ss} • {Ui.State(t.State)}\n\n{t.Text}{(t.Error.Length > 0 ? "\n\n" + t.Error : "")}")));
    }
}
