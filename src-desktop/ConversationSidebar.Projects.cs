using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed partial class ConversationSidebar
{
    private readonly ContextMenuStrip projectMenu = new() { Renderer = new ToolStripProfessionalRenderer(new LightMenuColors()), ShowImageMargin = true, BackColor = Color.White, Font = new Font("Segoe UI", 9.5f) };
    private readonly Dictionary<string, Bitmap> projectIcons = [];
    public event Action<string, ProjectChange>? ChangeProject;
    public event Action<string, bool>? ArchiveProjectRequested;
    public event Action<string>? ExploreProjectRequested;
    private Bitmap ProjectIcon(string kind)
    {
        if (projectIcons.TryGetValue(kind, out var cached)) return cached;
        int size = (int)Math.Round(20 * DeviceDpi / 96f); var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap)) { graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; Shapes.Icon(graphics, kind, new RectangleF(1, 1, size - 2, size - 2), Ui.Ink); }
        return projectIcons[kind] = bitmap;
    }
    private void ShowNodeMenu(TreeNode? node, Point location)
    {
        if (node?.Tag is ConversationInfo info) { topicMenu.Tag = info; topicMenu.Show(tree, location); return; }
        if (node?.Tag is not ProjectInfo project) return;
        projectMenu.Items.Clear(); projectMenu.ImageScalingSize = new Size((int)(20 * DeviceDpi / 96f), (int)(20 * DeviceDpi / 96f));
        ToolStripMenuItem Item(string text, string icon, Action action) => new(text, ProjectIcon(icon), (_, _) => action()) { Padding = new Padding(6, 7, 12, 7) };
        projectMenu.Items.Add(Item(project.Pinned ? "Bỏ ghim" : "Ghim", "pin", () => ChangeProject?.Invoke(project.Path, new(Pinned: !project.Pinned))));
        projectMenu.Items.Add(Item("Chỉnh sửa…", "settings", () => EditProject(project)));
        projectMenu.Items.Add(new ToolStripSeparator());
        var section = Item("Mục", "tasks", () => { });
        var standard = new ToolStripMenuItem("Dự án") { Checked = project.Section.Length == 0 }; standard.Click += (_, _) => ChangeProject?.Invoke(project.Path, new(Section: "")); section.DropDownItems.Add(standard);
        foreach (string name in projectSections.Concat(projects.Select(p => p.Section)).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)) {
            var entry = new ToolStripMenuItem(name) { Checked = string.Equals(name, project.Section, StringComparison.OrdinalIgnoreCase) }; entry.Click += (_, _) => ChangeProject?.Invoke(project.Path, new(Section: name)); section.DropDownItems.Add(entry);
        }
        section.DropDownItems.Add(new ToolStripSeparator()); section.DropDownItems.Add("Mục mới…", null, (_, _) => {
            string? value = AskProjectText("Tạo mục", "Tên mục", "", 60); if (!string.IsNullOrWhiteSpace(value)) ChangeProject?.Invoke(project.Path, new(Section: value));
        });
        projectMenu.Items.Add(section);
        projectMenu.Items.Add(Item("Mở trong File Explorer", "folder", () => ExploreProjectRequested?.Invoke(project.Path)));
        projectMenu.Items.Add(new ToolStripSeparator());
        projectMenu.Items.Add(Item(archives.Checked ? "Khôi phục các cuộc trò chuyện" : "Lưu trữ các cuộc trò chuyện", "archive", () => ArchiveProjectRequested?.Invoke(project.Path, !archives.Checked)));
        projectMenu.Items.Add(new ToolStripSeparator());
        projectMenu.Items.Add(Item("Gỡ dự án", "close", () => ChangeProject?.Invoke(project.Path, new(Removed: true))));
        projectMenu.Show(tree, location);
    }
    private void EditProject(ProjectInfo project)
    {
        string? name = AskProjectText("Chỉnh sửa project", "Tên hiển thị • giữ nguyên thư mục trên ổ đĩa", project.DisplayName, 100, project.Path);
        if (name != null) ChangeProject?.Invoke(project.Path, new(Name: name));
    }
    private string? AskProjectText(string caption, string label, string value, int maximum, string? path = null)
    {
        using var dialog = new Form { Text = caption, StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.None, Font = Font, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, Icon = Ui.AppIcon };
        var layout = Ui.Table(); layout.Padding = new Padding(16); layout.RowCount = 4;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var input = Ui.Text(); input.Text = value; input.MaxLength = maximum;
        layout.Controls.Add(Ui.Label(label), 0, 0); layout.Controls.Add(input, 0, 1);
        var note = Ui.Label(path ?? "Mục chỉ tổ chức project trong Hub."); note.MaximumSize = new Size(460, 0); layout.Controls.Add(note, 0, 2);
        var buttons = Ui.Flow(); var save = Ui.Button("Lưu", (_, _) => { if (input.Text.Trim().Length > 0) dialog.DialogResult = DialogResult.OK; }, true); var cancel = Ui.Button("Hủy", (_, _) => dialog.DialogResult = DialogResult.Cancel);
        buttons.Controls.AddRange([save, cancel]); layout.Controls.Add(buttons, 0, 3); dialog.Controls.Add(layout); dialog.AcceptButton = save; dialog.CancelButton = cancel;
        float scale = DeviceDpi / 96f; layout.Scale(new SizeF(scale, scale)); dialog.Size = new Size((int)(530 * scale), (int)(260 * scale)); Ui.Theme(dialog);
        dialog.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog(FindForm()) == DialogResult.OK ? input.Text.Trim() : null;
    }
}
