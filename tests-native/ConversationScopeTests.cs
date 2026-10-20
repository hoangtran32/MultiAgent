using System.Text.Json;
using MultiAgentHub.Core;

internal static class ConversationScopeTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Conversation scope assertion failed."); }
    private static Storage Store(string root) => new(Path.Combine(root, Guid.NewGuid().ToString("N")));
    private static AgentConfig Browser() => new() { Provider = "gemini", Connection = "gemini-web", BrowserBindingId = "fixture-tab", Model = "Confirmed" };
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        await test("projectless browser chat saves standalone metadata and exports without a workspace", async () => {
            var store = Store(root); var fixture = new TextOnly(); var runner = new JobRunner(fixture, store);
            var config = new HubConfig { Surface = "chat", Project = Path.Combine(root, "missing-workspace"), Agents = [Browser()] };
            runner.Start(config, "Research this topic"); await runner.Completion!;
            var job = runner.Snapshot()!; var info = store.Conversations().Single();
            Assert(job.State == "completed" && job.Project == "" && info.Project == "" && info.Surface == "chat");
            Assert(config.RecentProjects.Count == 0 && config.Projects.Count == 0 && config.Project.EndsWith("missing-workspace"));
            Assert(fixture.Directory == "" && !fixture.Prompt.Contains(config.Project));
            Assert(!Export.Markdown(job).Contains("Project:") && !File.ReadAllText(store.ConversationTextPath(info)).Contains("Project:"));
        });
        await test("CLI chat uses a private runtime folder and retains the configured work permission", async () => {
            var store = Store(root); var fixture = new TextOnly(); var runner = new JobRunner(fixture, store);
            var agent = new AgentConfig { Permission = "full" };
            runner.Start(new() { Surface = "chat", Project = "", Agents = [agent] }, "Explain a concept"); await runner.Completion!;
            var job = runner.Snapshot()!;
            Assert(job.State == "completed" && job.Project == "" && fixture.Permission == "read-only" && agent.Permission == "full");
            Assert(fixture.Directory == Path.Combine(store.ConversationsDir, job.ConversationId, "runtime") && Directory.Exists(fixture.Directory));
            Assert(!File.ReadAllText(Path.Combine(store.ConversationsDir, job.ConversationId, "job-" + job.Id + ".json")).Contains("workingDirectory"));
        });
        await test("chat continues after changing or removing the work project", async () => {
            var store = Store(root); var fixture = new TextOnly(); var runner = new JobRunner(fixture, store);
            var config = new HubConfig { Surface = "chat", Project = "", Agents = [Browser()] };
            runner.Start(config, "First question"); await runner.Completion!; var previous = runner.Snapshot()!;
            config.Project = Path.Combine(root, "another-missing-folder");
            runner.Start(config, "Follow up", previous: previous); await runner.Completion!;
            Assert(runner.Snapshot()!.ConversationId == previous.ConversationId && runner.Snapshot()!.Project == "");
            Assert(fixture.Prompt.Contains("First question") && store.ReadConversationJobs(previous.ConversationId).Count == 2);
        });
        await test("legacy chat metadata is detached without rewriting old job files", () => {
            var store = Store(root); string dir = Path.Combine(store.ConversationsDir, "legacy-chat"); Directory.CreateDirectory(dir);
            var job = new JobRecord { ConversationId = "legacy-chat", Surface = "chat", Project = root, Prompt = "Old chat", State = "completed" };
            string original = JsonSerializer.Serialize(job, Json.Options), jobFile = Path.Combine(dir, "job-" + job.Id + ".json");
            File.WriteAllText(jobFile, original);
            File.WriteAllText(Path.Combine(dir, "conversation.json"), JsonSerializer.Serialize(new { id = "legacy-chat", title = "Old chat", project = root, createdAt = job.Timestamp, textFile = "Old_chat.txt" }, Json.Options));
            var info = store.Conversations().Single();
            Assert(info.Surface == "chat" && info.Project == "" && store.ReadConversationJobs(info.Id).Single().Project == "");
            var config = new HubConfig { RecentProjects = [] }; ProjectCatalog.Merge(config, [info]); Assert(config.Projects.Count == 0);
            Assert(store.ArchiveProject(root, true) == 0 && !store.Conversations().Single().Archived);
            store.SaveConversationJob(new() { ConversationId = info.Id, Surface = "chat", Project = "", Prompt = "Follow up", State = "completed" });
            Assert(File.ReadAllText(jobFile) == original && store.Conversations().Single().Surface == "chat");
            return Task.CompletedTask;
        });
        await test("work still requires a real project and cannot resume a chat as a work task", async () => {
            var store = Store(root); var runner = new JobRunner(new TextOnly(), store); bool missing = false, crossed = false;
            try { runner.Start(new() { Project = "", Agents = [Browser()] }, "Work"); } catch (DirectoryNotFoundException) { missing = true; }
            var chat = new HubConfig { Surface = "chat", Project = "", Agents = [Browser()] };
            runner.Start(chat, "Chat"); await runner.Completion!;
            try { runner.Start(new() { Project = root, Agents = [Browser()] }, "Work", previous: runner.Snapshot()); } catch (ArgumentException) { crossed = true; }
            Assert(missing && crossed && !runner.Busy);
        });
        await test("chat attachment reads only the explicitly selected file without exposing its parent path", () => {
            string folder = Path.Combine(root, "outside-workspace"); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "notes.txt"); File.WriteAllText(file, "Selected research notes");
            string context = ProjectContext.ReadAttachment(file);
            Assert(context.Contains("notes.txt") && context.Contains("Selected research notes") && !context.Contains(folder));
            File.WriteAllText(file, "binary\0"); bool rejected = false;
            try { ProjectContext.ReadAttachment(file); } catch (ArgumentException) { rejected = true; }
            Assert(rejected); return Task.CompletedTask;
        });
    }
    private sealed class TextOnly : IAgentExecutor
    {
        public string Directory { get; private set; } = "";
        public string Prompt { get; private set; } = "";
        public string Permission { get; private set; } = "";
        public Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
        { Directory = project; Prompt = prompt; Permission = agent.Permission; return Task.FromResult("Offline fixture response"); }
    }
}
