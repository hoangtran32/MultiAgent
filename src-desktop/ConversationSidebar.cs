using MultiAgentHub.Core;
namespace MultiAgentHubApp;

internal sealed class ConversationSidebar : UserControl
{
    private readonly SidebarTree tree = new() { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Ui.Sidebar, ShowNodeToolTips = true, FullRowSelect = true, ItemHeight = 32, ShowLines = false, ShowPlusMinus = false, ShowRootLines = false, DrawMode = TreeViewDrawMode.OwnerDrawAll, AccessibleName = "Project và cuộc trò chuyện" };
    private readonly TextBox search = Ui.Text();
    private readonly ContextMenuStrip topicMenu = new() { BackColor = Ui.Surface, ForeColor = Ui.Ink };
    private readonly SidebarHeader surfaceHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TableLayoutPanel root = Ui.Table();
    private readonly CheckBox archives = new() { Text = "Hội thoại đã lưu trữ", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(4, 10, 4, 10) };
    private List<ConversationInfo> conversations = [];
    private List<string> projects = [];
    private string currentProject = "", selectedId = "";
    private bool updating;
    private TreeNode? hovered;
    public event Action<string>? ProjectSelected;
    public event Action<ConversationInfo>? ConversationSelected;
    public event Action? NewRequested, AddProjectRequested, ContinueRequested, OpenTextRequested;
    public event Action? SettingsRequested;
    public event Action<string, string?, bool?, bool?>? ChangeConversation;
    public ConversationSidebar()
    {
        Dock = DockStyle.Fill; BackColor = Ui.Sidebar; Padding = new Padding(8, 12, 8, 10);
        DoubleBuffered = true; Controls.Add(root);
        root.RowCount = 7;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        for (int i = 0; i < 3; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        surfaceHost.SetSearchButton(Ui.IconButton("search", "Tìm hội thoại", (_, _) => FocusSearch()));
        root.Controls.Add(surfaceHost, 0, 0);
        var create = Ui.Ghost("Đoạn chat mới", (_, _) => NewRequested?.Invoke()); ((ModernButton)create).IconKind = "edit"; create.TextAlign = ContentAlignment.MiddleLeft; create.Dock = DockStyle.Fill; create.Height = 36; create.MinimumSize = new Size(0, 36); create.Margin = new Padding(0, 8, 0, 12); root.Controls.Add(create, 0, 1);
        search.PlaceholderText = "Tìm cuộc trò chuyện…"; search.AccessibleName = "Tìm cuộc trò chuyện"; search.TextChanged += (_, _) => Populate(); search.Visible = false;
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { search.Clear(); search.Visible = false; tree.Focus(); e.SuppressKeyPress = true; } }; root.Controls.Add(search, 0, 2);
        var projectsHeader = Ui.Table(2); projectsHeader.Height = 32; projectsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); projectsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        projectsHeader.Controls.Add(Ui.Label("Dự án"), 0, 0); projectsHeader.Controls.Add(Ui.IconButton("plus", "Thêm project", (_, _) => AddProjectRequested?.Invoke()), 1, 0); root.Controls.Add(projectsHeader, 0, 3);
        root.Controls.Add(tree, 0, 4); archives.CheckedChanged += (_, _) => Populate();
        var archiveButton = Ui.Ghost("Hội thoại đã lưu trữ", (_, _) => { archives.Checked = !archives.Checked; }); archiveButton.TextAlign = ContentAlignment.MiddleLeft; archiveButton.Dock = DockStyle.Fill; archiveButton.Font = new Font("Segoe UI", 8.5f); archiveButton.ForeColor = Ui.Muted; root.Controls.Add(archiveButton, 0, 5);
        archives.CheckedChanged += (_, _) => archiveButton.Text = archives.Checked ? "← Quay lại hội thoại" : "Hội thoại đã lưu trữ";
        var footer = Ui.Ghost("Agent & model", (_, _) => SettingsRequested?.Invoke()); ((ModernButton)footer).IconKind = "settings"; footer.TextAlign = ContentAlignment.MiddleLeft; footer.Dock = DockStyle.Fill; root.Controls.Add(footer, 0, 6);
        tree.HandleCreated += (_, _) => { tree.ItemHeight = (int)(32 * tree.DeviceDpi / 96f); tree.Indent = (int)(18 * tree.DeviceDpi / 96f); };
        tree.DrawNode += DrawNode;
        tree.MouseMove += (_, e) => { var next = tree.GetNodeAt(e.Location); if (next != hovered) { var previous = hovered; hovered = next; tree.InvalidateRow(previous); tree.InvalidateRow(next); } };
        tree.MouseLeave += (_, _) => { var previous = hovered; hovered = null; tree.InvalidateRow(previous); };
        var menu = topicMenu;
        menu.Renderer = new ToolStripProfessionalRenderer(new LightMenuColors()); menu.ShowImageMargin = false; menu.BackColor = Color.White; menu.Font = new Font("Segoe UI", 9);
        Disposed += (_, _) => menu.Dispose();
        tree.BeforeSelect += (_, e) => { if (Control.MouseButtons == MouseButtons.Right) e.Cancel = true; };
        tree.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right && tree.GetNodeAt(e.Location)?.Tag is ConversationInfo info) { menu.Tag = info; menu.Show(tree, e.Location); } };
        tree.KeyDown += (_, e) => { if ((e.KeyCode == Keys.Apps || e.Shift && e.KeyCode == Keys.F10) && tree.SelectedNode?.Tag is ConversationInfo info) { menu.Tag = info; menu.Show(tree, tree.SelectedNode.Bounds.Location); e.SuppressKeyPress = true; } };
        menu.Opening += (_, e) => {
            menu.Items.Clear(); if (menu.Tag is not ConversationInfo info) { e.Cancel = true; return; }
            e.Cancel = false;
            menu.Items.Add("Tiếp tục hội thoại", null, (_, _) => { ConversationSelected?.Invoke(info); ContinueRequested?.Invoke(); });
            menu.Items.Add(info.Pinned ? "Bỏ ghim" : "Ghim hội thoại", null, (_, _) => ChangeConversation?.Invoke(info.Id, null, !info.Pinned, null));
            menu.Items.Add("Đổi tên…", null, (_, _) => Rename(info));
            menu.Items.Add(info.Archived ? "Khôi phục hội thoại" : "Lưu trữ hội thoại", null, (_, _) => ChangeConversation?.Invoke(info.Id, null, null, !info.Archived));
            menu.Items.Add("Mở bản .txt", null, (_, _) => { ConversationSelected?.Invoke(info); OpenTextRequested?.Invoke(); });
        };
        tree.AfterSelect += (_, e) => {
            if (updating) return;
            if (e.Node?.Tag is ConversationInfo info) { selectedId = info.Id; ConversationSelected?.Invoke(info); }
            else if (e.Node?.Tag is string project && !project.StartsWith("__")) { selectedId = ""; ProjectSelected?.Invoke(project); }
        };
    }
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
    public void SelectConversation(string id)
    {
        selectedId = id; updating = true;
        foreach (TreeNode root in tree.Nodes) foreach (TreeNode node in root.Nodes)
            if (node.Tag is ConversationInfo info && info.Id == id) { root.Expand(); tree.SelectedNode = node; }
        updating = false;
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
        int x = e.Node.Level == 0 ? D(7) : D(26);
        if (e.Node.Level == 0 && !section) { Shapes.Icon(e.Graphics, "folder", new RectangleF(x, row.Top + D(9), D(15), D(15)), Ui.Ink); x += D(23); }
        var textBounds = new Rectangle(x, row.Top, Math.Max(0, row.Width - x - D(8)), row.Height);
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
    public void Reload(List<string> savedProjects, List<ConversationInfo> savedConversations, string activeProject, string? conversationId)
    {
        conversations = savedConversations; projects = savedProjects.Concat(conversations.Select(c => c.Project)).Append(activeProject).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        currentProject = activeProject; selectedId = conversationId ?? ""; Populate();
    }
    private void Populate()
    {
        var expanded = tree.Nodes.Cast<TreeNode>().Where(n => n.IsExpanded).Select(n => (string)n.Tag!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        updating = true; hovered = null; tree.BeginUpdate(); tree.Nodes.Clear();
        string queryText = search.Text.Trim();
        var visible = conversations.Where(c => c.Archived == archives.Checked).ToList();
        void AddConversation(TreeNode parent, ConversationInfo info) {
            string caption = (info.Pinned ? "• " : "") + info.Title;
            var node = new TreeNode(caption) { Tag = info, ToolTipText = info.Title + "\n" + info.Project + "\n" + info.CreatedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm") };
            parent.Nodes.Add(node); if (info.Id == selectedId) { tree.SelectedNode = node; parent.Expand(); }
        }
        var pinned = visible.Where(c => c.Pinned && (queryText.Length == 0 || c.Title.Contains(queryText, StringComparison.OrdinalIgnoreCase) || c.Project.Contains(queryText, StringComparison.OrdinalIgnoreCase))).ToList();
        if (pinned.Count > 0) { var root = new TreeNode("Đã ghim") { Tag = "__pinned" }; tree.Nodes.Add(root); foreach (var info in pinned) AddConversation(root, info); root.Expand(); }
        foreach (string project in projects) {
            var items = visible.Where(c => !c.Pinned && string.Equals(c.Project, project, StringComparison.OrdinalIgnoreCase));
            string query = search.Text.Trim(); if (query.Length > 0 && !project.Contains(query, StringComparison.OrdinalIgnoreCase)) items = items.Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
            var matches = items.ToList(); if (query.Length > 0 && matches.Count == 0 && !project.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            string name = Path.GetFileName(project.TrimEnd(Path.DirectorySeparatorChar)); if (name.Length == 0) name = project;
            var root = new TreeNode(name) { Tag = project, ToolTipText = project, ForeColor = Ui.Muted };
            tree.Nodes.Add(root);
            foreach (var info in matches) {
                AddConversation(root, info);
            }
            if (expanded.Contains(project) || string.Equals(currentProject, project, StringComparison.OrdinalIgnoreCase) || query.Length > 0) root.Expand();
        }
        var recent = visible.Where(c => queryText.Length == 0 || c.Title.Contains(queryText, StringComparison.OrdinalIgnoreCase)).OrderByDescending(c => c.UpdatedAt).Take(8).ToList();
        if (recent.Count > 0) { var group = new TreeNode("Gần đây") { Tag = "__recent" }; tree.Nodes.Add(group); foreach (var info in recent) AddConversation(group, info); group.Expand(); }
        tree.EndUpdate(); updating = false;
    }
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
