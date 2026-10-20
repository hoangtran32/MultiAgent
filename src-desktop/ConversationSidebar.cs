using MultiAgentHub.Core;
namespace MultiAgentHubApp;

internal sealed partial class ConversationSidebar : UserControl
{
    private readonly SidebarTree tree = new() { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Ui.Sidebar, ShowNodeToolTips = true, FullRowSelect = true, ItemHeight = 32, ShowLines = false, ShowPlusMinus = false, ShowRootLines = false, DrawMode = TreeViewDrawMode.OwnerDrawAll, AccessibleName = "Project và cuộc trò chuyện" };
    private readonly TextBox search = Ui.Text();
    private readonly ContextMenuStrip topicMenu = new() { BackColor = Ui.Surface, ForeColor = Ui.Ink };
    private readonly SidebarHeader surfaceHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TableLayoutPanel root = Ui.Table();
    private readonly TableLayoutPanel projectsHeader = Ui.Table(2);
    private readonly CheckBox archives = new() { Text = "Hội thoại đã lưu trữ", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(4, 10, 4, 10) };
    private List<ConversationInfo> conversations = [];
    private List<ProjectInfo> projects = [];
    private List<string> projectSections = [];
    private readonly Dictionary<string, List<TreeNode>> conversationNodes = [];
    private string currentProject = "", selectedId = "";
    private bool updating;
    private bool chatSurface;
    private readonly List<Control> navigation = [];
    private TreeNode? hovered;
    public event Action<string>? ProjectSelected;
    public event Action<ConversationInfo>? ConversationSelected;
    public event Action? NewRequested, AddProjectRequested;
    public event Action<ConversationInfo>? ContinueInfoRequested, OpenTextInfoRequested;
    public event Action? SettingsRequested;
    public event Action<string, string?, bool?, bool?>? ChangeConversation;
    public ConversationSidebar()
    {
        Dock = DockStyle.Fill; BackColor = Ui.Sidebar; Padding = new Padding(8, 12, 8, 10);
        DoubleBuffered = true; Controls.Add(root);
        root.RowCount = 8;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        for (int i = 0; i < 3; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        surfaceHost.SetSearchButton(Ui.IconButton("search", "Tìm hội thoại", (_, _) => FocusSearch()));
        root.Controls.Add(surfaceHost, 0, 0);
        var create = Ui.Ghost("Đoạn chat mới", (_, _) => NewRequested?.Invoke()); ((ModernButton)create).IconKind = "edit"; create.TextAlign = ContentAlignment.MiddleLeft; create.Dock = DockStyle.Fill; create.Height = 36; create.MinimumSize = new Size(0, 36); create.Margin = new Padding(0, 8, 0, 12); root.Controls.Add(create, 0, 1);
        search.PlaceholderText = "Tìm cuộc trò chuyện…"; search.AccessibleName = "Tìm cuộc trò chuyện"; search.TextChanged += (_, _) => Populate(); search.Visible = false;
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { search.Clear(); search.Visible = false; tree.Focus(); e.SuppressKeyPress = true; } }; root.Controls.Add(search, 0, 2);
        projectsHeader.Height = 32; projectsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); projectsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        projectsHeader.Controls.Add(Ui.Label("Dự án"), 0, 0); projectsHeader.Controls.Add(Ui.IconButton("plus", "Thêm project", (_, _) => AddProjectRequested?.Invoke()), 1, 0); root.Controls.Add(projectsHeader, 0, 3);
        root.Controls.Add(tree, 0, 4); archives.CheckedChanged += (_, _) => Populate();
        var archiveButton = Ui.Ghost("Hội thoại đã lưu trữ", (_, _) => { archives.Checked = !archives.Checked; }); archiveButton.TextAlign = ContentAlignment.MiddleLeft; archiveButton.Dock = DockStyle.Fill; archiveButton.Font = new Font("Segoe UI", 8.5f); archiveButton.ForeColor = Ui.Muted; root.Controls.Add(archiveButton, 0, 6);
        archives.CheckedChanged += (_, _) => archiveButton.Text = archives.Checked ? "← Quay lại hội thoại" : "Hội thoại đã lưu trữ";
        var footer = Ui.Ghost("Agent & model", (_, _) => SettingsRequested?.Invoke()); ((ModernButton)footer).IconKind = "settings"; footer.TextAlign = ContentAlignment.MiddleLeft; footer.Dock = DockStyle.Fill; root.Controls.Add(footer, 0, 7);
        navigation.AddRange([surfaceHost, create, search, projectsHeader, tree, archiveButton, footer]);
        tree.HandleCreated += (_, _) => { tree.ItemHeight = (int)(32 * tree.DeviceDpi / 96f); tree.Indent = (int)(18 * tree.DeviceDpi / 96f); };
        tree.DrawNode += DrawNode;
        tree.MouseMove += (_, e) => { var next = tree.GetNodeAt(e.Location); if (next != hovered) { var previous = hovered; hovered = next; tree.InvalidateRow(previous); tree.InvalidateRow(next); } };
        tree.MouseLeave += (_, _) => { var previous = hovered; hovered = null; tree.InvalidateRow(previous); };
        var menu = topicMenu;
        menu.Renderer = new ToolStripProfessionalRenderer(new LightMenuColors()); menu.ShowImageMargin = false; menu.BackColor = Color.White; menu.Font = new Font("Segoe UI", 9);
        Disposed += (_, _) => { menu.Dispose(); projectMenu.Dispose(); foreach (var icon in projectIcons.Values) icon.Dispose(); };
        tree.BeforeSelect += (_, e) => { if (Control.MouseButtons == MouseButtons.Right) e.Cancel = true; };
        tree.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowNodeMenu(tree.GetNodeAt(e.Location), e.Location); };
        tree.KeyDown += (_, e) => { if (e.KeyCode == Keys.Apps || e.Shift && e.KeyCode == Keys.F10) { ShowNodeMenu(tree.SelectedNode, tree.SelectedNode?.Bounds.Location ?? Point.Empty); e.SuppressKeyPress = true; } };
        menu.Opening += (_, e) => {
            menu.Items.Clear(); if (menu.Tag is not ConversationInfo info) { e.Cancel = true; return; }
            e.Cancel = false;
            menu.Items.Add("Tiếp tục hội thoại", null, (_, _) => ContinueInfoRequested?.Invoke(info));
            menu.Items.Add(info.Pinned ? "Bỏ ghim" : "Ghim hội thoại", null, (_, _) => ChangeConversation?.Invoke(info.Id, null, !info.Pinned, null));
            menu.Items.Add("Đổi tên…", null, (_, _) => Rename(info));
            menu.Items.Add(info.Archived ? "Khôi phục hội thoại" : "Lưu trữ hội thoại", null, (_, _) => ChangeConversation?.Invoke(info.Id, null, null, !info.Archived));
            menu.Items.Add("Mở bản .txt", null, (_, _) => OpenTextInfoRequested?.Invoke(info));
        };
        tree.AfterSelect += (_, e) => {
            if (updating) return;
            if (e.Node?.Tag is ConversationInfo info) { selectedId = info.Id; ConversationSelected?.Invoke(info); }
            else if (e.Node?.Tag is ProjectInfo project) ProjectSelected?.Invoke(project.Path);
        };
    }
    public void SetProgressControl(Control control) => root.Controls.Add(control, 0, 5);
    public void SetRunning(bool running) { foreach (var control in navigation) if (control.Enabled == running) control.Enabled = !running; }
    public void SetSurfaceControl(Control control) {
        if (control is Button button) { button.AutoSize = false; button.TextAlign = ContentAlignment.MiddleLeft; }
        control.Font = new Font("Segoe UI", 11, FontStyle.Bold); surfaceHost.SetChoice(control); PerformLayout();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (root != null && root.RowStyles.Count > 0) {
            float height = 48 * DeviceDpi / 96f;
            if (Math.Abs(root.RowStyles[0].Height - height) > 0.5f) root.RowStyles[0].Height = height;
        }
        base.OnLayout(e);
    }
    public void FocusSearch() { search.Visible = true; search.Focus(); search.SelectAll(); }
    public void SelectConversation(string id, string? projectPath = null)
    {
        string previous = selectedId; selectedId = id; if (projectPath != null) currentProject = projectPath;
        updating = true;
        try {
            if (conversationNodes.TryGetValue(id, out var nodes) && !(tree.SelectedNode?.Tag is ConversationInfo current && current.Id == id)) {
                var node = nodes[0]; for (var parent = node.Parent; parent != null; parent = parent.Parent) parent.Expand(); tree.SelectedNode = node;
            }
            foreach (string key in new[] { previous, id }) if (conversationNodes.TryGetValue(key, out var rows)) foreach (var row in rows) tree.InvalidateRow(row);
        } finally { updating = false; }
    }
    public void ShowTopicMenu(Control anchor, string? id = null)
    {
        var info = conversations.FirstOrDefault(c => c.Id == (id ?? selectedId));
        if (info == null) return;
        topicMenu.Tag = info; topicMenu.Show(anchor, new Point(0, anchor.Height));
    }
    public void ApplyTheme() { BackColor = Ui.Sidebar; SetBackground(this); }
    private static void SetBackground(Control root) { foreach (Control child in root.Controls) { if (child is Panel or Label or CheckBox) child.BackColor = Ui.Sidebar; SetBackground(child); } }
    private void DrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (e.Node == null) return;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float scale = tree.DeviceDpi / 96f; int D(int value) => (int)(value * scale);
        bool section = e.Node.Tag is string tag && tag.StartsWith("__");
        bool selected = e.Node.Tag is ConversationInfo info && info.Id == selectedId;
        var row = new Rectangle(0, e.Bounds.Top, tree.ClientSize.Width - D(2), tree.ItemHeight);
        using var background = new SolidBrush(Ui.Sidebar); e.Graphics.FillRectangle(background, row);
        if (selected || hovered == e.Node && !section) {
            using var path = Shapes.Rounded(new RectangleF(D(1), row.Top + D(2), row.Width - D(2), row.Height - D(4)), D(7));
            using var brush = new SolidBrush(Ui.Selection); e.Graphics.FillPath(brush, path);
        }
        int x = D(7 + e.Node.Level * 18);
        bool project = e.Node.Tag is ProjectInfo;
        if (project) { Shapes.Icon(e.Graphics, "folder", new RectangleF(x, row.Top + D(9), D(15), D(15)), Ui.Ink); x += D(23); }
        bool pinned = e.Node.Tag is ProjectInfo { Pinned: true };
        if (pinned) Shapes.Icon(e.Graphics, "pin", new RectangleF(row.Right - D(23), row.Top + D(9), D(14), D(14)), Ui.Muted);
        var textBounds = new Rectangle(x, row.Top, Math.Max(0, row.Width - x - D(pinned ? 28 : 8)), row.Height);
        TextRenderer.DrawText(e.Graphics, e.Node.Text, tree.Font, textBounds, section ? Ui.Muted : Ui.Ink, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
    private void Rename(ConversationInfo info)
    {
        using var dialog = new Form { Text = "Đổi tên hội thoại", StartPosition = FormStartPosition.CenterParent, Size = new Size(500, 190), Font = Font, MinimizeBox = false, MaximizeBox = false, AutoScaleMode = AutoScaleMode.None };
        var root = Ui.Table(); root.Padding = new Padding(16); var input = Ui.Text(); input.Text = info.Title; input.MaxLength = 100;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(Ui.Label("Tên hội thoại"), 0, 0); root.Controls.Add(input, 0, 1);
        var save = Ui.Button("Lưu", (_, _) => { if (string.IsNullOrWhiteSpace(input.Text)) return; ChangeConversation?.Invoke(info.Id, input.Text.Trim(), null, null); dialog.DialogResult = DialogResult.OK; }, true);
        root.Controls.Add(save, 0, 2); dialog.Controls.Add(root); dialog.AcceptButton = save; Ui.Theme(dialog);
        float scale = DeviceDpi / 96f; root.Scale(new SizeF(scale, scale)); dialog.Size = new Size((int)(500*scale), (int)(190*scale)); dialog.ShowDialog(FindForm());
    }
    public void Reload(List<ProjectInfo> savedProjects, List<string> sections, List<ConversationInfo> savedConversations, string activeProject, string? conversationId, string surface)
    {
        chatSurface = surface == "chat"; projectsHeader.Visible = !chatSurface;
        conversations = savedConversations.Where(c => (c.Surface == "chat") == chatSurface).ToList();
        projects = chatSurface ? [] : savedProjects; projectSections = chatSurface ? [] : sections;
        currentProject = activeProject; selectedId = conversationId ?? ""; Populate();
    }
    private void Populate()
    {
        var expanded = AllNodes(tree.Nodes).Where(n => n.IsExpanded).Select(NodeKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string topKey = tree.TopNode != null ? NodeKey(tree.TopNode) : "";
        updating = true; hovered = null; tree.BeginUpdate(); tree.Nodes.Clear(); conversationNodes.Clear();
        try {
        string queryText = search.Text.Trim();
        var removed = projects.Where(p => p.Removed).ToList();
        var visible = conversations.Where(c => c.Archived == archives.Checked && !removed.Any(p => ProjectCatalog.SamePath(p.Path, c.Project))).ToList();
        bool Matches(ConversationInfo info) => queryText.Length == 0 || info.Title.Contains(queryText, StringComparison.OrdinalIgnoreCase) || info.Project.Contains(queryText, StringComparison.OrdinalIgnoreCase) || projects.Any(p => ProjectCatalog.SamePath(p.Path, info.Project) && (p.DisplayName.Contains(queryText, StringComparison.OrdinalIgnoreCase) || p.Section.Contains(queryText, StringComparison.OrdinalIgnoreCase)));
        void AddConversation(TreeNode parent, ConversationInfo info) {
            string caption = (info.Pinned ? "• " : "") + info.Title;
            var node = new TreeNode(caption) { Tag = info, ToolTipText = info.Title + "\n" + info.Project + "\n" + info.CreatedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm") };
            parent.Nodes.Add(node); if (!conversationNodes.TryGetValue(info.Id, out var nodes)) conversationNodes[info.Id] = nodes = []; nodes.Add(node);
            if (info.Id == selectedId && tree.SelectedNode == null) { tree.SelectedNode = node; for (var group = parent; group != null; group = group.Parent) group.Expand(); }
        }
        var pinned = visible.Where(c => c.Pinned && Matches(c)).ToList();
        if (pinned.Count > 0) { var root = new TreeNode("Đã ghim") { Tag = "__pinned" }; tree.Nodes.Add(root); foreach (var info in pinned) AddConversation(root, info); root.Expand(); }
        void AddProject(ProjectInfo project, TreeNodeCollection destination) {
            var items = visible.Where(c => !c.Pinned && ProjectCatalog.SamePath(c.Project, project.Path));
            bool projectMatches = queryText.Length == 0 || project.Path.Contains(queryText, StringComparison.OrdinalIgnoreCase) || project.DisplayName.Contains(queryText, StringComparison.OrdinalIgnoreCase) || project.Section.Contains(queryText, StringComparison.OrdinalIgnoreCase);
            var matches = (projectMatches ? items : items.Where(c => c.Title.Contains(queryText, StringComparison.OrdinalIgnoreCase))).ToList();
            if (!projectMatches && matches.Count == 0) return;
            var node = new TreeNode(project.DisplayName) { Tag = project, ToolTipText = project.Path };
            destination.Add(node); foreach (var info in matches) AddConversation(node, info);
            if (expanded.Contains(NodeKey(node)) || string.Equals(currentProject, project.Path, StringComparison.OrdinalIgnoreCase) || queryText.Length > 0) node.Expand();
        }
        var available = projects.Where(p => !p.Removed).ToList();
        var pinnedProjects = available.Where(p => p.Pinned).ToList();
        if (pinnedProjects.Count > 0) { var group = new TreeNode("Dự án đã ghim") { Tag = "__projects-pinned" }; foreach (var item in pinnedProjects) AddProject(item, group.Nodes); if (group.Nodes.Count > 0) { tree.Nodes.Add(group); group.Expand(); } }
        foreach (var item in available.Where(p => !p.Pinned && p.Section.Length == 0)) AddProject(item, tree.Nodes);
        foreach (string section in projectSections.Concat(available.Select(p => p.Section)).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)) {
            var group = new TreeNode(section) { Tag = "__section:" + section };
            foreach (var item in available.Where(p => !p.Pinned && string.Equals(p.Section, section, StringComparison.OrdinalIgnoreCase))) AddProject(item, group.Nodes);
            if (group.Nodes.Count > 0) { tree.Nodes.Add(group); group.Expand(); }
        }
        var recent = visible.Where(Matches).OrderByDescending(c => c.UpdatedAt).Take(chatSurface ? int.MaxValue : 8).ToList();
        if (recent.Count > 0) { var group = new TreeNode(chatSurface ? "Cuộc trò chuyện" : "Gần đây") { Tag = "__recent" }; tree.Nodes.Add(group); foreach (var info in recent) AddConversation(group, info); group.Expand(); }
        if (AllNodes(tree.Nodes).FirstOrDefault(n => NodeKey(n) == topKey) is { } top) tree.TopNode = top;
        } finally { tree.EndUpdate(); updating = false; }
    }
    private static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes) { foreach (TreeNode node in nodes) { yield return node; foreach (var child in AllNodes(node.Nodes)) yield return child; } }
    private static string NodeKey(TreeNode node) => node.Tag switch { ProjectInfo p => "project:" + p.Path, ConversationInfo c => "chat:" + c.Id, string section => section, _ => "" };
}

internal sealed class ConversationView
{
    public TabPage Page { get; } = new("Cuộc hội thoại") { BackColor = Color.White, Padding = new Padding(10) };
    private readonly ConversationTranscript text = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Segoe UI", 10), BorderStyle = BorderStyle.None, DetectUrls = false, AccessibleName = "Nội dung cuộc hội thoại" };
    public ConversationView() => Page.Controls.Add(text);
    public void Update(IEnumerable<JobRecord> jobs)
    {
        string value = string.Join("\n\n────────────────────────────────\n\n", jobs.Select(Export.PlainText)); if (text.Text == value) return;
        text.SetPlainText(value);
    }
}
