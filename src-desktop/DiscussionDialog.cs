using MultiAgentHub.Core;
namespace MultiAgentHubApp;

internal sealed class DiscussionDialog : Form
{
    public DiscussionOptions Result { get; private set; }
    public DiscussionDialog(DiscussionOptions options, int agentCount)
    {
        SuspendLayout(); AutoScaleMode = AutoScaleMode.None;
        Result = options.Copy(); Text = "Bàn luận chủ đề & nghiên cứu"; Font = new Font("Segoe UI", 10); BackColor = Ui.Background;
        Size = new Size(800, 780);
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false;
        if (Ui.AppIcon != null) Icon = Ui.AppIcon;
        var root = Ui.Table(2); root.Padding = new Padding(20);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(root);
        var title = Ui.Label("Cho các model phát triển và phản biện ý tưởng", true); root.Controls.Add(title, 0, 0); root.SetColumnSpan(title, 2);
        NumericUpDown Number(string label, int row, int min, int max, int value) {
            var control = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 150, MinimumSize = new Size(150, 30), Margin = new Padding(4, 8, 4, 8), AccessibleName = label };
            root.Controls.Add(Ui.Label(label), 0, row); root.Controls.Add(control, 1, row); return control;
        }
        var turns = Number("Số lượt tối đa (0 = không giới hạn)", 1, 0, 100000, options.MaxTurns);
        var minutes = Number("Thời gian phiên (phút, 0 = vô hạn)", 2, 0, 10080, options.DurationMinutes);
        var timeout = Number("Mỗi lượt (phút, 0 = không giới hạn)", 3, 0, 10080, options.TurnTimeoutMinutes);
        var pause = Number("Nghỉ tùy chọn (giây, 0 = liên tục)", 4, 0, 3600, options.PauseSeconds);
        Ui.Hint(turns, "Tính cả lượt trả lời thành công và lượt lỗi. 0: tiếp tục trao đổi cho đến khi bạn Kết thúc & tổng hợp, Dừng hoặc không còn model khả dụng.");
        Ui.Hint(minutes, "0: Hub không đặt thời điểm kết thúc phiên. Khi có giới hạn, Hub dành phần cuối để tổng hợp.");
        Ui.Hint(timeout, "0: Hub không ngắt một model vì thời gian xử lý. Khi đặt số phút, giới hạn này áp dụng cho cả lượt trao đổi và tổng hợp.");
        Ui.Hint(pause, "Mặc định 0: model trả lời xong là chuyển ngay sang model kế tiếp, không cần bạn nhắn thêm. Chỉ tăng khi bạn chủ động muốn giãn các lượt.");
        var estimate = Ui.Label(""); estimate.AutoSize = false; estimate.Dock = DockStyle.Fill;
        root.Controls.Add(estimate, 0, 5); root.SetColumnSpan(estimate, 2);
        void UpdateEstimate() => estimate.Text = $"{agentCount} model được chọn • " + (turns.Value == 0 ? "không giới hạn số lượt trao đổi." : $"tối đa {(int)turns.Value + agentCount} lần gọi (gồm lượt lỗi và thử tổng hợp).") + "\n" +
            (pause.Value == 0 ? "Chạy liên tục: tự chuyển model/lượt kế tiếp, không cần tin nhắn bổ sung." : $"Bạn chọn nghỉ {(int)pause.Value}s sau mỗi lượt; hết nghỉ sẽ tự chạy tiếp.") +
            "\n⋯ → Kết thúc & tổng hợp để kết thúc sớm.";
        turns.ValueChanged += (_, _) => UpdateEstimate(); pause.ValueChanged += (_, _) => UpdateEstimate(); UpdateEstimate();
        var note = Ui.Label("Model lần lượt đọc ý kiến trước, đề xuất, phản biện và đưa cách kiểm chứng. Khi chỉ còn một model, nó tiếp tục tự phản biện.\n\nHub Chat nghiên cứu bằng văn bản; chatbot web dùng các khả năng có trong website, không có quyền file/terminal trên máy. Hub Code giữ nguyên quyền CLI của từng agent theo yêu cầu.\n\nChatbot từ trình duyệt dùng tài khoản bạn đăng nhập qua extension. CLI/API cũng có thể bàn luận; kết nối chỉ sao chép/dán không tự động.\n\nGiữ Hub và các tab đã kết nối mở, máy không sleep. Lưu sau từng lượt; mở lại thư viện để xem hoặc tiếp tục, không tự chạy lại.\n\nGiới hạn lượt của Hub độc lập với quota tài khoản. Model trên website do bạn chọn và xác nhận; Hub không tự đổi model bằng nhãn.");
        note.AutoSize = false; note.Dock = DockStyle.Fill; root.Controls.Add(note, 0, 6); root.SetColumnSpan(note, 2);
        var actions = Ui.Flow(); var save = Ui.Button("Lưu cài đặt", (_, _) => {
            Result = new() { MaxTurns = (int)turns.Value, DurationMinutes = (int)minutes.Value, TurnTimeoutMinutes = (int)timeout.Value, PauseSeconds = (int)pause.Value };
            Result.Validate(); DialogResult = DialogResult.OK;
        }, true);
        var continuous = Ui.Button("Trao đổi liên tục", (_, _) => { turns.Value = minutes.Value = timeout.Value = pause.Value = 0; });
        var preset = Ui.Button("Mẫu 8 giờ", (_, _) => { turns.Value = 48; minutes.Value = 480; timeout.Value = pause.Value = 0; });
        var cancel = Ui.Button("Hủy", (_, _) => DialogResult = DialogResult.Cancel);
        actions.Controls.AddRange([continuous, preset, save, cancel]); root.Controls.Add(actions, 0, 7); root.SetColumnSpan(actions, 2); AcceptButton = save; CancelButton = cancel;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        float scale = DeviceDpi / 96f; root.Scale(new SizeF(scale, scale)); MinimumSize = new Size((int)(740 * scale), (int)(700 * scale));
        Ui.Theme(this); AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        var area = Screen.FromControl(this).WorkingArea; Size = new Size(Math.Min((int)(800 * scale), area.Width - 40), Math.Min((int)(780 * scale), area.Height - 40));
    }
}

internal sealed class DiscussionView
{
    public TabPage Page { get; } = new("Hội thoại & tổng hợp") { BackColor = Color.White, Padding = new Padding(8) };
    private readonly ConversationTranscript report = Box("Bản tổng hợp chủ đề"), transcript = Box("Toàn bộ cuộc trò chuyện");
    private readonly Label status = Ui.Label("");
    public DiscussionView(Action<Action> protect, float scale)
    {
        var root = Ui.Table(); Page.Controls.Add(root); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = Ui.Flow(); actions.Controls.AddRange([status, Ui.Button("Sao chép tổng hợp", (_, _) => protect(() => { if (report.TextLength > 0) Clipboard.SetText(report.Text); }))]); root.Controls.Add(actions, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        foreach (var item in new[] { ("Tổng hợp chủ đề", report), ("Cuộc trò chuyện", transcript) }) {
            var page = new TabPage(item.Item1) { BackColor = Color.White, Padding = new Padding(8) }; page.Controls.Add(item.Item2); tabs.TabPages.Add(page);
        }
        root.Controls.Add(tabs, 0, 1); root.Scale(new SizeF(scale, scale));
    }
    private static ConversationTranscript Box(string name) => new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Segoe UI", 10), BackColor = Color.White, BorderStyle = BorderStyle.None, DetectUrls = false, AccessibleName = name };
    private static void Replace(ConversationTranscript box, string text) => box.SetPlainText(text);
    public void Update(JobRecord job)
    {
        int turns = job.Turns.Count(t => t.Kind == "discussion");
        status.Text = (job.Discussion?.TurnUsage(turns) ?? $"{turns} lượt") + $" • {(job.ReportByModel ? "Đã có tổng hợp từ model" : "Lưu sau từng lượt")}";
        Replace(report, job.Report.Length > 0 ? job.Report : "Bản tổng hợp sẽ xuất hiện khi kết thúc phiên.\n\nChủ đề: " + job.Prompt + "\n\nMở tab Cuộc trò chuyện để theo dõi từng lượt.");
        Replace(transcript, string.Join("\n\n────────────────────────\n\n", job.Turns.Select(t => t.Kind == "user" ?
            $"Bạn bổ sung • {t.Timestamp.LocalDateTime:dd/MM HH:mm:ss} • {(t.State == "queued" ? "Chờ lượt kế tiếp" : "Đã gửi vào lượt " + t.Number)}\n\n{t.Text}" :
            $"Lượt {t.Number} • {t.Name} • {t.Model}{(t.Kind == "summary" ? " • tổng hợp" : "")}\n{t.Timestamp.LocalDateTime:dd/MM HH:mm:ss} • {Ui.State(t.State)}\n\n{t.Text}{(t.Error.Length > 0 ? "\n\n" + t.Error : "")}")));
    }
}
