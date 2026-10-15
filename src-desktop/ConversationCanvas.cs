using MultiAgentHub.Core;
namespace MultiAgentHubApp;

/// <summary>Persistent native message cards with independent bodies and actions.</summary>
internal sealed class ConversationCanvas : UserControl
{
    private MessageFeed feed = new();
    private readonly Dictionary<string, MessageFeed> feeds = [];
    private readonly LinkedList<string> recentFeeds = new();
    private readonly TableLayoutPanel welcome = Ui.Table();
    private readonly Label title = Ui.Label("Bạn muốn làm gì hôm nay?", true), description = Ui.Label("");
    private readonly Button setup = Ui.Button("+ Thêm agent của bạn", primary: true);
    private List<ChatSection> sections = [];
    private bool hasAgents = true;
    private string welcomeSurface = "work";
    public event Action<string>? SuggestionSelected;
    public event Action? SetupRequested;
    public ConversationCanvas()
    {
        DoubleBuffered = true; Dock = DockStyle.Fill; Padding = new Padding(0, 8, 0, 8);
        Controls.Add(feed); Controls.Add(welcome); welcome.RowCount = 5;
        welcome.RowStyles.Add(new RowStyle(SizeType.Percent, 35)); welcome.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        welcome.RowStyles.Add(new RowStyle(SizeType.AutoSize)); welcome.RowStyles.Add(new RowStyle(SizeType.AutoSize)); welcome.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        title.Font = new Font("Segoe UI", 20, FontStyle.Regular); title.Anchor = AnchorStyles.None; welcome.Controls.Add(title, 0, 1);
        description.MaximumSize = new Size(620, 0); description.TextAlign = ContentAlignment.MiddleCenter; description.Anchor = AnchorStyles.None; welcome.Controls.Add(description, 0, 2);
        var suggestions = Ui.Flow(); suggestions.Dock = DockStyle.None; suggestions.Anchor = AnchorStyles.None;
        foreach (string text in new[] { "Cùng xây dựng một tính năng", "Phản biện một ý tưởng", "Giải thích và khám phá" })
            suggestions.Controls.Add(Ui.Button(text, (_, _) => SuggestionSelected?.Invoke(text)));
        setup.Click += (_, _) => SetupRequested?.Invoke(); setup.Visible = false;
        suggestions.Controls.Add(setup); welcome.Controls.Add(suggestions, 0, 3);
        ShowWelcome("work");
    }
    public void ShowWelcome(string surface)
    {
        welcomeSurface = surface;
        title.Text = surface == "chat" ? "Cùng nghĩ xa hơn." : "Cùng tạo nên điều tiếp theo.";
        SetAgentAvailability(hasAgents); sections.Clear(); welcome.Visible = true; feed.Visible = false; welcome.BringToFront();
    }
    public void SetAgentAvailability(bool configured)
    {
        hasAgents = configured; setup.Visible = !configured;
        description.Text = !configured ? "Thêm CLI, API hoặc ứng dụng AI bạn đang dùng.\nMột agent chạy độc lập; thêm đồng đội để cùng làm việc và phản biện." : welcomeSurface == "chat" ?
            "Một cuộc trò chuyện. Nhiều góc nhìn.\nChọn model để hỏi đáp hoặc cho các agent bàn luận với nhau." :
            "Chọn project và đội agent của bạn.\nCùng triển khai, rà soát và hoàn thiện công việc trong một cuộc hội thoại.";
    }
    public void ApplyTheme() { feed.ApplyTheme(); foreach (var cached in feeds.Values) cached.ApplyTheme(); }
    public void ScrollToEnd() => feed.ScrollToEnd();
    public void CopyAll()
    {
        if (sections.Count > 0) Clipboard.SetText(string.Join("\n\n", sections.Select(s => s.Title + "\n" + s.Meta + "\n\n" + s.Text)));
    }
    public void Update(IEnumerable<JobRecord> jobs)
    {
        var records = jobs.ToList();
        string topicKey = records.FirstOrDefault() is { } first ? first.ConversationId.Length > 0 ? first.ConversationId : first.Id : "";
        if (!feeds.TryGetValue(topicKey, out var nextFeed)) {
            nextFeed = feeds.Count == 0 ? feed : new MessageFeed { Bounds = DisplayRectangle }; nextFeed.Visible = false;
            if (nextFeed.Parent == null) Controls.Add(nextFeed); feeds[topicKey] = nextFeed;
        }
        if (feed != nextFeed) feed.Visible = false;
        feed = nextFeed; recentFeeds.Remove(topicKey); recentFeeds.AddLast(topicKey);
        while (recentFeeds.Count > 3) { string oldest = recentFeeds.First!.Value; recentFeeds.RemoveFirst(); var unused = feeds[oldest]; feeds.Remove(oldest); Controls.Remove(unused); unused.Dispose(); }
        var next = new List<ChatSection>();
        foreach (var job in records) {
            string Key(string part) => job.Id + ":" + part;
            next.Add(new("Bạn", job.Timestamp.LocalDateTime.ToString("dd/MM HH:mm"), job.Prompt, true, Key("user")));
            if (job.Mode is "discussion" or "collaboration" or "team") {
                foreach (var turn in job.Turns.Where(t => t.Kind != "summary")) {
                    var result = job.Results.FirstOrDefault(r => r.AgentId == turn.AgentId);
                    next.Add(new(turn.Name, $"{turn.Model} · {turn.Role}{(turn.TaskId.Length > 0 ? " / " + turn.TaskId : "")} · Lượt {turn.Number} · {Ui.State(turn.State)}",
                        turn.Text + (turn.Error.Length > 0 ? "\n" + turn.Error : ""), Key: Key("turn:" + turn.Number), Logs: result?.Logs ?? "", Prompt: result?.SentPrompt ?? ""));
                }
                foreach (var queued in job.Results.Where(r => r.State == "queued")) next.Add(new(queued.Name, Ui.AgentState(job, queued), "", Key: Key("queued:" + queued.AgentId)));
                if (job.Mode is "discussion" or "team" && job.Report.Length > 0) next.Add(new("Tổng hợp", job.ReportByModel ? "Từ model" : "Bản ghi tự động", job.Report, Key: Key("report")));
                if (job.Team is { } team && team.Directives.Count + team.PendingDirectives.Count > 0)
                    next.Add(new("Bạn bổ sung", "Áp dụng tại ranh giới bước", string.Join("\n\n", team.Directives.Concat(team.PendingDirectives.Select(d => "[Đang chờ áp dụng] " + d))), true, Key("directives")));
            } else {
                for (int index = 0; index < job.Results.Count; index++) {
                    var result = job.Results[index];
                    next.Add(new(result.Name, $"{result.Model} · {Ui.AgentState(job, result)}", result.Text + (result.Error.Length > 0 ? "\n\n" + result.Error : ""), Key: Key("result:" + index), Logs: result.Logs, Prompt: result.SentPrompt));
                }
            }
        }
        sections = next; feed.Apply(sections); welcome.Visible = false; feed.Visible = true; feed.BringToFront();
    }
}
