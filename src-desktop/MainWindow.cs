using System.Diagnostics;
using System.Text.Json;
using MultiAgentHub.Core;
namespace MultiAgentHubApp;
internal sealed partial class MainWindow : Form
{
    private readonly Storage storage;
    private readonly ISecretStore secrets = new CredentialSecrets();
    private HubConfig config;
    private readonly JobRunner runner;
    private readonly TextBox project = Ui.Text(), prompt = Ui.Text(true);
    private readonly TextBox topic = new() { Width = 245, MaxLength = 100, PlaceholderText = "Tên topic (để trống: lấy từ yêu cầu)", AccessibleName = "Tên topic", Margin = new Padding(4, 7, 4, 4) };
    private readonly ConversationSidebar sidebar = new();
    private ConversationView? conversationView;
    private string? selectedConversationId;
    private JobRecord? continueFrom;
    private List<JobRecord> conversationJobs = [];
    private readonly ComboBox addProvider = Ui.Combo(185), addConnection = Ui.Combo(290), mode = Ui.Combo(220), history = Ui.Combo(460);
    private readonly ComboBox recentProjects = Ui.Combo(270);
    private readonly NumericUpDown count = new() { Minimum = 1, Maximum = Catalog.MaxAgents, Value = 1, Width = 64, Margin = new Padding(4, 5, 4, 4) };
    private readonly DataGridView grid = new();
    private readonly Label info = Ui.Label(""), summary = Ui.Label(""), attachment = Ui.Label("Không đính kèm file"), status = Ui.Label("Sẵn sàng");
    private readonly TabControl results = new() { Dock = DockStyle.Fill, Padding = new Point(14, 7) };
    private readonly List<Control> locked = [];
    private readonly Dictionary<string, ResultView> views = [];
    private DiscussionView? discussionView;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> pendingQuota = new();
    private string quotaSaveError = "";
    private DateTimeOffset retryQuotaSave;
    private readonly Button run, stop;
    private readonly Button addButton;
    private readonly Button discussionSettings;
    private readonly Panel agentPanel = new() { Dock = DockStyle.Fill }, detailPanel = new() { Dock = DockStyle.Fill };
    private readonly ConversationCanvas canvas = new();
    private readonly SessionProgressView sessionProgress = new();
    private readonly Panel conversationHost = new() { Dock = DockStyle.Fill }, boardPanel = new() { Dock = DockStyle.Right, Width = 420 };
    private readonly Label headerTitle = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ContextMenuStrip headerMenu = new(), attachmentMenu = new();
    private readonly Button boardToggle = Ui.IconButton("tasks", "Công việc & đầu ra");
    private readonly TeamBoard teamBoard = new();
    private readonly Button teamSettings;
    private readonly Button teamButton = Ui.Button("Chọn agent");
    private readonly ContextMenuStrip teamMenu = new() { BackColor = Ui.Surface, ForeColor = Ui.Ink };
    private readonly ComboBox surface = Ui.Combo(200);
    private readonly Label workspacePath = Ui.Label("");
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 200 };
    private volatile JobRecord? latest;
    private JobRecord? displayed;
    private string lastLoadedJob = "";
    private string? contextFile;
    private readonly Dictionary<string, (long Stamp, List<JobRecord> Jobs, long Access)> loadedConversations = [];
    private CancellationTokenSource? conversationLoad;
    private int conversationLoadVersion;
    private bool conversationLoading, projectMutation;
    private string detailJobId = "";
    private bool updating, closing;
    public MainWindow(string? dataDir = null)
    {
        SuspendLayout(); AutoScaleMode = AutoScaleMode.None;
        storage = new(dataDir);
        config = storage.LoadConfig(); runner = new(new AgentExecutor(new ApiTransport(secrets)), storage);
        Text = "Multi-Agent Hub"; ShowIcon = false; Font = new Font("Segoe UI", 9.5f); BackColor = Ui.Background; ForeColor = Ui.Ink;
        if (Ui.AppIcon != null) Icon = Ui.AppIcon;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1400, 900);
        Size = new Size(Math.Min(1380, (int)(area.Width * 0.95)), Math.Min(960, (int)(area.Height * 0.95)));
        MinimumSize = new Size(1000, 720); StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        KeyDown += (_, e) => {
            if (e.Control && e.KeyCode == Keys.N && !runner.Busy) { e.SuppressKeyPress = true; NewConversation(); }
            else if (e.Control && e.Shift && e.KeyCode == Keys.F) { e.SuppressKeyPress = true; sidebar.FocusSearch(); }
            else if (e.Control && e.KeyCode == Keys.Oemcomma && !runner.Busy) { e.SuppressKeyPress = true; OpenAgents(); }
        };
        var root = Ui.Table(); root.Padding = new Padding(0, 0, 0, 4); root.RowCount = 5;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 174)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        var shell = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Size = new Size(1380, 900), SplitterDistance = 286, SplitterWidth = 1, BackColor = Ui.Border };
        var rail = new NavigationRail(); rail.NewRequested += NewConversation; rail.SearchRequested += () => sidebar.FocusSearch(); rail.ProjectRequested += BrowseProject;
        shell.Panel1.Controls.Add(sidebar); shell.Panel1.Controls.Add(rail); shell.Panel2.Controls.Add(root); Controls.Add(shell); locked.Add(sidebar); locked.Add(rail);
        sidebar.NewRequested += NewConversation; sidebar.AddProjectRequested += BrowseProject;
        sidebar.ProjectSelected += path => { if (!runner.Busy && !string.Equals(path, config.Project, StringComparison.OrdinalIgnoreCase)) SetProject(path); };
        sidebar.ConversationSelected += OpenConversation;
        sidebar.ContinueInfoRequested += info => _ = OpenConversationAsync(info, true);
        sidebar.OpenTextInfoRequested += info => Protect(() => OpenTextFor(info));
        sidebar.SettingsRequested += OpenAgents;
        sidebar.ChangeProject += UpdateProject;
        sidebar.ArchiveProjectRequested += (path, archived) => _ = ArchiveProjectAsync(path, archived);
        sidebar.ExploreProjectRequested += path => Protect(() => {
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Thư mục project không còn tồn tại.");
            var explorer = new ProcessStartInfo("explorer.exe") { UseShellExecute = false }; explorer.ArgumentList.Add(path); Process.Start(explorer)?.Dispose();
        });
        Ui.Choices(surface, [new("work", "Làm việc"), new("chat", "Trò chuyện")], config.Surface); surface.AccessibleName = "Không gian Làm việc hoặc Trò chuyện";
        sidebar.SetSurfaceControl(Ui.ChoiceButton(surface, "Làm việc hoặc Trò chuyện", c => c.Id == "chat" ? "Hub Chat" : "Hub Code")); locked.Add(surface);
        surface.SelectedIndexChanged += (_, _) => {
            if (updating || runner.Busy) return;
            if (config.Surface == "chat") config.ChatMode = config.Mode; else config.WorkMode = config.Mode;
            config.Surface = Ui.Value(surface);
            mode.SelectedItem = mode.Items.Cast<Choice>().First(c => c.Id == (config.Surface == "chat" ? config.ChatMode : config.WorkMode));
            Protect(Save); UpdateSummary();
            if (displayed == null) canvas.ShowWelcome(config.Surface);
        };
        sidebar.ChangeConversation += (id, title, pinned, archived) => Protect(() => {
            storage.UpdateConversation(id, title, pinned, archived); ReloadLibrary();
            if (id == selectedConversationId && title != null) topic.Text = title;
        });
        var header = Ui.Table(2); header.Height = 54; header.AutoSize = false; header.Padding = new Padding(22, 8, 14, 8);
        header.Paint += (_, e) => { using var pen = new Pen(Ui.Border); e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 8, 8, 0) };
        topic.Dock = DockStyle.Fill; topic.BorderStyle = BorderStyle.None; topic.BackColor = Ui.Background; topic.ForeColor = Ui.Ink; topic.Font = new Font("Segoe UI", 10, FontStyle.Regular);
        topic.PlaceholderText = "Cuộc trò chuyện mới"; topic.Margin = Padding.Empty; topic.Tag = "composer-input";
        headerTitle.Font = topic.Font; heading.Controls.Add(topic); heading.Controls.Add(headerTitle);
        void Heading() { headerTitle.Text = topic.Text; headerTitle.Visible = topic.ReadOnly; topic.Visible = !topic.ReadOnly; Ui.Hint(headerTitle, topic.Text); }
        topic.TextChanged += (_, _) => Heading(); topic.ReadOnlyChanged += (_, _) => Heading(); Heading(); locked.Add(topic); header.Controls.Add(heading, 0, 0);
        var headerActions = Ui.Flow(); headerActions.WrapContents = false;
        var more = Ui.IconButton("more", "Tùy chọn hội thoại", (_, _) => OpenHeaderMenu());
        headerMenu.Tag = more; boardToggle.Click += (_, _) => OpenBoard();
        headerActions.Controls.AddRange([Ui.IconButton("down", "Đến cuối hội thoại", (_, _) => canvas.ScrollToEnd()), more, boardToggle]); header.Controls.Add(headerActions, 1, 0); root.Controls.Add(header, 0, 0);
        boardPanel.Controls.Add(teamBoard); boardPanel.Visible = false;
        var boardHeader = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(8, 2, 8, 2) };
        var boardClose = Ui.IconButton("close", "Đóng bảng công việc", (_, _) => { if (boardPanel.FindForm() is { } form && form != this) form.Close(); else boardPanel.Visible = false; }); boardClose.Dock = DockStyle.Right;
        var boardCaption = Ui.Label("Công việc & đầu ra", true); boardCaption.Dock = DockStyle.Fill;
        boardHeader.Controls.Add(boardCaption); boardHeader.Controls.Add(boardClose); boardPanel.Controls.Add(boardHeader);
        boardPanel.Paint += (_, e) => { using var pen = new Pen(Ui.Border); e.Graphics.DrawLine(pen, 0, 0, 0, boardPanel.Height); };
        conversationHost.Controls.Add(canvas); conversationHost.Controls.Add(boardPanel); root.Controls.Add(conversationHost, 0, 1);
        root.Controls.Add(sessionProgress, 0, 2);
        sessionProgress.BoardRequested += OpenBoard; sessionProgress.DetailsRequested += OpenDetails; sessionProgress.StopRequested += () => runner.Cancel();
        sessionProgress.ContinueRequested += () => {
            if (runner.Busy || conversationLoading || displayed == null) return;
            if (prompt.TextLength > 0) { prompt.Focus(); return; }
            prompt.Text = "Tiếp tục phần còn dang dở. Đối chiếu kết quả và file đã có trước khi thực hiện thêm; giữ nguyên các task đã hoàn tất.";
            StartJob();
        };
        canvas.SuggestionSelected += value => { prompt.Text = value + ": "; prompt.Focus(); prompt.SelectionStart = prompt.TextLength; };
        project.Text = config.Project; project.AccessibleName = "Project";
        recentProjects.AccessibleName = "Project gần đây";
        recentProjects.SelectedIndexChanged += (_, _) => { if (!updating && recentProjects.SelectedItem is string path) SetProject(path); };
        Shown += (_, _) => { int D(int value) => (int)(value * DeviceDpi / 96f); shell.Panel1MinSize = D(240); shell.Panel2MinSize = D(610); shell.SplitterDistance = D(286); };
        var agents = Ui.Table(); agents.RowCount = 4;
        agents.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        agents.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        agents.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        agents.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        agentPanel.Controls.Add(agents);

        var add = Ui.Flow();
        add.Controls.AddRange([Ui.Label("Công cụ AI", true), addProvider, Ui.Label("Kết nối", true), addConnection, Ui.Label("Số lượng", true), count]);
        addButton = Ui.Button("+ Thêm agent", (_, _) => AddAgents(), true);
        var discover = Ui.Button("Dò công cụ & model", (_, _) => DiscoverAgents());
        add.Controls.AddRange([discover, addButton, info]); agents.Controls.Add(add, 0, 0);
        addConnection.DropDownWidth = 340; addProvider.DropDownWidth = 220; mode.DropDownWidth = 250;
        Ui.Choices(addProvider, new[] { new Choice("", "Chọn công cụ AI…") }.Concat(Catalog.Providers));
        addProvider.AccessibleName = "Công cụ AI"; addConnection.AccessibleName = "Cách kết nối"; count.AccessibleName = "Số lượng agent";
        void ChooseProvider() {
            string provider = Ui.Value(addProvider);
            var connections = provider.Length > 0 ? Catalog.Connections(provider) : [];
            string detected = connections.FirstOrDefault(c => c.Id is "codex-cli" or "claude-cli" or "agy-cli" && AgentDiscovery.FindExecutable(new() { Provider = provider, Connection = c.Id }) != null)?.Id ?? "";
            Ui.Choices(addConnection, new[] { new Choice("", "Chọn kiểu kết nối…") }.Concat(connections), detected);
            UpdateAddAvailability();
        }
        addProvider.SelectedIndexChanged += (_, _) => ChooseProvider(); ChooseProvider();
        locked.AddRange([addProvider, addConnection, count, addButton, discover]);

        grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false; grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(224, 233, 240); grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Ink;
        grid.ColumnHeadersHeight = 36; grid.RowTemplate.Height = 36; grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(215, 238, 241); grid.DefaultCellStyle.SelectionForeColor = Ui.Ink;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = "Chạy", FillWeight = 40, MinimumWidth = 55 });
        foreach (var column in new[] { ("name", "Tên / vai trò", 110f, 130), ("provider", "Công cụ AI", 110f, 120), ("connection", "Cách kết nối", 155f, 170), ("model", "Model", 120f, 130), ("state", "Trạng thái", 105f, 110) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = column.Item1, HeaderText = column.Item2, FillWeight = column.Item3, MinimumWidth = column.Item4, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable; grid.AccessibleName = "Danh sách agent"; agents.Controls.Add(grid, 0, 1); locked.Add(grid);
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) => { if (updating || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var next = CopyConfig(); next.Agents[e.RowIndex].Enabled = grid.Rows[e.RowIndex].Cells[0].Value is true;
            try { Storage.Validate(next); storage.Write("native-config", next); config = next; UpdateSummary(); }
            catch (Exception error) { updating = true; grid.Rows[e.RowIndex].Cells[0].Value = config.Agents[e.RowIndex].Enabled; updating = false; Protect(() => throw error); }
        };
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditAgent(); };

        var actions = Ui.Flow(); var edit = Ui.Button("Thiết lập / model", (_, _) => EditAgent()); var duplicate = Ui.Button("Nhân bản", (_, _) => Duplicate());
        var remove = Ui.Button("Xóa agent", (_, _) => Remove()); var up = Ui.Button("↑", (_, _) => MoveAgent(-1)); var down = Ui.Button("↓", (_, _) => MoveAgent(1));
        up.AccessibleName = "Đưa agent lên"; down.AccessibleName = "Đưa agent xuống";
        up.MinimumSize = new Size(36, 32); down.MinimumSize = new Size(36, 32);
        Ui.Choices(mode, [new("consensus", "Chốt từng ý"), new("team", "Bàn tròn & thực hiện"), new("parallel", "Song song"), new("pipeline", "Lần lượt / phản biện"), new("collaboration", "Cùng làm việc"), new("discussion", "Bàn luận qua lại")], config.Mode); mode.AccessibleName = "Điều phối";
        mode.SelectedIndexChanged += (_, _) => { if (updating) return; config.Mode = Ui.Value(mode); if (config.Surface == "chat") config.ChatMode = config.Mode; else config.WorkMode = config.Mode; Protect(Save); UpdateSummary(); };
        discussionSettings = Ui.Button("Giới hạn bàn luận…", (_, _) => Protect(() => {
            using var dialog = new DiscussionDialog(config.Discussion, config.Agents.Count(a => a.Enabled));
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var next = CopyConfig(); next.Discussion = dialog.Result; Commit(next, Selected?.Id);
        }));
        var accounts = Ui.Button("Tài khoản / kết nối", (_, _) => { using var dialog = new AgentConnectionsDialog(config.Agents, secrets); dialog.ShowDialog(this); });
        actions.Controls.AddRange([edit, duplicate, remove, up, down, accounts, info]); agents.Controls.Add(actions, 0, 2); locked.AddRange([edit, duplicate, remove, up, down, accounts, mode, discussionSettings]);
        teamSettings = Ui.Button("Phân vai & giới hạn nhóm…", (_, _) => Protect(() => {
            using var dialog = new TeamDialog(config.Team, config.Agents, config.Project, config.Mode == "consensus");
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var next = CopyConfig(); next.Team = dialog.Result; Commit(next, Selected?.Id);
        })); locked.Add(teamSettings);
        var sessionSettings = Ui.Flow();
        sessionSettings.Controls.AddRange([teamSettings, discussionSettings]); agents.Controls.Add(sessionSettings, 0, 3);
        Ui.Hint(teamSettings, "Chọn model cho từng vai, giới hạn ý/task, lần gọi và lệnh kiểm tra của Chốt từng ý hoặc Bàn tròn & thực hiện.");
        Ui.Hint(discussionSettings, "Đặt số lượt, thời lượng, nhịp trao đổi và tổng hợp của chế độ Bàn luận qua lại.");


        var composer = new Panel { Dock = DockStyle.Fill, Height = 174, Padding = new Padding(24, 6, 24, 10) };
        var input = new ComposerPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 8), BackColor = Ui.Background };
        var inputLayout = Ui.Table(); inputLayout.RowCount = 4;
        inputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); inputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); inputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        input.Controls.Add(inputLayout); composer.Controls.Add(input);
        workspacePath.AutoSize = false; workspacePath.AutoEllipsis = true; workspacePath.Dock = DockStyle.Fill;
        workspacePath.Margin = new Padding(0, 0, 0, 4); workspacePath.TextAlign = ContentAlignment.MiddleLeft;
        workspacePath.AccessibleName = "Thư mục project hiện tại"; workspacePath.UseMnemonic = false;
        inputLayout.Controls.Add(workspacePath, 0, 0);
        prompt.BorderStyle = BorderStyle.None; prompt.ScrollBars = ScrollBars.Vertical; prompt.AcceptsReturn = true; prompt.AccessibleName = "Tin nhắn cho agent"; prompt.PlaceholderText = "Hỏi điều gì đó hoặc mô tả việc cần làm…"; prompt.Tag = "composer-input";
        prompt.Font = new Font("Segoe UI", 9.5f); prompt.Margin = new Padding(0, 2, 0, 4); inputLayout.Controls.Add(prompt, 0, 1); locked.Add(prompt);
        prompt.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartJob(); } };
        var toolbar = Ui.Table(2); toolbar.Margin = Padding.Empty; toolbar.RowCount = 1; toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var choices = Ui.Flow(); choices.WrapContents = false;
        var attach = Ui.IconButton("plus", "Đính kèm, thay hoặc bỏ file", (_, _) => OpenAttachmentMenu()); attachmentMenu.Tag = attach; locked.Add(attach);
        var modeChoice = Ui.ChoiceButton(mode, "Cách phối hợp agent", c => c.Id switch { "consensus" => "Chốt từng ý", "team" => "Bàn tròn", "collaboration" => "Cùng làm", "pipeline" => "Lần lượt", "discussion" => "Bàn luận", _ => "Song song" }, c => ModeExplanation(c.Id));
        choices.Controls.AddRange([attach, modeChoice]); toolbar.Controls.Add(choices, 0, 0);
        teamButton.Click += (_, _) => ChooseTeam(); ((ModernButton)teamButton).Appearance = "ghost"; teamButton.MaximumSize = new Size(220, 34); teamButton.MinimumSize = new Size(80, 30); locked.Add(teamButton);
        var send = Ui.Flow(); send.WrapContents = false; send.Dock = DockStyle.Right;
        run = Ui.IconButton("up", "Gửi yêu cầu", (_, _) => StartJob(), true); run.Size = new Size(32, 32);
        stop = sessionProgress.StopButton; stop.Enabled = false; stop.Visible = false;
        send.Controls.AddRange([teamButton, run]); toolbar.Controls.Add(send, 1, 0); locked.Add(run);
        attachment.AutoEllipsis = true; attachment.AutoSize = false; attachment.Height = 22; attachment.Dock = DockStyle.Top; attachment.Visible = false;
        inputLayout.Controls.Add(attachment, 0, 2); inputLayout.Controls.Add(toolbar, 0, 3);
        root.Controls.Add(composer, 0, 3);
        bool adjustingComposer = false, composerLayoutQueued = false;
        int measuredPromptWidth = -1;
        void QueueComposerLayout() {
            if (composerLayoutQueued || !IsHandleCreated || Disposing || IsDisposed) return;
            composerLayoutQueued = true;
            BeginInvoke((Action)(() => {
                composerLayoutQueued = false;
                if (!Disposing && !IsDisposed) ComposerLayout();
            }));
        }
        void ComposerLayout() {
            if (adjustingComposer || Disposing || IsDisposed || composer.IsDisposed) return;
            adjustingComposer = true;
            try {
                int D(int value) => (int)Math.Round(value * composer.DeviceDpi / 96f);
                int margin = Math.Max(D(24), (composer.Width - D(780)) / 2);
                var padding = new Padding(margin, D(6), margin, D(10));
                if (composer.Padding != padding) composer.Padding = padding;
                int lines = prompt.IsHandleCreated ? prompt.GetLineFromCharIndex(prompt.TextLength) + 1 : 1;
                int lineHeight;
                using (var graphics = prompt.CreateGraphics())
                    lineHeight = TextRenderer.MeasureText(graphics, "Mg", prompt.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height + D(2);
                int textHeight = (int)Math.Clamp((long)Math.Max(1, lines) * lineHeight + D(8), D(58), D(150));
                int attachmentHeight = contextFile != null ? D(24) : 0;
                int toolbarHeight = D(36);
                int workspaceHeight = D(24);
                // The parent row owns the height of its docked child. Setting composer.Height
                // first causes an immediate competing dock/layout pass during native text input.
                int desiredHeight = textHeight + prompt.Margin.Vertical + input.Padding.Vertical + padding.Vertical + toolbarHeight + attachmentHeight + workspaceHeight;
                bool workspaceChanged = Math.Abs(inputLayout.RowStyles[0].Height - workspaceHeight) > 0.5f;
                bool attachmentChanged = Math.Abs(inputLayout.RowStyles[2].Height - attachmentHeight) > 0.5f;
                bool toolbarChanged = Math.Abs(inputLayout.RowStyles[3].Height - toolbarHeight) > 0.5f;
                bool heightChanged = Math.Abs(root.RowStyles[3].Height - desiredHeight) > 0.5f;
                if (workspaceChanged || attachmentChanged || toolbarChanged || heightChanged) {
                    root.SuspendLayout(); inputLayout.SuspendLayout();
                    try {
                        if (workspaceChanged) inputLayout.RowStyles[0].Height = workspaceHeight;
                        if (attachmentChanged) inputLayout.RowStyles[2].Height = attachmentHeight;
                        if (toolbarChanged) inputLayout.RowStyles[3].Height = toolbarHeight;
                        if (heightChanged) root.RowStyles[3].Height = desiredHeight;
                    } finally { inputLayout.ResumeLayout(true); root.ResumeLayout(true); }
                }
            } finally { adjustingComposer = false; }
        }
        prompt.TextChanged += (_, _) => QueueComposerLayout();
        prompt.SizeChanged += (_, _) => { if (prompt.ClientSize.Width != measuredPromptWidth) { measuredPromptWidth = prompt.ClientSize.Width; QueueComposerLayout(); } };
        prompt.FontChanged += (_, _) => QueueComposerLayout();
        composer.Resize += (_, _) => QueueComposerLayout(); DpiChanged += (_, _) => QueueComposerLayout();
        attachment.TextChanged += (_, _) => { attachment.Visible = contextFile != null; QueueComposerLayout(); }; Shown += (_, _) => QueueComposerLayout();
        var output = Ui.Table(); output.RowStyles.Add(new RowStyle(SizeType.AutoSize)); output.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); detailPanel.Controls.Add(output);
        var outputHeader = Ui.Flow(); outputHeader.Controls.AddRange([history, Ui.Button("Xem lịch sử", (_, _) => ShowHistory()), Ui.Button("Dùng lại yêu cầu", (_, _) => Reuse()), Ui.Button("Xuất Markdown…", (_, _) => ExportJob())]); output.Controls.Add(outputHeader, 0, 0);
        history.AccessibleName = "Lịch sử tác vụ"; output.Controls.Add(results, 0, 1);
        status.Font = new Font("Segoe UI", 8); status.AutoSize = false; status.AutoEllipsis = true; status.Dock = DockStyle.Fill; status.Height = 18; status.Margin = new Padding(24, 0, 24, 1); root.Controls.Add(status, 0, 4);
        status.TextChanged += (_, _) => Ui.Hint(status, status.Text);
        runner.Changed += value => latest = value;
        runner.AgentQuotaReached += id => pendingQuota[id] = 0;
        refresh.Tick += (_, _) => {
            ApplyQuotaChanges();
            if (latest is { } next && !ReferenceEquals(next, displayed)) { displayed = next; RenderJob(next); }
            if (runner.Busy && displayed is { } current) ShowJobStatus(current);
        };
        canvas.SetupRequested += OpenAgents;
        refresh.Start(); FormClosing += OnClosing; Populate(); LoadHistory(); LoadProjects();
        try { storage.ImportHistory(); ReloadLibrary(); } catch (Exception e) { status.Text = "Chưa nhập được lịch sử vào thư viện: " + e.Message; }
        float scale = DeviceDpi / 96f;
        shell.Scale(new SizeF(scale, scale)); agentPanel.Scale(new SizeF(scale, scale)); detailPanel.Scale(new SizeF(scale, scale)); MinimumSize = new Size((int)(900 * scale), (int)(620 * scale));
        grid.RowTemplate.Height = (int)(36 * scale); grid.ColumnHeadersHeight = (int)(36 * scale);
        foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
        Ui.Theme(this); Ui.Theme(agentPanel); Ui.Theme(detailPanel); sidebar.ApplyTheme(); rail.ApplyTheme(); canvas.ApplyTheme(); canvas.ShowWelcome(config.Surface); canvas.SetAgentAvailability(config.Agents.Count > 0); topic.BackColor = Ui.Background;
        AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        Size = new Size(Math.Min((int)(1380 * scale), (int)(area.Width * 0.95)), Math.Min((int)(960 * scale), (int)(area.Height * 0.95)));
        if (config.LastConversationId.Length > 0 && storage.Conversations().FirstOrDefault(c => c.Id == config.LastConversationId) is { } previousChat) Shown += (_, _) => OpenConversation(previousChat);
    }
    private AgentConfig? Selected => grid.CurrentRow == null ? null : config.Agents.ElementAtOrDefault(grid.CurrentRow.Index);
    private void ShowPanel(Panel panel, string title, int width, int height)
    {
        using var dialog = new Form { Text = title, Font = Font, StartPosition = FormStartPosition.CenterParent,
            BackColor = Ui.Background, ForeColor = Ui.Ink, ShowInTaskbar = false, Icon = Icon, AutoScaleMode = AutoScaleMode.None };
        float scale = DeviceDpi / 96f; var area = Screen.FromControl(this).WorkingArea;
        dialog.Size = new Size(Math.Min((int)(width * scale), area.Width-40), Math.Min((int)(height * scale), area.Height-40));
        dialog.Controls.Add(panel);
        try { dialog.ShowDialog(this); } finally { dialog.Controls.Remove(panel); }
    }
    private void OpenAgents()
    {
        if (config.Agents.Count == 0 && !runner.Busy) DiscoverAgents();
        ShowPanel(agentPanel, "Agent & model", 1220, 640);
    }
    private void OpenDetails()
    {
        if (displayed != null) RenderDetails(displayed); else ClearResults();
        ShowPanel(detailPanel, "Chi tiết phiên • câu trả lời / nhật ký / prompt", 1200, 760);
    }
    private void OpenBoard()
    {
        if (conversationHost.Width >= 1020 * DeviceDpi / 96f) { boardPanel.Visible = !boardPanel.Visible; return; }
        boardPanel.Dock = DockStyle.Fill; boardPanel.Visible = true;
        try { ShowPanel(boardPanel, "Công việc & đầu ra", 1000, 760); }
        finally { boardPanel.Dock = DockStyle.Right; boardPanel.Visible = false; conversationHost.Controls.Add(boardPanel); boardPanel.BringToFront(); }
    }
    private void OpenHeaderMenu()
    {
        StyleMenu(headerMenu); headerMenu.Items.Clear();
        headerMenu.Items.Add("Chi tiết / nhật ký / prompt", null, (_, _) => OpenDetails());
        headerMenu.Items.Add("Sao chép hội thoại", null, (_, _) => canvas.CopyAll());
        var text = headerMenu.Items.Add("Mở bản .txt", null, (_, _) => OpenConversationText()); text.Enabled = selectedConversationId != null;
        var manage = headerMenu.Items.Add("Ghim / đổi tên / lưu trữ…", null, (_, _) => { if (headerMenu.Tag is Control anchor) sidebar.ShowTopicMenu(anchor, selectedConversationId); }); manage.Enabled = selectedConversationId != null && !runner.Busy;
        if (runner.Busy && Catalog.IsTeamMode(displayed?.Mode)) {
            headerMenu.Items.Add(new ToolStripSeparator());
            headerMenu.Items.Add("Dừng sau bước hiện tại", null, (_, _) => Protect(runner.StopTeamAfterStep)).Enabled = displayed?.Team?.StopAfterStep != true;
        }
        if (runner.Busy && displayed?.Mode == "discussion") {
            headerMenu.Items.Add(new ToolStripSeparator());
            bool accepting = !displayed.DiscussionFinishRequested && displayed.DiscussionPhase is not ("summarizing" or "finished");
            headerMenu.Items.Add("Chạy lượt tiếp ngay", null, (_, _) => Protect(runner.NextDiscussionTurn)).Enabled = accepting && displayed.DiscussionPhase == "resting";
            headerMenu.Items.Add("Kết thúc & tổng hợp", null, (_, _) => Protect(runner.FinishDiscussion)).Enabled = accepting;
        }
        if (headerMenu.Tag is Control button) headerMenu.Show(button, new Point(0, button.Height));
    }
    private void OpenAttachmentMenu()
    {
        if (runner.Busy) return;
        if (contextFile == null) { Attach(); return; }
        StyleMenu(attachmentMenu); attachmentMenu.Items.Clear();
        attachmentMenu.Items.Add("Thay file đính kèm…", null, (_, _) => Attach());
        attachmentMenu.Items.Add("Bỏ file đính kèm", null, (_, _) => { contextFile = null; attachment.Text = "Không đính kèm file"; });
        if (attachmentMenu.Tag is Control button) attachmentMenu.Show(button, new Point(0, -attachmentMenu.PreferredSize.Height));
    }
    private static string ModeExplanation(string id) => id switch {
        "consensus" => "Chốt từng ý: A nêu một ý ngắn, B phản hồi ngay; hai bên sửa và xác nhận cùng phiên bản trước khi thực hiện/review phần đó rồi mở ý tiếp.\nKhông tự coi im lặng là đồng ý; hết giới hạn vẫn chưa chốt thì giữ lại bất đồng. Cấu hình trong Agent & model → Phân vai & giới hạn nhóm.",
        "parallel" => "Song song: các agent cùng nhận yêu cầu và trả lời độc lập.\nPhù hợp để so sánh nhiều phương án; không truyền kết quả cho nhau trong lượt này.",
        "pipeline" => "Lần lượt / phản biện: chạy theo thứ tự trong danh sách agent.\nAgent sau nhận kết quả của các agent trước để bổ sung hoặc phản biện; mỗi agent một lượt.",
        "collaboration" => "Cùng làm việc: agent đầu thực hiện, các agent còn lại rà soát, rồi agent khả dụng đầu tiên hoàn thiện.\nVí dụ: viết code → review → sửa theo góp ý. Chỉ chọn một agent thì chạy một lượt.",
        "discussion" => "Bàn luận qua lại: các model trao đổi, phản biện và phát triển ý tưởng qua nhiều lượt, rồi tổng hợp.\nPhù hợp để khám phá một chủ đề. Đặt số lượt/thời lượng trong Agent & model → Giới hạn bàn luận.",
        "team" => "Bàn tròn & thực hiện: các vai ngang hàng thảo luận, thống nhất task, thực hiện và review kết quả.\nPhù hợp xây dựng project có nhiều việc/phụ thuộc. Phân vai và giới hạn trong Agent & model.",
        _ => "Chọn cách các agent trao đổi và thực hiện yêu cầu."
    };
    private static void StyleMenu(ContextMenuStrip menu)
    {
        menu.Renderer = new ToolStripProfessionalRenderer(new LightMenuColors()); menu.BackColor = Color.White; menu.ForeColor = Ui.Ink; menu.Font = new Font("Segoe UI", 9); menu.ShowImageMargin = false;
    }
    private void ChooseTeam()
    {
        if (runner.Busy) return;
        if (config.Agents.Count == 0) { OpenAgents(); return; }
        var menu = teamMenu; menu.Items.Clear();
        foreach (var agent in config.Agents) {
            var item = new ToolStripMenuItem(agent.Name + "  ·  " + (agent.Model.Length > 0 ? agent.Model : Catalog.ConnectionName(agent))) { Checked = agent.Enabled };
            item.Click += (_, _) => Protect(() => { var next = CopyConfig(); next.Agents.First(a => a.Id == agent.Id).Enabled = !agent.Enabled; Commit(next, agent.Id); });
            menu.Items.Add(item);
        }
        menu.Show(teamButton, new Point(0, -menu.PreferredSize.Height));
    }
    private void Protect(Action action) { try { action(); } catch (Exception e) { status.Text = e.Message; MessageBox.Show(this, e.Message, "Multi-Agent Hub", MessageBoxButtons.OK, MessageBoxIcon.Information); } }
    private void Save() { Storage.Validate(config); storage.Write("native-config", config); }
    private void ApplyQuotaChanges(bool force = false)
    {
        if (pendingQuota.IsEmpty) return;
        var ids = pendingQuota.Keys.ToArray();
        updating = true;
        foreach (string id in ids) {
            int index = config.Agents.FindIndex(a => a.Id == id); if (index < 0) continue;
            config.Agents[index].Enabled = false; grid.Rows[index].Cells["enabled"].Value = false;
        }
        updating = false; UpdateSummary();
        if (!force && DateTimeOffset.Now < retryQuotaSave) return;
        try { Save(); foreach (string id in ids) pendingQuota.TryRemove(id, out _); quotaSaveError = ""; }
        catch (Exception e) { quotaSaveError = "Chưa lưu được việc bỏ chọn: " + e.Message; retryQuotaSave = DateTimeOffset.Now.AddSeconds(5); }
    }
    private HubConfig CopyConfig() => JsonSerializer.Deserialize<HubConfig>(JsonSerializer.Serialize(config, Json.Options), Json.Options)!;
    private void Commit(HubConfig next, string? selected = null)
    {
        Storage.Validate(next); storage.Write("native-config", next); config = next; Populate(selected);
    }
    private void Populate(string? selected = null)
    {
        updating = true; grid.Rows.Clear();
        foreach (var agent in config.Agents)
        {
            string ready = Availability(agent);
            int row = grid.Rows.Add(agent.Enabled, agent.Name, Catalog.ProviderName(agent.Provider), Catalog.ConnectionName(agent), agent.Model.Length > 0 ? agent.Model : "Theo ứng dụng / CLI", ready);
            if (agent.Id == selected) grid.CurrentCell = grid.Rows[row].Cells[1];
        }
        updating = false; count.Maximum = Math.Max(1, Catalog.MaxAgents - config.Agents.Count); UpdateAddAvailability(); UpdateSummary();
    }
    private void UpdateAddAvailability() => addButton.Enabled = !runner.Busy && config.Agents.Count < Catalog.MaxAgents && Ui.Value(addProvider).Length > 0 && Ui.Value(addConnection).Length > 0;
    private string Availability(AgentConfig agent)
    {
        if (Catalog.IsManual(agent.Connection)) return agent.Connection == "manual" || ProcessTransport.Find(agent) != null ? "Sao chép / dán" : "Chọn .exe / sao chép";
        if (!Catalog.IsApi(agent.Connection)) return ProcessTransport.Find(agent) != null ? "CLI đã tìm thấy" : "Chưa tìm thấy CLI";
        try { new ApiTransport(secrets).Validate(agent); return "API đã cấu hình"; }
        catch (Exception e) { return e.Message.Replace(agent.Name + ": ", ""); }
    }
    private void UpdateSummary()
    {
        canvas.SetAgentAvailability(config.Agents.Count > 0);
        var chosen = config.Agents.Where(a => a.Enabled).ToList(); info.Text = $"{chosen.Count}/{config.Agents.Count} chọn • tối đa {Catalog.MaxAgents}";
        string pathCaption = string.IsNullOrWhiteSpace(config.Project) ? "Chưa chọn thư mục project" : "Project: " + config.Project;
        if (workspacePath.Text != pathCaption) { workspacePath.Text = pathCaption; Ui.Hint(workspacePath, pathCaption); }
        teamButton.Text = chosen.Count == 0 ? "Chọn agent ▾" : chosen.Count == 1 ? chosen[0].Name + " ▾" : $"{chosen[0].Name} + {chosen.Count-1} ▾";
        teamButton.AccessibleName = "Chọn agent tham gia: " + string.Join(" + ", chosen.Select(a => a.Name));
        teamSettings.Enabled = !runner.Busy;
        boardToggle.Visible = Catalog.IsTeamMode(config.Mode) || Catalog.IsTeamMode(displayed?.Mode);
        if (!boardToggle.Visible) boardPanel.Visible = false;
        summary.Text = Catalog.IsTeamMode(config.Mode) ? $"{(config.Mode == "consensus" ? "Chốt một ý mỗi lần" : "Ngang hàng")} • ≤{config.Team.MaxCalls} lần gọi • {(config.Team.RunVerification ? "có lệnh kiểm tra" : "bạn tự kiểm tra")}" : config.Mode == "discussion" ? $"{chosen.Count} model • ≤{config.Discussion.MaxTurns} lượt • ≤{config.Discussion.DurationMinutes} phút" : config.Mode == "collaboration" ? "Thực hiện → rà soát → hoàn thiện" : config.Mode == "pipeline" ? string.Join(" → ", chosen.Select(a => a.Name)) : $"{chosen.Count} agent cùng nhận yêu cầu"; summary.MaximumSize = new Size(420, 32);
        discussionSettings.Enabled = !runner.Busy;
        run.Text = ""; run.AccessibleName = runner.Busy && displayed?.Mode is "team" or "consensus" or "discussion" ? "Gửi bổ sung" : "Gửi yêu cầu"; Ui.Hint(run, run.AccessibleName);
        Ui.Hint(teamButton, string.Join("\n", chosen.Select(a => $"{a.Name} • {a.Model}"))); Ui.Hint(status, summary.Text);
        prompt.PlaceholderText = config.Surface == "chat" ? "Hỏi bất cứ điều gì… Ctrl + Enter để gửi" : config.Mode == "discussion" ? "Chủ đề và điều bạn muốn các model cùng khám phá…" : "Mô tả việc cần làm… Ctrl + Enter để gửi";
    }
    private void AddAgents() => Protect(() => {
        if (runner.Busy) return; int quantity = (int)count.Value;
        if (config.Agents.Count + quantity > Catalog.MaxAgents) throw new ArgumentException($"Tối đa {Catalog.MaxAgents} agent.");
        string provider = Ui.Value(addProvider), connection = Ui.Value(addConnection);
        if (provider.Length == 0 || connection.Length == 0) throw new ArgumentException("Chọn công cụ AI và cách kết nối trước khi thêm agent.");
        var next = CopyConfig();
        for (int i = 0; i < quantity; i++) {
            var agent = new AgentConfig { Provider = provider, Connection = connection, Name = Catalog.ProviderName(provider) + " " + (next.Agents.Count + 1), Permission = connection == "agy-cli" || Catalog.IsManual(connection) || Catalog.IsApi(connection) ? "default" : "read-only" };
            agent.Enabled = Catalog.IsManual(connection) || (!Catalog.IsApi(connection) && ProcessTransport.Find(agent) != null); next.Agents.Add(agent);
        }
        Commit(next, next.Agents[^1].Id); status.Text = "Đã thêm agent. Agent chưa cấu hình được tắt; mở Thiết lập / model để hoàn thiện.";
    });
    private void EditAgent() => Protect(() => {
        if (runner.Busy || Selected is not { } selected) return; using var dialog = new AgentEditor(selected, secrets);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var next = CopyConfig(); next.Agents[next.Agents.FindIndex(a => a.Id == selected.Id)] = dialog.Result; Commit(next, selected.Id); status.Text = "Đã lưu cấu hình agent. Đánh dấu Chạy để đưa agent vào tác vụ.";
    });
    private void DiscoverAgents() => Protect(() => {
        if (runner.Busy) return;
        Form owner = agentPanel.FindForm() ?? this;
        using var discovery = new AgentDiscoveryDialog(config.Agents, secrets);
        if (discovery.ShowDialog(owner) != DialogResult.OK || discovery.Selected is not { } found) return;
        if (!found.Existing && config.Agents.Count >= Catalog.MaxAgents) throw new ArgumentException($"Tối đa {Catalog.MaxAgents} agent.");
        using var editor = new AgentEditor(found.Agent, secrets, found.Inventory);
        if (editor.ShowDialog(owner) != DialogResult.OK) return;
        var next = CopyConfig(); int existing = next.Agents.FindIndex(a => a.Id == editor.Result.Id);
        if (existing >= 0) next.Agents[existing] = editor.Result; else next.Agents.Add(editor.Result);
        Commit(next, editor.Result.Id); status.Text = "Đã lưu agent bạn chọn từ kết quả dò. Bật Chạy để sử dụng; agent đang tắt không tự bật lại.";
    });
    private void Duplicate() => Protect(() => {
        if (runner.Busy || Selected is not { } agent) return; if (config.Agents.Count >= Catalog.MaxAgents) throw new ArgumentException("Đã đạt giới hạn agent.");
        var copy = agent.Copy(true); copy.Name = (agent.Name + " (bản sao)")[..Math.Min(agent.Name.Length + 10, 80)];
        var next = CopyConfig(); next.Agents.Insert(config.Agents.IndexOf(agent) + 1, copy); if (Catalog.IsApi(copy.Connection)) copy.Enabled = false;
        Commit(next, copy.Id); status.Text = "Đã nhân bản. API key riêng cần cấu hình lại hoặc dùng biến môi trường.";
    });
    private void Remove() => Protect(() => { if (runner.Busy || Selected is not { } agent) return; var next = CopyConfig(); next.Agents.RemoveAll(a => a.Id == agent.Id); Commit(next); secrets.Delete(agent.Id); status.Text = "Đã bỏ agent và key riêng; lịch sử được giữ lại."; });
    private void MoveAgent(int direction) => Protect(() => {
        if (runner.Busy || Selected is not { } agent) return; int index = config.Agents.IndexOf(agent), target = index + direction; if (target < 0 || target >= config.Agents.Count) return;
        var next = CopyConfig(); (next.Agents[index], next.Agents[target]) = (next.Agents[target], next.Agents[index]); Commit(next, agent.Id);
    });
    private void BrowseProject()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = project.Text, Description = "Chọn project" };
        if (dialog.ShowDialog(this) == DialogResult.OK) SetProject(dialog.SelectedPath);
    }
    private void SetProject(string value) => Protect(() => {
        string path = Path.GetFullPath(value.Trim()); if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Project không tồn tại.");
        CancelConversationLoad(); run.Enabled = !runner.Busy;
        if (path != config.Project) { contextFile = null; attachment.Text = "Không đính kèm file"; }
        bool changed = !string.Equals(path, config.Project, StringComparison.OrdinalIgnoreCase);
        var next = CopyConfig(); next.Project = path; next.RecentProjects = new[] { path }.Concat(next.RecentProjects).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ProjectCatalog.Ensure(next, path);
        Commit(next, Selected?.Id); project.Text = path; LoadProjects(); if (changed) NewConversation(); else ReloadLibrary();
    });
    private void LoadProjects()
    {
        updating = true; recentProjects.Items.Clear(); recentProjects.Items.AddRange(config.RecentProjects.Where(Directory.Exists).Cast<object>().ToArray()); recentProjects.SelectedItem = config.Project; updating = false;
    }
    private void Attach() => Protect(() => {
        using var dialog = new OpenFileDialog { InitialDirectory = Directory.Exists(project.Text) ? project.Text : config.Project, Title = "Chọn file trong project (tối đa 512 KB)" };
        if (dialog.ShowDialog(this) == DialogResult.OK) { ProjectContext.Read(project.Text, dialog.FileName); contextFile = dialog.FileName; attachment.Text = Path.GetFileName(contextFile); }
    });
    private void StartJob() => Protect(() => {
        if (conversationLoading || projectMutation) { status.Text = "Đang mở hội thoại/cập nhật thư viện, đợi hoàn tất rồi gửi yêu cầu."; return; }
        if (!runner.Busy && config.Agents.Count == 0) { OpenAgents(); status.Text = "Thêm agent và cấu hình kết nối trước khi gửi yêu cầu."; return; }
        if (runner.Busy) {
            if (Catalog.IsTeamMode(displayed?.Mode)) { runner.AddTeamInstruction(prompt.Text.Trim()); prompt.Clear(); canvas.ScrollToEnd(); status.Text = "Đã nhận bổ sung; áp dụng tại ranh giới bước tiếp theo."; }
            else if (displayed?.Mode == "discussion") { runner.AddDiscussionInstruction(prompt.Text.Trim()); prompt.Clear(); canvas.ScrollToEnd(); status.Text = "Đã nhận bổ sung; bỏ thời gian nghỉ và gửi vào lượt tiếp theo. Lượt đang chạy được giữ nguyên."; }
            return;
        }
        config.Project = Path.GetFullPath(project.Text.Trim()); if (!Directory.Exists(config.Project)) throw new DirectoryNotFoundException("Project không tồn tại.");
        config.RecentProjects = new[] { config.Project }.Concat(config.RecentProjects).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ProjectCatalog.Ensure(config, config.Project); Save(); LoadProjects();
        string context = contextFile == null ? "" : ProjectContext.Read(config.Project, contextFile);
        runner.Start(config, prompt.Text.Trim(), context, continueFrom, topic.Text.Trim()); prompt.Clear(); ClearResults(); displayed = null;
        if (runner.Snapshot() is { } job) { selectedConversationId = job.ConversationId; config.LastConversationId = job.ConversationId; Protect(Save); topic.Text = job.Title; RenderJob(job); canvas.ScrollToEnd(); }
    });
    private void ClearResults()
    {
        var pages = results.TabPages.Cast<TabPage>().ToArray(); results.TabPages.Clear(); foreach (var page in pages) page.Dispose();
        views.Clear(); discussionView = null; conversationView = null; detailJobId = "";
    }
    private void ReloadLibrary()
    {
        try {
            var conversations = storage.Conversations(); int previousCount = config.Projects.Count;
            ProjectCatalog.Merge(config, conversations); if (previousCount != config.Projects.Count) Save();
            sidebar.Reload(config.Projects, config.ProjectSections, conversations, config.Project, selectedConversationId);
        }
        catch (Exception e) { status.Text = "Không đọc được thư viện: " + e.Message; }
    }
    private void NewConversation()
    {
        if (runner.Busy) return;
        CancelConversationLoad();
        selectedConversationId = null; continueFrom = null; conversationJobs = []; latest = null; displayed = null;
        config.LastConversationId = ""; contextFile = null; attachment.Text = "Không đính kèm file"; Protect(Save);
        topic.ReadOnly = false; topic.Clear(); prompt.Clear(); teamBoard.Clear(); sessionProgress.Clear(); canvas.ShowWelcome(config.Surface); UpdateSummary(); ReloadLibrary(); run.Enabled = true;
        status.Text = "Cuộc trò chuyện mới. Đặt tên topic hoặc để Hub lấy tên từ yêu cầu đầu tiên."; prompt.Focus();
    }
    private void CancelConversationLoad() { conversationLoadVersion++; conversationLoad?.Cancel(); conversationLoad = null; conversationLoading = false; }
    private DiscussionOptions RestoreDiscussionLimits(DiscussionOptions? saved)
    {
        var options = saved?.Copy() ?? config.Discussion.Copy();
        // Old transcripts describe their earlier schedule; retain the user's current pacing preference.
        options.PauseSeconds = config.Discussion.PauseSeconds;
        return options;
    }
    private void OpenConversation(ConversationInfo info) => _ = OpenConversationAsync(info);
    private async Task OpenConversationAsync(ConversationInfo info, bool focus = false)
    {
        if (runner.Busy || closing) return;
        if (selectedConversationId == info.Id && displayed != null && !conversationLoading) { if (focus) prompt.Focus(); return; }
        CancelConversationLoad(); int version = conversationLoadVersion;
        using var request = new CancellationTokenSource(); conversationLoad = request; conversationLoading = true;
        run.Enabled = false; sessionProgress.Clear(); status.Text = "Đang mở: " + info.Title;
        try {
            long stamp = await Task.Run(() => storage.ConversationStamp(info.Id), request.Token);
            List<JobRecord> jobs;
            if (loadedConversations.TryGetValue(info.Id, out var cached) && cached.Stamp == stamp) jobs = cached.Jobs;
            else jobs = await Task.Run(() => storage.ReadConversationJobs(info.Id, token: request.Token), request.Token);
            if (request.IsCancellationRequested || version != conversationLoadVersion || runner.Busy || closing || IsDisposed) return;
            if (jobs.Count == 0) throw new InvalidDataException("Cuộc trò chuyện chưa có nội dung.");
            loadedConversations[info.Id] = (stamp, jobs, Environment.TickCount64);
            while (loadedConversations.Count > 4) loadedConversations.Remove(loadedConversations.MinBy(p => p.Value.Access).Key);
            selectedConversationId = info.Id; continueFrom = jobs[^1]; conversationJobs = jobs; latest = null; displayed = jobs[^1];
            config.LastConversationId = info.Id; config.Project = info.Project; project.Text = info.Project;
            contextFile = null; attachment.Text = "Không đính kèm file";
            updating = true;
            try {
                config.Discussion = RestoreDiscussionLimits(continueFrom.Discussion); config.Team = continueFrom.Team?.Options.Copy() ?? config.Team;
                config.Surface = continueFrom.Surface; config.Mode = continueFrom.Mode;
                if (config.Surface == "chat") config.ChatMode = config.Mode; else config.WorkMode = config.Mode;
                surface.SelectedItem = surface.Items.Cast<Choice>().First(c => c.Id == config.Surface);
                mode.SelectedItem = mode.Items.Cast<Choice>().First(c => c.Id == config.Mode);
                topic.ReadOnly = true; topic.Text = info.Title; prompt.Clear();
            } finally { updating = false; }
            RenderJob(displayed, false); Save(); UpdateSummary(); sidebar.SelectConversation(info.Id, info.Project);
            status.Text = $"{info.Title} • {jobs.Count} lần trao đổi • Nhập yêu cầu mới để tiếp tục.";
            if (focus) { if (!Directory.Exists(info.Project)) throw new DirectoryNotFoundException("Thư mục project không còn tồn tại; hội thoại vẫn đọc được."); prompt.Focus(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed && version == conversationLoadVersion) { status.Text = "Không mở được hội thoại: " + e.Message; if (selectedConversationId != null) sidebar.SelectConversation(selectedConversationId, config.Project); } }
        finally { if (!IsDisposed && version == conversationLoadVersion) { conversationLoad = null; conversationLoading = false; run.Enabled = !runner.Busy || Catalog.IsTeamMode(displayed?.Mode); } }
    }
    private void OpenConversationText() => Protect(() => {
        if (selectedConversationId == null) { status.Text = "Chọn cuộc trò chuyện ở thanh bên trước."; return; }
        var info = storage.Conversations().First(c => c.Id == selectedConversationId); OpenTextFor(info);
    });
    private void OpenTextFor(ConversationInfo info)
    {
        string path = storage.ConversationTextPath(info);
        if (!File.Exists(path)) throw new FileNotFoundException("Chưa có bản text của cuộc trò chuyện.");
        Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = false, ArgumentList = { path } })?.Dispose(); status.Text = "File hội thoại: " + path;
    }
    private void RenderJob(JobRecord job, bool live = true)
    {
        canvas.Update(conversationJobs.Where(j => j.Id != job.Id).Append(job));
        bool busy = runner.Busy;
        bool liveComposer = job.Mode is "team" or "consensus" or "discussion";
        bool canSend = Catalog.IsTeamMode(job.Mode) && job.State == "running" || job.Mode == "discussion" && !job.DiscussionFinishRequested && job.DiscussionPhase is not ("summarizing" or "finished");
        foreach (var control in locked) {
            bool enabled = !busy || liveComposer && control == prompt || canSend && control == run;
            // Keep the live composer focused while snapshots arrive; drafts also survive summarizing.
            if (control.Enabled != enabled) control.Enabled = enabled;
        }
        stop.Enabled = busy; stop.Visible = busy; run.Visible = !busy || liveComposer; history.Enabled = !busy;
        string sendAction = busy && liveComposer ? "Gửi bổ sung" : "Gửi yêu cầu";
        if (run.AccessibleName != sendAction) { run.AccessibleName = sendAction; Ui.Hint(run, sendAction); }
        if (busy && job.Mode == "discussion") prompt.PlaceholderText = canSend ? "Bổ sung ý kiến… Ctrl + Enter để gửi vào lượt tiếp theo" : "Đang kết thúc bàn luận… Bạn có thể soạn yêu cầu tiếp theo tại đây";
        if (Catalog.IsTeamMode(job.Mode)) {
            boardToggle.Visible = true;
            teamBoard.Update(job, busy); run.Text = ""; run.AccessibleName = busy ? "Gửi bổ sung" : "Gửi yêu cầu";
        }
        UpdateAddAvailability(); ShowJobStatus(job);
        if (detailPanel.FindForm() != null) RenderDetails(job);
        if (live && !busy && lastLoadedJob != job.Id) {
            lastLoadedJob = job.Id; LoadHistory(); ReloadLibrary();
            if (selectedConversationId != null) {
                loadedConversations.Remove(selectedConversationId); continueFrom = job; topic.ReadOnly = true;
                conversationJobs = conversationJobs.Where(j => j.Id != job.Id).Append(job).ToList(); UpdateSummary();
            }
        }
    }
    private void RenderDetails(JobRecord job)
    {
        if (detailJobId != job.Id) { ClearResults(); detailJobId = job.Id; }
        bool busy = runner.Busy;
        if (selectedConversationId != null) {
            if (conversationView == null) { conversationView = new(); results.TabPages.Add(conversationView.Page); Ui.Theme(conversationView.Page); }
            conversationView.Update(conversationJobs.Where(j => j.Id != job.Id).Append(job));
        }
        if (job.Mode == "discussion") {
            if (discussionView == null) { discussionView = new(Protect, DeviceDpi / 96f); results.TabPages.Add(discussionView.Page); Ui.Theme(discussionView.Page); results.SelectedTab = discussionView.Page; }
            discussionView.Update(job);
        }
        foreach (DataGridViewRow row in grid.Rows) if (job.Results.FirstOrDefault(r => r.AgentId == config.Agents[row.Index].Id) is { } result) row.Cells["state"].Value = Ui.State(result.State);
        foreach (var result in job.Results)
        {
            if (!views.TryGetValue(result.AgentId, out var view)) { view = new ResultView(result.AgentId, Protect, Reply, OpenExternal, id => Protect(() => runner.ReportQuota(id)), DeviceDpi / 96f); views[result.AgentId] = view; results.TabPages.Add(view.Page); Ui.Theme(view.Page); }
            view.Update(result, busy, Ui.AgentState(job, result)); if (result.SentPrompt.Length == 0 && result.HandoffPrompt.Length == 0) view.SetPrompt(job.Prompt);
        }
        if (busy && job.Results.Any(r => r.State == "quota" && views[r.AgentId].Page == results.SelectedTab)) {
            var remaining = job.Results.FirstOrDefault(r => r.State is "waiting" or "running");
            if (remaining != null) results.SelectedTab = views[remaining.AgentId].Page;
        }
    }
    private void ShowJobStatus(JobRecord job)
    {
        sessionProgress.Update(job, runner.Busy);
        status.Text = quotaSaveError;
    }
    private void Reply(string agentId, string text) => Protect(() => runner.Reply(agentId, text));
    private void OpenExternal(string agentId, string handoff) => Protect(() => {
        var agent = config.Agents.First(a => a.Id == agentId); Clipboard.SetText(handoff);
        if (agent.Connection == "manual") { status.Text = "Đã sao chép prompt. Dán vào ứng dụng AI của bạn."; return; }
        string binary = ProcessTransport.Find(agent) ?? throw new FileNotFoundException("Chưa tìm thấy ứng dụng. Chọn executable trong Thiết lập; prompt đã được sao chép.");
        if (!binary.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Chọn executable .exe.");
        var start = new ProcessStartInfo(binary) { UseShellExecute = false, WorkingDirectory = config.Project };
        if (agent.Connection == "antigravity-ide")
        {
            string folder = Path.Combine(storage.DataDir, "handoffs"); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"native-{runner.Snapshot()!.Id}-{agentId}.txt"); File.WriteAllText(file, handoff);
            foreach (string arg in new[] { "--reuse-window", config.Project, "--goto", file }) start.ArgumentList.Add(arg);
        }
        Process.Start(start)?.Dispose(); status.Text = "Đã mở ứng dụng và sao chép prompt. Chọn model, gửi prompt rồi dán kết quả về Hub.";
    });
    private void LoadHistory()
    {
        try { string? selected = (history.SelectedItem as JobRecord)?.Id; history.Items.Clear(); history.Items.AddRange(storage.History().Cast<object>().ToArray()); history.SelectedItem = history.Items.Cast<JobRecord>().FirstOrDefault(j => j.Id == selected) ?? history.Items.Cast<JobRecord>().FirstOrDefault(); }
        catch (Exception e) { status.Text = "Không đọc được lịch sử: " + e.Message; }
    }
    private void ShowHistory() => Protect(() => {
        if (runner.Busy || history.SelectedItem is not JobRecord job) return;
        var info = storage.Conversations().FirstOrDefault(c => c.Id == job.ConversationId || job.ConversationId.Length == 0 && c.Id == job.Id);
        if (info != null) { OpenConversation(info); return; }
        CancelConversationLoad(); selectedConversationId = null; continueFrom = null; conversationJobs = []; ClearResults(); latest = null; displayed = job; RenderJob(job, false);
    });
    private void Reuse() => Protect(() => {
        if (runner.Busy) return; var job = displayed ?? history.SelectedItem as JobRecord; if (job == null) return;
        CancelConversationLoad();
        if (Directory.Exists(job.Project)) SetProject(job.Project); prompt.Text = job.Prompt; prompt.Focus();
        if (job.Mode == "discussion") { config.Discussion = RestoreDiscussionLimits(job.Discussion); mode.SelectedItem = mode.Items.Cast<Choice>().First(c => c.Id == "discussion"); Save(); UpdateSummary(); }
        status.Text = "Đã điền lại yêu cầu. Kiểm tra agent/model và file đính kèm trước khi chạy.";
    });
    private void ExportJob() => Protect(() => {
        var job = displayed ?? history.SelectedItem as JobRecord; if (job == null) return;
        using var dialog = new SaveFileDialog { Filter = "Markdown (*.md)|*.md", FileName = $"hub-{job.Timestamp:yyyyMMdd-HHmm}.md" };
        if (dialog.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(dialog.FileName, Export.Markdown(job)); status.Text = "Đã xuất kết quả: " + dialog.FileName; }
    });
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return; e.Cancel = true; closing = true; CancelConversationLoad(); refresh.Stop(); Enabled = false; runner.Cancel();
        if (runner.Completion != null) try { await runner.Completion; } catch (Exception) { }
        ApplyQuotaChanges(true);
        try { if (Directory.Exists(project.Text)) { config.Project = Path.GetFullPath(project.Text); Save(); } } catch (Exception) { }
        teamMenu.Dispose(); headerMenu.Dispose(); attachmentMenu.Dispose(); surface.Dispose(); mode.Dispose(); boardPanel.Dispose(); agentPanel.Dispose(); detailPanel.Dispose(); Close();
    }
}
internal sealed class ResultView
{
    public TabPage Page { get; }
    private readonly Label label = Ui.Label(""), hint = Ui.Label("Sao chép prompt → chọn model trong ứng dụng AI → dán câu trả lời về đây.");
    private readonly ConversationTranscript answer = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10), DetectUrls = false }, logs = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9), DetectUrls = false }, sent = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9), DetectUrls = false };
    private readonly TextBox reply = Ui.Text(true);
    private readonly TableLayoutPanel root = Ui.Table();
    private readonly Panel manual = new() { Dock = DockStyle.Fill };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage manualPage = new("Dán kết quả") { BackColor = Color.White, Padding = new Padding(8) };
    private readonly Button submit;
    private readonly Button openApp, copyPrompt, copyAnswer;
    private string handoff = "";
    private readonly float scale;
    public ResultView(string id, Action<Action> protect, Action<string, string> receive, Action<string, string> open, Action<string> quota, float scale)
    {
        this.scale = scale;
        Page = new TabPage { BackColor = Color.White, Padding = new Padding(10) }; Page.Controls.Add(root);
        var header = Ui.Flow();
        copyAnswer = Ui.Button("Sao chép kết quả", (_, _) => protect(() => { if (!string.IsNullOrEmpty(answer.Text)) Clipboard.SetText(answer.Text); }));
        copyPrompt = Ui.Button("Sao chép prompt", (_, _) => protect(() => { if (handoff.Length > 0) Clipboard.SetText(handoff); }));
        header.Controls.AddRange([label, copyAnswer, copyPrompt]);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.Controls.Add(header, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        foreach (var item in new[] { ("Câu trả lời", answer), ("Nhật ký", logs), ("Prompt gửi đi", sent) }) { var page = new TabPage(item.Item1) { Padding = new Padding(8), BackColor = Color.White }; page.Controls.Add(item.Item2); tabs.TabPages.Add(page); }
        hint.AutoSize = false; hint.Height = 28; hint.Dock = DockStyle.Top;
        reply.AccessibleName = "Câu trả lời từ ứng dụng AI"; reply.PlaceholderText = "Dán câu trả lời từ agent này…";
        var buttons = Ui.Flow(); buttons.AutoSize = false; buttons.Dock = DockStyle.Bottom; buttons.Height = 42;
        openApp = Ui.Button("Mở ứng dụng & sao chép", (_, _) => open(id, handoff)); buttons.Controls.Add(openApp);
        submit = Ui.Button("Nhận kết quả & tiếp tục", (_, _) => {
            if (string.IsNullOrWhiteSpace(reply.Text) || reply.Text.Length > 1000000) { protect(() => throw new ArgumentException("Câu trả lời cần có 1–1.000.000 ký tự.")); return; }
            receive(id, reply.Text);
        }, true); buttons.Controls.Add(submit); buttons.Controls.Add(Ui.Button("Hết quota → bỏ chọn", (_, _) => quota(id)));
        manual.Controls.Add(reply); manual.Controls.Add(buttons); manual.Controls.Add(hint); manualPage.Controls.Add(manual); tabs.TabPages.Add(manualPage);
        root.Scale(new SizeF(scale, scale));
        tabs.TabPages.Remove(manualPage);
    }
    public void SetPrompt(string value) { handoff = value; Replace(sent, value); copyPrompt.Enabled = value.Length > 0; }
    private static void Replace(ConversationTranscript control, string value) => control.SetPlainText(value);
    public void Update(AgentResult result, bool busy, string? state = null)
    {
        state ??= Ui.State(result.State); Page.Text = result.Name + " • " + state; label.Text = $"{Catalog.ProviderName(result.Provider.Length > 0 ? result.Provider : "custom")} • {(result.Model.Length > 0 ? result.Model : "Model theo ứng dụng / CLI")} • {state}";
        string value = result.Text + (result.Error.Length > 0 ? "\n\nLỗi: " + result.Error : ""); Replace(answer, value); Replace(logs, result.Logs); copyAnswer.Enabled = value.Length > 0;
        SetPrompt(result.SentPrompt.Length > 0 ? result.SentPrompt : result.HandoffPrompt);
        label.ForeColor = result.State == "error" ? Color.Firebrick : result.State == "quota" ? Color.DarkOrange : result.State == "success" ? Color.DarkGreen : Ui.Ink;
        bool waiting = busy && result.State == "waiting";
        if (waiting && !tabs.TabPages.Contains(manualPage)) { tabs.TabPages.Add(manualPage); tabs.SelectedTab = manualPage; }
        else if (!waiting && tabs.TabPages.Contains(manualPage)) { tabs.TabPages.Remove(manualPage); tabs.SelectedIndex = 0; }
        submit.Enabled = waiting;
        openApp.Visible = result.Connection is "antigravity-ide" or "antigravity-desktop";
    }
}
