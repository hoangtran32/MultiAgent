using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed class CodeWorkspaceView : UserControl
{
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage filesPage = new("File & editor"), diffPage = new("Git diff"), logsPage = new("Nhật ký lệnh / agent");
    private readonly TreeView files = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly TextBox editor = Ui.Text(true), diff = Ui.Text(true), logs = Ui.Text(true);
    private readonly Label pathLabel = Ui.Label("Chọn file trong project để xem hoặc chỉnh sửa.");
    private readonly Button save;
    private readonly Func<bool> busy;
    private readonly CancellationTokenSource lifetime = new();
    private string project = "";
    private string? pendingProject;
    private WorkspaceDocument? document;
    private JobRecord? job;
    private bool settingText, dirty;
    private int generation;
    private int fileLoadVersion;
    private bool lifetimeDisposed;
    public CodeWorkspaceView(TeamBoard board, Func<bool> busy)
    {
        this.busy = busy; Dock = DockStyle.Fill; Controls.Add(tabs);
        var work = new TabPage("Giải pháp & task"); work.Controls.Add(board); tabs.TabPages.Add(work);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new(1000, 600), SplitterDistance = 240, Panel1MinSize = 100, Panel2MinSize = 150 }; split.Panel1.Controls.Add(files);
        var content = Ui.Table(); content.RowStyles.Add(new(SizeType.AutoSize)); content.RowStyles.Add(new(SizeType.Percent, 100)); split.Panel2.Controls.Add(content);
        var actions = Ui.Flow(); save = Ui.Button("Lưu file", (_, _) => SaveFile());
        actions.Controls.AddRange([save, Ui.Button("Tải lại file", async (_, _) => { if (document != null && Discard()) await OpenFile(document.Path); }), Ui.Button("Làm mới cây file", async (_, _) => await ReloadFiles())]);
        actions.Controls.Add(Ui.Button("Đóng file", (_, _) => { if (!Discard()) return; document = null; dirty = false; settingText = true; editor.Clear(); settingText = false; if (pendingProject is { } next) { pendingProject = null; SetProject(next); } UpdateSave(); }));
        actions.Controls.Add(pathLabel); content.Controls.Add(actions, 0, 0); content.Controls.Add(editor, 0, 1);
        editor.Font = diff.Font = logs.Font = new Font("Consolas", 10); editor.WordWrap = diff.WordWrap = logs.WordWrap = false; editor.AcceptsTab = true; editor.ScrollBars = diff.ScrollBars = logs.ScrollBars = ScrollBars.Both;
        editor.TextChanged += (_, _) => { if (!settingText) dirty = true; UpdateSave(); };
        editor.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; SaveFile(); } };
        diff.ReadOnly = logs.ReadOnly = true; filesPage.Controls.Add(split); diffPage.Controls.Add(diff); logsPage.Controls.Add(logs);
        files.BeforeExpand += async (_, e) => { if (e.Node is { Tag: string relative } && e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag == null) await Fill(e.Node, relative); };
        files.NodeMouseDoubleClick += async (_, e) => { if (e.Node?.Tag is string path && File.Exists(System.IO.Path.Combine(project, path)) && Discard()) await OpenFile(path); };
        tabs.SelectedIndexChanged += async (_, _) => {
            if (IsDisposed || Disposing || lifetimeDisposed) return;
            if (tabs.SelectedTab == filesPage && files.Nodes.Count == 0) await ReloadFiles();
            if (tabs.SelectedTab == diffPage) await ShowDiff();
            if (tabs.SelectedTab == logsPage) ShowLogs();
        };
        Ui.Theme(this); UpdateSave();
    }
    public void SetProject(string path)
    {
        if (project == path) return;
        if (dirty) { pendingProject = path; pathLabel.Text = "Project đã đổi; còn bản nháp chưa lưu của " + document?.Path + ". Sao chép bản nháp rồi bấm Đóng file."; editor.ReadOnly = true; save.Enabled = false; return; }
        project = path; generation++; document = null; files.Nodes.Clear(); settingText = true; editor.Clear(); settingText = false;
        foreach (var page in new[] { filesPage, diffPage, logsPage }) { if (path.Length > 0 && !tabs.TabPages.Contains(page)) tabs.TabPages.Add(page); if (path.Length == 0) tabs.TabPages.Remove(page); }
        if (tabs.SelectedTab == filesPage) _ = ReloadFiles(); UpdateSave();
    }
    private bool Discard() => !dirty || MessageBox.Show(this, "Bỏ bản nháp chưa lưu để mở/tải lại file?", "File chưa lưu", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    private async Task ReloadFiles() { if (project.Length == 0) return; files.Nodes.Clear(); var root = new TreeNode(System.IO.Path.GetFileName(project)) { Tag = "." }; files.Nodes.Add(root); await Fill(root, "."); root.Expand(); }
    private async Task Fill(TreeNode node, string relative)
    {
        int revision = generation; string root = project;
        try {
            var entries = await Task.Run(() => Directory.EnumerateFileSystemEntries(WorkspaceDocuments.Resolve(root, relative)).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0 && !new[] { ".git", "node_modules", "bin", "obj", ".build", "app-bin", "release" }.Contains(System.IO.Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                .Take(1000).Select(p => (Path: System.IO.Path.GetRelativePath(root, p), Folder: Directory.Exists(p))).OrderByDescending(e => e.Folder).ThenBy(e => e.Path).ToArray(), lifetime.Token);
            if (IsDisposed || revision != generation || node.TreeView != files) return; node.Nodes.Clear();
            foreach (var entry in entries) { var child = new TreeNode(System.IO.Path.GetFileName(entry.Path)) { Tag = entry.Path }; if (entry.Folder) child.Nodes.Add(new TreeNode("…")); node.Nodes.Add(child); }
            if (entries.Length == 1000) node.Nodes.Add(new TreeNode("Danh sách giới hạn 1.000 mục; mở thư mục cụ thể."));
        } catch (Exception error) when (error is not OutOfMemoryException) { if (!IsDisposed && revision == generation) pathLabel.Text = error.Message; }
    }
    private async Task OpenFile(string relative)
    {
        if (pendingProject != null) { pathLabel.Text = "Đóng bản nháp của project trước để mở project mới."; return; }
        int revision = generation, request = ++fileLoadVersion; string root = project;
        try {
            var loaded = await Task.Run(() => WorkspaceDocuments.Read(root, relative), lifetime.Token);
            if (IsDisposed || revision != generation || request != fileLoadVersion) return; document = loaded; settingText = true; editor.Text = loaded.Text; settingText = false; dirty = false; pathLabel.Text = System.IO.Path.Combine(root, relative); editor.ReadOnly = busy(); UpdateSave();
        } catch (Exception error) when (error is not OutOfMemoryException) { if (!IsDisposed && revision == generation && request == fileLoadVersion) pathLabel.Text = error.Message; }
    }
    private void SaveFile()
    {
        if (busy() || pendingProject != null || document == null || !dirty) return;
        try { document = WorkspaceDocuments.Save(project, document, editor.Text); dirty = false; pathLabel.Text = "Đã lưu " + document.Path; UpdateSave(); }
        catch (Exception error) { pathLabel.Text = error.Message; }
    }
    private async Task ShowDiff()
    {
        if (document == null) { diff.Text = "Chọn file ở File & editor trước khi xem diff."; return; }
        int revision = generation; string relative = document.Path, root = project;
        try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20)); string value = await WorkspaceDocuments.Diff(root, relative, timeout.Token); if (!IsDisposed && revision == generation && document?.Path == relative) diff.Text = value; }
        catch (Exception error) { if (!IsDisposed) diff.Text = error.Message; }
    }
    private void UpdateSave() { save.Enabled = document != null && dirty && !busy() && pendingProject == null && !editor.ReadOnly; }
    public void UpdateJob(JobRecord value) { job = value; editor.ReadOnly = busy() || pendingProject != null; UpdateSave(); if (tabs.SelectedTab == logsPage) ShowLogs(); }
    private void ShowLogs()
    {
        string value = job == null ? "Nhật ký xuất hiện khi agent hoặc lệnh kiểm tra chạy." : string.Join("\r\n\r\n", job.Results.Where(r => r.Logs.Length > 0).Select(r => r.Name + " / " + r.Model + "\r\n" + r.Logs)) + "\r\n" + string.Join("\r\n\r\n", job.Team?.Evidence.Select(e => e.Name + " · " + e.State + "\r\n" + e.Log) ?? []);
        value = value.Length > 64000 ? "[…nhật ký cũ đã rút gọn]\r\n" + value[^64000..] : value; if (logs.Text != value) logs.Text = value;
    }
    protected override void Dispose(bool disposing) { if (disposing && !lifetimeDisposed) { lifetimeDisposed = true; lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
