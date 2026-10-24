using System.Text;
using System.Text.Json;
using MultiAgentHub.Core;

internal static class CodingWorkflowTests
{
    private static void Assert(bool value, string message = "Coding workflow assertion failed") { if (!value) throw new Exception(message); }
    private static AgentConfig Agent(string id) => new() { Id = id, Name = id, Provider = "codex", Connection = "codex-cli", Permission = "full", Model = "discussion-" + id };
    private static CodingOptions Roles(string brain = "A", string worker = "B", string brainModel = "algorithm-model", string workerModel = "implementation-model") => new() { Enabled = true, Brain = new() { Role = "brain", AgentId = brain, Model = brainModel }, Implementer = new() { Role = "implementer", AgentId = worker, Model = workerModel } };
    private static HubConfig Config(string path) => new() { Project = path, Mode = "consensus", Agents = [Agent("A"), Agent("B")], Team = new() { MaxCalls = 0, DurationMinutes = 0, Coding = Roles(), RunVerification = true, VerificationProject = path,
        Checks = [new() { Name = "Offline fixture check", Executable = Environment.ProcessPath!, Arguments = ["--fixture", "harness-command"] }] } };
    private static async Task Done(JobRunner runner) { await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(30)); Assert(!runner.Busy); }
    private static Storage Store(string project) => new(project + "-profile");
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        string Folder(string suffix) { string path = Path.Combine(root, "coding-" + suffix); Directory.CreateDirectory(path); return path; }
        await test("IDE workflow reviews every planned slice before any design or code and hands algorithm to configured worker", async () => {
            string path = Folder("ordered"); var fixture = new Fixture(); var runner = new JobRunner(fixture, Store(path));
            runner.Start(Config(path), "Implement two requested slices."); await Done(runner); var job = runner.Snapshot()!;
            Assert(job.State == "completed" && job.CompletionConfirmed && job.Team!.ApprovedPlanHash.Length == 64);
            Assert(fixture.Work.Count == 2 && fixture.Design.Count == 2 && fixture.PlanReviews == 2);
            int firstDesign = fixture.Events.FindIndex(s => s.StartsWith("design")); int lastPlanReview = fixture.Events.FindLastIndex(s => s.StartsWith("plan-review")); Assert(firstDesign > lastPlanReview);
            Assert(fixture.Design.All(d => d.AgentId == "A" && d.Model == "algorithm-model" && d.Permission == "read-only"));
            Assert(fixture.Work.All(w => w.AgentId == "B" && w.Model == "implementation-model" && w.Plan.Contains("Approved algorithm")));
            Assert(job.Team!.Tasks.All(t => t.State == "done" && t.DesignVersion == t.ProposalVersion && t.IndependentReview));
        });
        await test("IDE plan disagreement cannot be presented as implemented or enter a coding role", async () => {
            string path = Folder("plan-disagreement"); var fixture = new Fixture { RejectPlan = true }; var runner = new JobRunner(fixture, Store(path));
            runner.Start(Config(path), "Implement two requested slices."); await Done(runner); var job = runner.Snapshot()!;
            Assert(job.State == "unverified" && !job.CompletionConfirmed && fixture.Work.Count == 0 && fixture.Design.Count == 0 && job.Team!.Tasks.All(t => t.State == "planned"));
            Assert(job.StopReason.Contains("chưa chuyển sang code"));
        });
        await test("role switch during algorithm call discards old handoff and applies exact newly configured models", async () => {
            string path = Folder("live-roles"); var fixture = new Fixture(); var runner = new JobRunner(fixture, Store(path));
            fixture.OnDesign = () => { fixture.OnDesign = null; runner.UpdateCodingRoles(Roles("B", "A", "new-algorithm", "new-worker")); Assert(runner.Snapshot()!.Team!.PendingCoding != null); };
            runner.Start(Config(path), "Implement two requested slices."); await Done(runner); var job = runner.Snapshot()!;
            Assert(job.State == "completed" && fixture.Design.Count == 3 && fixture.Work.Count == 2);
            Assert(fixture.Work.All(w => w.AgentId == "A" && w.Model == "new-worker"));
            Assert(job.Team!.Tasks.All(t => t.DesignAgentId == "B" && t.DesignModel == "new-algorithm" && t.ImplementationModel == "new-worker"));
            Assert(job.Team.CodingRoleRevision == 2 && job.Team.PendingCoding == null);
        });
        await test("new user scope during plan review invalidates approvals before any code runs", async () => {
            string path = Folder("scope-race"); var fixture = new Fixture(); var runner = new JobRunner(fixture, Store(path));
            fixture.OnPlanReview = () => { fixture.OnPlanReview = null; runner.AddTeamInstruction("Preserve the existing public interfaces while implementing."); };
            runner.Start(Config(path), "Implement two requested slices."); await Done(runner); var job = runner.Snapshot()!;
            Assert(job.State == "completed" && fixture.Work.Count == 2 && fixture.PlanReviews >= 4);
            Assert(job.Team!.Tasks.All(t => t.ProposalVersion == 2 && t.DesignVersion == 2));
        });
        await test("brain failure stops explicitly without substituting the worker as algorithm model", async () => {
            string path = Folder("brain-error"); var fixture = new Fixture { FailDesign = true }; var runner = new JobRunner(fixture, Store(path));
            runner.Start(Config(path), "Implement two requested slices."); await Done(runner); var job = runner.Snapshot()!;
            Assert(job.State == "partial" && !job.CompletionConfirmed && fixture.Work.Count == 0 && fixture.Design.Single().AgentId == "A");
            Assert(job.StopReason.Contains("algorithm fixture failure"));
        });
        await test("IDE resume preserves reviewed done slices while newly configured roles implement the remaining slice", async () => {
            string path = Folder("resume"); var fixture = new Fixture(); var store = Store(path); var runner = new JobRunner(fixture, store);
            fixture.OnCodeReview = () => { if (fixture.Work.Count == 1) runner.StopTeamAfterStep(); };
            var config = Config(path); runner.Start(config, "Implement two requested slices."); await Done(runner); var previous = runner.Snapshot()!;
            Assert(previous.Team!.Tasks.Count(t => t.State == "done") == 1 && fixture.Work.Count == 1);
            var next = new Fixture(); config.Team.Coding = Roles("B", "A", "resume-algorithm", "resume-worker"); var resumed = new JobRunner(next, store);
            resumed.Start(config, "Continue the remaining slice; retain completed work.", previous: previous); await Done(resumed); var job = resumed.Snapshot()!;
            Assert(job.State == "completed" && next.Work.Single().TaskId == "I2" && next.Work.Single().Model == "resume-worker");
            Assert(job.Team!.Tasks.First(t => t.Id == "I1").DesignModel == "algorithm-model");
        });
        await test("coding preset validation rejects unselected or text-only workers and leaves permission configuration unchanged", () => {
            var agents = new List<AgentConfig> { Agent("A"), Agent("B") }; var roles = Roles(); CodingWorkflow.Validate(roles, agents, false);
            agents[1].Permission = "read-only"; bool rejected = false; try { CodingWorkflow.Validate(roles, agents, false); } catch (ArgumentException) { rejected = true; } Assert(rejected && agents[1].Permission == "read-only");
            agents[1].Permission = "full"; agents[1].Connection = "browser-web"; rejected = false; try { CodingWorkflow.Validate(roles, agents, true); } catch (ArgumentException) { rejected = true; } Assert(rejected);
            return Task.CompletedTask;
        });
        await test("workspace editor preserves Unicode encoding and rejects stale or escaping saves", () => {
            string path = Folder("editor"); string file = Path.Combine(path, "source.txt"); File.WriteAllText(file, "Nội dung", Encoding.Unicode);
            var document = WorkspaceDocuments.Read(path, "source.txt"); Assert(document.CodePage == 1200 && document.Bom);
            var saved = WorkspaceDocuments.Save(path, document, "Nội dung mới"); Assert(saved.Text == "Nội dung mới" && saved.CodePage == 1200 && saved.Bom);
            File.WriteAllText(file, "External edit"); bool rejected = false; try { WorkspaceDocuments.Save(path, saved, "Bad overwrite"); } catch (IOException) { rejected = true; } Assert(rejected && File.ReadAllText(file) == "External edit");
            foreach (string relative in new[] { "../outside.txt", ".git/config", ".git./config", "source.txt:stream" }) { rejected = false; try { WorkspaceDocuments.Resolve(path, relative); } catch (ArgumentException) { rejected = true; } Assert(rejected); }
            return Task.CompletedTask;
        });
    }
    private sealed record DesignCall(string AgentId, string Model, string Permission);
    private sealed record WorkCall(string AgentId, string Model, string TaskId, string Plan);
    private sealed class Fixture : IAgentExecutor
    {
        public bool RejectPlan, FailDesign;
        public int PlanReviews;
        public Action? OnDesign, OnPlanReview, OnCodeReview;
        public List<DesignCall> Design { get; } = [];
        public List<WorkCall> Work { get; } = [];
        public List<string> Events { get; } = [];
        public Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            const string marker = "BẢNG TASK:\n"; int start = prompt.IndexOf(marker, StringComparison.Ordinal) + marker.Length; int end = prompt.IndexOf("\nTÀI LIỆU GẦN ĐÂY:", start, StringComparison.Ordinal);
            var tasks = JsonSerializer.Deserialize<List<TeamTask>>(prompt[start..end], Json.Options)!; TeamReply reply;
            if (prompt.Contains("REVIEW TOÀN BỘ GIẢI PHÁP TRƯỚC KHI CODE")) {
                PlanReviews++; Events.Add("plan-review " + agent.Id); OnPlanReview?.Invoke(); Assert(tasks.Count == 2 && tasks.All(t => t.State is "planned" or "done")); Assert(agent.Permission == "read-only");
                int scopeStart = prompt.IndexOf("YÊU CẦU GỐC:\n", StringComparison.Ordinal) + "YÊU CẦU GỐC:\n".Length; int scopeEnd = prompt.IndexOf("\nNGỮ CẢNH:", scopeStart, StringComparison.Ordinal);
                reply = RejectPlan && agent.Id == "B" ? new() { Message = "Plan has a real gap", ScopeGaps = [new(prompt[scopeStart..scopeEnd], "The second requested slice is not covered sufficiently.")] } : new() { Message = "All requested slices are covered by the plan; ready to implement.", Finished = true };
            } else if (prompt.Contains("ĐỐI CHIẾU KẾT THÚC")) { Assert(tasks.All(t => t.State == "done")); reply = new() { Message = "Implementation is complete within the request.", Finished = true }; }
            else if (prompt.Contains("Đây là lượt ĐẦU NÃO THUẬT TOÁN:")) {
                Events.Add("design " + agent.Id); Design.Add(new(agent.Id, agent.Model, agent.Permission)); OnDesign?.Invoke(); if (FailDesign) throw new IOException("algorithm fixture failure");
                reply = new() { Message = "Algorithm and interfaces for this slice.", Outcome = "ready", ImplementationPlan = "Approved algorithm: read current files, implement the agreed slice, retain public interfaces, verify with the configured fixture." };
            } else if (prompt.Contains("Đây là lượt THỰC HIỆN task.")) {
                Assert(agent.Permission == "full"); var task = tasks.First(t => t.State == "running"); Assert(prompt.Contains("BÀN GIAO TỪ ĐẦU NÃO"));
                Events.Add("work " + task.Id); Work.Add(new(agent.Id, agent.Model, task.Id, prompt)); File.WriteAllText(Path.Combine(project, task.Id + ".txt"), "Implemented " + task.Id);
                reply = new() { Message = "Implemented the assigned slice.", Outcome = "done", Files = [task.Id + ".txt"] };
            } else if (prompt.Contains("Đây là lượt REVIEW ngang hàng.")) { OnCodeReview?.Invoke(); reply = new() { Message = "The slice meets its criterion and has passing command evidence.", Outcome = "approve" }; }
            else {
                Assert(agent.Permission == "read-only"); Events.Add("discuss " + agent.Id);
                var active = tasks.FirstOrDefault(t => t.State is not ("done" or "planned"));
                reply = active != null ? new() { Message = "Agree with this exact slice.", TaskReviews = [new() { TaskId = active.Id, Version = active.ProposalVersion, Accept = true, Reason = "Fits the requested solution." }] } : tasks.Count < 2 ?
                    new() { Message = "One required slice.", Tasks = [new() { Id = "I" + (tasks.Count + 1), Title = "Requested slice", Description = "Implement this requested small slice.", Acceptance = "Its output exists and the configured check passes.", DependsOn = tasks.Count == 0 ? [] : ["I1"] }] } : new() { Message = "The whole requested solution has been discussed.", Finished = true };
            }
            return Task.FromResult(JsonSerializer.Serialize(reply, Json.Options));
        }
    }
}
