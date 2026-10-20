using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed partial class Storage
{
    public string DataDir { get; }
    public Storage(string? dataDir = null)
    {
        DataDir = dataDir ?? Environment.GetEnvironmentVariable("MULTI_AGENT_HUB_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiAgentHub");
        Directory.CreateDirectory(DataDir);
    }
    public void Write<T>(string name, T value)
    {
        string file = Path.Combine(DataDir, name + ".json"), temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Json.Options)); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public HubConfig LoadConfig()
    {
        string file = Path.Combine(DataDir, "native-config.json");
        if (File.Exists(file))
        {
            var config = JsonSerializer.Deserialize<HubConfig>(File.ReadAllText(file), Json.Options) ?? throw new InvalidDataException("Không đọc được cấu hình native.");
            Validate(config); return config;
        }
        var migrated = Migrate();
        Write("native-config", migrated); return migrated;
    }
    public static void Validate(HubConfig config)
    {
        if (config.Agents.Count > Catalog.MaxAgents || config.Agents.Select(a => a.Id).Distinct().Count() != config.Agents.Count || config.Agents.Any(a => !System.Text.RegularExpressions.Regex.IsMatch(a.Id, "^[a-zA-Z0-9-]{1,80}$"))) throw new InvalidDataException("Danh sách agent không hợp lệ.");
        if (!Catalog.Modes.Contains(config.Mode)) throw new InvalidDataException("Chế độ chạy không hợp lệ.");
        if (config.Surface is not ("work" or "chat")) throw new InvalidDataException("Không gian không hợp lệ.");
        if (config.ChatAgentIds is { } chatIds && (chatIds.Count > Catalog.MaxAgents || chatIds.Distinct().Count() != chatIds.Count || chatIds.Any(id => !config.Agents.Any(a => a.Id == id)))) throw new InvalidDataException("Danh sách chatbot đã chọn không hợp lệ.");
        if (config.BrowserTopics.Count > 4096 || config.BrowserTopics.Any(pair => pair.Key.Length is < 1 or > 128 || pair.Key.Any(char.IsControl) || pair.Value.Length is < 1 or > 128 || pair.Value.Any(char.IsControl))) throw new InvalidDataException("Liên kết chủ đề chatbot không hợp lệ.");
        if (new[] { config.WorkMode, config.ChatMode }.Any(m => !Catalog.Modes.Contains(m))) throw new InvalidDataException("Cách phối hợp không hợp lệ.");
        config.Discussion.Validate();
        config.Team.Validate();
        if (config.Projects.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Projects.Count) throw new InvalidDataException("Project bị trùng đường dẫn.");
        foreach (var project in config.Projects) {
            if (!Path.IsPathFullyQualified(project.Path)) throw new InvalidDataException("Đường dẫn project cần là đường dẫn tuyệt đối.");
            ProjectCatalog.ValidateText(project.Name, 100); ProjectCatalog.ValidateText(project.Section, 60);
        }
        foreach (string section in config.ProjectSections) ProjectCatalog.ValidateText(section, 60);
        foreach (var agent in config.Agents) Catalog.Validate(agent);
    }
    private HubConfig Migrate()
    {
        var config = new HubConfig();
        string sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        config.Project = Directory.Exists(Path.Combine(sourceRoot, "src-desktop")) ? sourceRoot : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string file = Path.Combine(DataDir, "config.json");
        if (!File.Exists(file)) return config;
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var old = doc.RootElement;
        if (Directory.Exists(old.Str("activeProject"))) config.Project = old.Str("activeProject");
        config.Mode = old.Str("defaultMode") == "pipeline" ? "pipeline" : "parallel";
        if (old.TryGetProperty("recentProjects", out var recent) && recent.ValueKind == JsonValueKind.Array)
            config.RecentProjects = recent.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
        string[] selected = old.TryGetProperty("defaultAgents", out var chosen) && chosen.ValueKind == JsonValueKind.Array ? chosen.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray() : [];
        bool hasDefinitions = old.TryGetProperty("agentDefinitions", out var definitions) && definitions.ValueKind == JsonValueKind.Array;
        var defs = hasDefinitions ? definitions.EnumerateArray().ToArray() : [];
        config.Agents = hasDefinitions ? defs.Select(d => new AgentConfig {
            Id = d.Str("id"), Name = d.Str("name"), Instructions = d.Str("instructions"), Command = d.Str("command"),
            Provider = d.Str("kind") switch { "codex" => "codex", "antigravity" => "antigravity", _ => "custom" },
            Connection = d.Str("kind") switch { "codex" => "codex-cli", "antigravity" => "antigravity-ide", "custom-cli" => "custom-cli", _ => "manual" },
            Arguments = d.TryGetProperty("args", out var args) ? args.EnumerateArray().Select(a => a.GetString()!).ToList() : []
        }).ToList() : LegacyAgents(old, selected);
        foreach (var agent in config.Agents)
        {
            agent.Enabled = selected.Contains(agent.Id);
            agent.Permission = agent.Connection == "codex-cli" && old.Str("permission") is "full" or "workspace-write" or "read-only" ? old.Str("permission") : "default";
            if (old.TryGetProperty("agentOptions", out var options) && options.TryGetProperty(agent.Id, out var value))
            { agent.Model = value.Str("model"); agent.Reasoning = value.Str("reasoning"); }
        }
        Validate(config); return config;
    }
    // Old JS versions used these IDs. Recover only entries actually present in the user's data.
    private static List<AgentConfig> LegacyAgents(JsonElement old, string[] selected)
    {
        var ids = selected.AsEnumerable();
        if (old.TryGetProperty("agentOptions", out var options) && options.ValueKind == JsonValueKind.Object)
            ids = ids.Concat(options.EnumerateObject().Select(p => p.Name));
        return ids.Distinct().Select(id => id switch {
            "codex-cli" => new AgentConfig { Id = id, Name = "Codex", Provider = "codex", Connection = "codex-cli" },
            "antigravity-local" => new AgentConfig { Id = id, Name = "Antigravity IDE", Provider = "antigravity", Connection = "antigravity-ide" },
            _ => null
        }).OfType<AgentConfig>().ToList();
    }
    public List<JobRecord> History()
    {
        var history = ReadHistory();
        string checkpoint = Path.Combine(DataDir, "native-active-discussion.json");
        if (File.Exists(checkpoint)) {
            var active = JsonSerializer.Deserialize<JobRecord>(File.ReadAllText(checkpoint), Json.Options);
            if (active != null && !history.Any(j => j.Id == active.Id)) {
                active.State = "interrupted"; active.DiscussionPhase = "interrupted"; active.NextTurnAt = null;
                active.StopReason = "Phiên chưa kết thúc trước khi Hub đóng. Nội dung đã lưu; không tự chạy lại.";
                foreach (var turn in active.Turns.Where(t => t.State == "running")) { turn.State = "cancelled"; turn.Error = "Lượt bị gián đoạn."; }
                foreach (var result in active.Results.Where(r => r.State is "running" or "queued")) result.State = "cancelled";
                if (active.Team is { } team) {
                    team.Phase = "interrupted";
                    foreach (var task in team.Tasks.Where(t => t.State is "running" or "verifying" or "reviewing")) task.State = "interrupted";
                    foreach (var evidence in team.Evidence.Where(e => e.State == "running")) { evidence.State = "interrupted"; evidence.Current = false; }
                    active.Report = TeamEngine.Report(active);
                } else if (active.Report.Length == 0) active.Report = DiscussionEngine.LocalReport(active);
                history.Insert(0, active);
            }
        }
        return history.Take(50).ToList();
    }
    private List<JobRecord> ReadHistory()
    {
        string file = Path.Combine(DataDir, "native-history.json");
        if (File.Exists(file)) return JsonSerializer.Deserialize<List<JobRecord>>(File.ReadAllText(file), Json.Options) ?? [];
        string legacy = Path.Combine(DataDir, "history.json");
        if (!File.Exists(legacy)) return [];
        using var doc = JsonDocument.Parse(File.ReadAllText(legacy));
        return doc.RootElement.EnumerateArray().Take(50).Select(j => new JobRecord {
            Id = j.Str("id"), Prompt = j.Str("prompt"), Project = j.Str("project"), Mode = j.Str("mode"), State = j.Str("status"),
            Timestamp = DateTimeOffset.TryParse(j.Str("timestamp"), out var date) ? date : DateTimeOffset.Now,
            Results = j.GetProperty("results").EnumerateArray().Select(r => new AgentResult { AgentId = r.Str("agentId"), Name = r.Str("agentName"), State = r.Str("status"), Error = r.Str("error"), Logs = r.Str("logs"), Text = r.TryGetProperty("data", out var data) ? data.Str("text") : "" }).ToList()
        }).ToList();
    }
    public void SaveJob(JobRecord job)
    {
        SaveConversationJob(job);
        Write("native-history", new[] { job }.Concat(History().Where(j => j.Id != job.Id)).Take(50).ToList());
    }
    public void Checkpoint(JobRecord job) { SaveConversationJob(job); Write("native-active-discussion", job); }
    public void ClearCheckpoint(string id)
    {
        string file = Path.Combine(DataDir, "native-active-discussion.json");
        if (File.Exists(file) && JsonSerializer.Deserialize<JobRecord>(File.ReadAllText(file), Json.Options)?.Id == id) File.Delete(file);
    }
}
