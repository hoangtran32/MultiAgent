using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed partial class MainWindow
{
    private void UpdateProject(string path, ProjectChange change) => Protect(() => {
        if (runner.Busy || projectMutation) return;
        if (change.Removed == true) { CancelConversationLoad(); run.Enabled = true; }
        var next = CopyConfig(); ProjectCatalog.Change(next, path, change);
        bool reset = change.Removed == true && ProjectCatalog.SamePath(config.Project, path);
        if (reset) { next.Project = next.Projects.FirstOrDefault(p => !p.Removed && Directory.Exists(p.Path))?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); next.LastConversationId = ""; }
        Commit(next); project.Text = config.Project; LoadProjects();
        if (reset) NewConversation(); else { ReloadLibrary(); UpdateSummary(); }
        status.Text = change.Removed == true ? "Đã gỡ khỏi thư viện Hub. Thư mục và hội thoại vẫn giữ nguyên; thêm lại cùng đường dẫn để khôi phục." : "Đã cập nhật project trong thư viện Hub.";
    });
    private async Task ArchiveProjectAsync(string path, bool archived)
    {
        if (runner.Busy || projectMutation || closing) return;
        projectMutation = true; status.Text = archived ? "Đang lưu trữ hội thoại của project…" : "Đang khôi phục hội thoại của project…";
        try {
            int count = await Task.Run(() => storage.ArchiveProject(path, archived));
            if (IsDisposed || closing) return;
            ReloadLibrary(); status.Text = $"Đã {(archived ? "lưu trữ" : "khôi phục")} {count} hội thoại. File project và nội dung hội thoại được giữ nguyên.";
        } catch (Exception e) { if (!IsDisposed) status.Text = "Không cập nhật được thư viện: " + e.Message; }
        finally { projectMutation = false; }
    }
}
