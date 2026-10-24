using System.Text;
using System.Text.Json;
using MultiAgentHub.Core;

internal static class IdeTests
{
    private static void Assert(bool value, string message = "IDE assertion failed") { if (!value) throw new Exception(message); }
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        string Folder(string suffix) { string path = Path.Combine(root, "ide-" + suffix); Directory.CreateDirectory(path); return path; }
        HubConfig Config(string path) => new() { Project = path, Mode = "consensus", Agents = [new() { Id = "A", Name = "Chosen", Permission = "full" }, new() { Id = "B", Name = "Other", Permission = "full" }], Team = new() { MaxCalls = 0, DurationMinutes = 0 } };
        await test("IDE fast mode sends one request to the exact selected agent and preserves the shared configuration", async () => {
            string path = Folder("fast"); var config = Config(path); var executor = new FixtureExecutor(); var session = new IdeSession(path, new(path + "-profile"), () => config, executor);
            session.Start(new() { Prompt = "Fix the requested issue.", AgentId = "B", Model = "explicit-model" }); await session.Runner.Completion!;
            Assert(executor.Calls.Count == 1 && executor.Calls[0].Id == "B" && executor.Calls[0].Model == "explicit-model");
            Assert(session.Runner.Snapshot()!.State == "completed" && config.Mode == "consensus" && config.Agents.All(a => a.Enabled) && config.Agents[1].Model.Length == 0);
            var snapshot = JsonSerializer.SerializeToElement(session.Snapshot(), Json.Options); Assert(!snapshot.GetProperty("busy").GetBoolean() && snapshot.GetProperty("results")[0].GetProperty("text").GetString() == "Resolved requested issue.");
        });
        await test("IDE review is read-only and a follow-up retains conversation context without replaying a completed turn", async () => {
            string path = Folder("follow-up"); var config = Config(path); var executor = new FixtureExecutor(); var session = new IdeSession(path, new(path + "-profile"), () => config, executor);
            session.Start(new() { Prompt = "Inspect the existing issue.", AgentId = "A", Mode = "review" }); await session.Runner.Completion!; var first = session.Runner.Snapshot()!;
            Assert(executor.Calls.Single().Permission == "read-only" && config.Agents[0].Permission == "full");
            session.Start(new() { Prompt = "Explain the result.", AgentId = "A", PreviousId = first.Id }); await session.Runner.Completion!; var second = session.Runner.Snapshot()!;
            Assert(second.PreviousJobId == first.Id && second.ConversationId == first.ConversationId && executor.Calls.Count == 2);
            Assert(executor.Prompts[1].Contains("Inspect the existing issue.") && executor.Prompts[1].Contains("Resolved requested issue."));
            Assert(JsonSerializer.SerializeToElement(session.Snapshot(), Json.Options).GetProperty("history").GetArrayLength() == 1);
        });
        await test("IDE rejects an unavailable or browser worker rather than substituting another selected model", () => {
            string path = Folder("selection"); var config = Config(path); var executor = new FixtureExecutor(); var session = new IdeSession(path, new(path + "-profile"), () => config, executor);
            foreach (var id in new[] { "missing", "B" }) { if (id == "B") { config.Agents[1].Provider = "gemini"; config.Agents[1].Connection = "gemini-web"; } bool rejected = false; try { session.Start(new() { Prompt = "Do work.", AgentId = id }); } catch (ArgumentException) { rejected = true; } Assert(rejected); }
            Assert(executor.Calls.Count == 0 && !session.Runner.Busy); return Task.CompletedTask;
        });
        await test("IDE profiles isolate projects and NDJSON preserves multiline Unicode in a single frame", () => {
            string path = Folder("framing"); Assert(IdeSession.WorkspaceProfile(root, path) != IdeSession.WorkspaceProfile(root, Folder("different")));
            string wire = IdeWire.Serialize(new { id = "1", result = new { text = "Dòng 1\nDòng 2\r\nẢnh 🧠", project = path } });
            Assert(!wire.Contains('\n') && !wire.Contains('\r')); using var json = JsonDocument.Parse(wire); Assert(json.RootElement.GetProperty("result").GetProperty("text").GetString() == "Dòng 1\nDòng 2\r\nẢnh 🧠"); return Task.CompletedTask;
        });
        await test("team convergence ignores rewritten prose and resets on real agreement or user scope changes", () => {
            var guard = new TeamConvergence(); var team = new TeamRun { Tasks = [new() { Id = "I1", Title = "Required change", Acceptance = "Meets request" }] };
            guard.Observe(team); for (int i = 0; i < 3; i++) { team.Decisions.Add("New wording " + i); team.Tasks[0].Feedback = "Still thinking " + i; guard.Observe(team); }
            Assert(guard.UnchangedRounds == 3); team.Tasks[0].AgreedBy.Add("peer"); guard.Observe(team); Assert(guard.UnchangedRounds == 0);
            guard.Observe(team); Assert(guard.UnchangedRounds == 1); team.Directives.Add("New user scope"); guard.Observe(team); Assert(guard.UnchangedRounds == 0); return Task.CompletedTask;
        });
        await test("consensus without a call or time limit stops repeated empty exchanges without falsely completing", async () => {
            string path = Folder("convergence"); var config = Config(path); config.Team.ExecuteTasks = false; var executor = new FixtureExecutor { EmptyDiscussion = true }; var runner = new JobRunner(executor, new(path + "-profile"));
            runner.Start(config, "Answer the concrete question."); await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(15)); var job = runner.Snapshot()!;
            Assert(executor.Calls.Count <= 10 && job.State == "partial" && !job.CompletionConfirmed && job.StopReason.Contains("vòng lặp"));
        });
        await test("fast IDE cannot write concurrently with another workflow in the same workspace", async () => {
            string path = Folder("writer"); var config = Config(path); var executor = new FixtureExecutor(); var session = new IdeSession(path, new(path + "-profile"), () => config, executor);
            using var writer = TeamWorkspace.AcquireWriter(path); session.Start(new() { Prompt = "Modify source.", AgentId = "A" }); await session.Runner.Completion!;
            Assert(executor.Calls.Count == 0 && session.Runner.Snapshot()!.State == "failed");
        });
        await test("an informational request in IDE planning can finish without inventing implementation tasks", async () => {
            string path = Folder("informational"); var config = Config(path); config.Team.Coding = new() { Enabled = true, Brain = new() { Role = "brain", AgentId = "A" }, Implementer = new() { Role = "implementer", AgentId = "B" } };
            config.Agents.Add(new() { Id = "manual-peer", Name = "Manual", Provider = "custom", Connection = "manual" });
            config.Agents.Add(new() { Id = "browser-peer", Name = "Browser", Provider = "gemini", Connection = "gemini-web" });
            var executor = new FixtureExecutor { FinishedDiscussion = true }; var session = new IdeSession(path, new(path + "-profile"), () => config, executor); var runner = session.Runner;
            session.Start(new() { Mode = "plan", Prompt = "Explain the project architecture; no code change is requested." }); await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(15)); var job = runner.Snapshot()!;
            Assert(job.State == "completed" && job.CompletionConfirmed && job.Team!.Tasks.Count == 0 && executor.Calls.Count == 4);
            Assert(executor.Calls.All(a => a.Permission == "read-only"));
            Assert(executor.Calls.All(a => a.Id is "A" or "B") && config.Agents.All(a => a.Enabled));
        });
        await test("IDE projection bounds large Unicode histories without exposing internal prompts or transport configuration", () => {
            string path = Folder("large"); var config = Config(path); var session = new IdeSession(path, new(path + "-profile"), () => config, new FixtureExecutor());
            string text = new('研', 64000); var job = new JobRecord { Prompt = text, Surface = "work", Project = path, State = "completed", Team = new() };
            for (int i = 0; i < 32; i++) { job.Results.Add(new() { AgentId = "A" + i, Name = "Agent", Text = text, SentPrompt = "PRIVATE TRANSPORT PROMPT" }); job.Turns.Add(new() { Number = i, Kind = "team", Text = text }); }
            for (int i = 0; i < 50; i++) job.Team.Tasks.Add(new() { Id = "T" + i, Title = "Task", Description = text, Acceptance = text, Feedback = text, Outcome = text, ImplementationPlan = text });
            for (int i = 0; i < 150; i++) job.Team.Artifacts.Add(new() { Title = "Artifact", Content = text });
            string wire = IdeWire.Serialize(session.Snapshot(job)); Assert(Encoding.UTF8.GetByteCount(wire) < 12 * 1024 * 1024 && !wire.Contains("PRIVATE TRANSPORT PROMPT")); return Task.CompletedTask;
        });
    }
    private sealed class FixtureExecutor : IAgentExecutor
    {
        public bool EmptyDiscussion, FinishedDiscussion;
        public List<AgentConfig> Calls { get; } = [];
        public List<string> Prompts { get; } = [];
        public Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
        {
            Calls.Add(agent.Copy()); Prompts.Add(prompt);
            return Task.FromResult(EmptyDiscussion || FinishedDiscussion ? JsonSerializer.Serialize(new TeamReply { Message = "Assessment of the current request.", Finished = FinishedDiscussion }, Json.Options) : "Resolved requested issue.");
        }
    }
}
