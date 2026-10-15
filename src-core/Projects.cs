using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiAgentHub.Core;

public sealed class ProjectInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Section { get; set; } = "";
    public bool Pinned { get; set; }
    public bool Removed { get; set; }
    [JsonIgnore] public string DisplayName => Name.Length > 0 ? Name : System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Path;
}

public sealed record ProjectChange(string? Name = null, bool? Pinned = null, string? Section = null, bool? Removed = null);

public static class ProjectCatalog
{
    public static bool SamePath(string first, string second) => string.Equals(System.IO.Path.TrimEndingDirectorySeparator(first), System.IO.Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);
    public static ProjectInfo Ensure(HubConfig config, string path, bool restore = true)
    {
        string absolute = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        var project = config.Projects.FirstOrDefault(p => SamePath(p.Path, absolute));
        if (project == null) { project = new() { Path = absolute }; config.Projects.Add(project); }
        if (restore) project.Removed = false;
        return project;
    }
    public static void Merge(HubConfig config, IEnumerable<ConversationInfo> conversations)
    {
        foreach (string path in config.RecentProjects.Concat(conversations.Select(c => c.Project)).Distinct(StringComparer.OrdinalIgnoreCase)) {
            try { Ensure(config, path, false); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) { }
        }
    }
    public static void Change(HubConfig config, string path, ProjectChange change)
    {
        var project = Ensure(config, path, false);
        if (change.Name != null) { ValidateText(change.Name, 100); project.Name = change.Name.Trim(); }
        if (change.Section != null) {
            ValidateText(change.Section, 60); project.Section = change.Section.Trim();
            if (project.Section.Length > 0 && !config.ProjectSections.Contains(project.Section, StringComparer.OrdinalIgnoreCase)) config.ProjectSections.Add(project.Section);
        }
        if (change.Pinned.HasValue) project.Pinned = change.Pinned.Value;
        if (change.Removed.HasValue) project.Removed = change.Removed.Value;
        if (project.Removed) config.RecentProjects.RemoveAll(p => SamePath(p, project.Path));
    }
    public static void ValidateText(string value, int maximum)
    {
        if (value.Length > maximum || value.Any(char.IsControl)) throw new ArgumentException($"Tên cần tối đa {maximum} ký tự, không chứa ký tự điều khiển.");
    }
}

public sealed partial class Storage
{
    public long ConversationStamp(string id)
    {
        lock (conversationGate) return File.GetLastWriteTimeUtc(System.IO.Path.Combine(ConversationDir(id), "conversation.json")).Ticks;
    }
    public int ArchiveProject(string project, bool archived)
    {
        lock (conversationGate) {
            var conversations = Conversations().Where(c => ProjectCatalog.SamePath(c.Project, project) && c.Archived != archived).ToList();
            foreach (var conversation in conversations) {
                conversation.Archived = archived;
                AtomicText(System.IO.Path.Combine(ConversationDir(conversation.Id), "conversation.json"), JsonSerializer.Serialize(conversation, Json.Options));
            }
            return conversations.Count;
        }
    }
}
