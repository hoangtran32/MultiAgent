using System.Text.Json;
using MultiAgentHub.Core;

internal static class ConsensusTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Consensus assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected rejected consensus transition."); }
    private static TaskProposal Proposal(string id, int version = 1) => new() { Id = id, Version = version, Title = "One small idea " + id, Description = "Produce one bounded analysis.", Acceptance = "Explain assumptions and a way to verify.", RequiresWorkspace = false };
    private static TeamReply Vote(TeamTask task, bool accept) => new() { Message = accept ? "This version is clear." : "The assumption needs revision.", TaskReviews = [new() { TaskId = task.Id, Version = task.ProposalVersion, Accept = accept, Reason = "Scope and acceptance criterion reviewed." }] };
    private static HubConfig Config(string root, int calls = 40, bool chat = false) => new() { Project = root, Surface = chat ? "chat" : "work", Mode = "consensus",
        Agents = [new() { Name = "A", Provider = "codex", Connection = "codex-cli", Permission = "full" }, new() { Name = "B", Provider = "claude", Connection = "claude-cli", Permission = "full" }], Team = new() { MaxCalls = calls } };
    private static async Task Complete(JobRunner runner) { try { await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(15)); } finally { runner.Cancel(); await runner.Completion!; } }

    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        await test("idea agreement locks next idea and expires approvals on revision", () => {
            var team = new TeamRun(); string[] members = ["A", "B"];
            IdeaConsensus.Apply(team, new() { Message = "First idea", Tasks = [Proposal("I1")] }, "A", members);
            var task = team.Tasks.Single(); IdeaConsensus.Apply(team, Vote(task, false), "B", members);
            Reject(() => IdeaConsensus.Validate(team, new() { Message = "Skip to next", Tasks = [Proposal("I2")] }));
            Reject(() => IdeaConsensus.Validate(team, new() { Message = "All done", Finished = true }));
            IdeaConsensus.Apply(team, new() { Message = "Revised scope", TaskAmendments = [Proposal("I1", 2)] }, "B", members);
            Assert(task.ProposalVersion == 2 && task.AgreedBy.SequenceEqual(new[] { "B" }) && task.State == "proposed");
            Reject(() => IdeaConsensus.Validate(team, new() { Message = "Stale vote", TaskReviews = [new() { TaskId = "I1", Version = 1, Accept = true, Reason = "Old version" }] }));
            IdeaConsensus.Apply(team, Vote(task, true), "A", members); Assert(task.State == "ready" && task.AgreedBy.Count == 2);
            Reject(() => IdeaConsensus.Validate(team, new() { Message = "Next too early", Tasks = [Proposal("I2")] }));
            task.State = "done"; IdeaConsensus.Apply(team, new() { Message = "Next idea", Tasks = [Proposal("I2")] }, "A", members);
            Assert(team.Tasks.Count == 2 && IdeaConsensus.Active(team)?.Id == "I2"); return Task.CompletedTask;
        });
        await test("idea quorum includes every available agent and resets on new instructions", () => {
            var team = new TeamRun(); string[] members = ["A", "B", "C"];
            IdeaConsensus.Apply(team, new() { Message = "One idea", Tasks = [Proposal("I1")] }, "A", members);
            var task = team.Tasks.Single(); IdeaConsensus.Apply(team, Vote(task, true), "B", members); Assert(task.State == "proposed");
            Reject(() => IdeaConsensus.Apply(team, Vote(task, true), "outsider", members));
            IdeaConsensus.Apply(team, Vote(task, true), "C", members); Assert(task.State == "ready");
            IdeaConsensus.Invalidate(task); Assert(task.State == "proposed" && task.ProposalVersion == 2 && task.AgreedBy.Count == 0); return Task.CompletedTask;
        });
        await test("consensus negotiates revised idea, executes and reviews before next idea", async () => {
            var store = new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))); var config = Config(root);
            var fixture = new ConsensusFixture { ReviseFirst = true, DesiredIdeas = 2 }; var runner = new JobRunner(fixture, store);
            runner.Start(config, "Complete two small ideas, agreeing each before doing it."); await Complete(runner);
            var job = runner.Snapshot()!; var tasks = job.Team!.Tasks;
            Assert(job.State == "completed" && tasks.Count == 2 && tasks.All(t => t.State == "done" && t.IndependentReview && t.AgreedBy.Count == 2));
            Assert(tasks[0].ProposalVersion == 2 && fixture.WorkVersions.First() == 2 && fixture.NextIdeaAfterDone && fixture.ReviewCalls == 2);
            Assert(fixture.PlanningCalls >= 6 && fixture.Prompts.All(p => p.Contains("CHẾ ĐỘ CHỐT TỪNG Ý")));
            var saved = store.ReadConversationJobs(job.ConversationId).Single();
            Assert(saved.Team!.Tasks[0].ProposalVersion == 2 && Export.PlainText(saved).Contains("v2") && Export.Markdown(saved).Contains("CHỐT TỪNG Ý"));
        });
        await test("disagreement uses bounded calls without forcing agreement or moving on", async () => {
            var fixture = new ConsensusFixture { AlwaysDisagree = true }; var runner = new JobRunner(fixture, new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))));
            runner.Start(Config(root, 10), "One disputed idea"); await Complete(runner);
            var job = runner.Snapshot()!;
            Assert(job.State == "partial" && job.Team!.Calls == 10 && job.Team.Tasks.Count == 1 && job.Team.Tasks[0].State == "proposed");
            Assert(fixture.WorkVersions.Count == 0 && fixture.PlanningCalls == 10);
        });
        await test("ordinary peer failure does not turn disagreement into solo agreement", async () => {
            var fixture = new ConsensusFixture { FailPeer = true }; var runner = new JobRunner(fixture, new Storage(Path.Combine(root, Guid.NewGuid().ToString("N")))); int quota = 0; runner.AgentQuotaReached += _ => quota++;
            runner.Start(Config(root), "Need both peers"); await Complete(runner); var job = runner.Snapshot()!;
            Assert(job.State == "partial" && job.Team!.Tasks.Single().State == "proposed" && fixture.WorkVersions.Count == 0 && quota == 0);
            Assert(job.StopReason.Contains("B: fixture peer failure"));
        });
        await test("individual work timeout names the agent, preserves output and cannot approve work", async () => {
            var fixture = new ConsensusFixture { TimeoutWork = true }; var config = Config(root); config.Team.WorkTimeoutMinutes = 45;
            var store = new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))); var runner = new JobRunner(fixture, store);
            runner.Start(config, "A slow task"); await Complete(runner); var job = runner.Snapshot()!;
            Assert(job.State == "partial" && job.Turns.Last().State == "timeout" && job.Turns.Last().Text == "Reported work saved before timeout");
            Assert(job.StopReason.Contains("A:") && job.StopReason.Contains("45 phút") && fixture.ReviewCalls == 0 && job.Team!.Tasks.Single().State == "ready");
            var resumedFixture = new ConsensusFixture(); var resumed = new JobRunner(resumedFixture, store);
            resumed.Start(config, "Continue from existing files", previous: job); await Complete(resumed);
            Assert(resumedFixture.Prompts.Any(p => p.Contains("Reported work saved before timeout") && p.Contains("CHƯA ĐƯỢC HUB XÁC NHẬN")));
        });
        await test("unlimited consensus passes thirty calls and still finishes only after peer review", async () => {
            var config = Config(root); config.Team.MaxCalls = 0; config.Team.DurationMinutes = 0;
            var runner = new JobRunner(new ConsensusFixture { DesiredIdeas = 9 }, new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))));
            runner.Start(config, "Complete nine ideas with independent review."); await Complete(runner);
            var job = runner.Snapshot()!;
            Assert(job.State == "completed" && job.Deadline == null && job.Team!.Calls > 30);
            Assert(job.Team!.Tasks.Count == 9 && job.Team.Tasks.All(t => t.State == "done" && t.IndependentReview));
        });
        await test("resuming a capped conversation uses newly saved unlimited limits", async () => {
            var config = Config(root, 2); var store = new Storage(Path.Combine(root, Guid.NewGuid().ToString("N")));
            var first = new JobRunner(new ConsensusFixture(), store); first.Start(config, "Complete one idea."); await Complete(first);
            var earlier = first.Snapshot()!; Assert(earlier.State == "partial");
            config.Team.MaxCalls = 0; config.Team.DurationMinutes = 0;
            var resumed = new JobRunner(new ConsensusFixture(), store); resumed.Start(config, "Continue the unfinished task.", previous: earlier); await Complete(resumed);
            var job = resumed.Snapshot()!;
            Assert(job.State == "completed" && job.Team!.Options.MaxCalls == 0 && job.Team.Options.DurationMinutes == 0 && job.Team.Calls > 2);
        });
        await test("unlimited consensus still honors explicit user cancellation", async () => {
            var config = Config(root); config.Team.MaxCalls = 0; config.Team.DurationMinutes = 0;
            var runner = new JobRunner(new BlockingDiscussionFixture(), new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))));
            runner.Start(config, "An unfinished conversation.");
            await Task.Delay(20); runner.Cancel(); await Complete(runner);
            Assert(runner.Snapshot()!.State == "cancelled");
        });
        await test("quota fallback is explicitly solo and never claims independent completion", async () => {
            var fixture = new ConsensusFixture { QuotaPeer = true, DesiredIdeas = 1 }; var runner = new JobRunner(fixture, new Storage(Path.Combine(root, Guid.NewGuid().ToString("N")))); var exhausted = new List<string>(); runner.AgentQuotaReached += exhausted.Add;
            var config = Config(root); runner.Start(config, "One small idea"); await Complete(runner); var job = runner.Snapshot()!;
            Assert(exhausted.SequenceEqual(new[] { config.Agents[1].Id }) && job.State == "unverified" && job.Team!.Tasks.Single().AgreedBy.Count == 1);
            Assert(job.Team!.Decisions.Any(d => d.Contains("đồng thuận độc lập")) && !job.Team.Tasks.Single().IndependentReview);
        });
        await test("chat consensus plans idea by idea without executing workspace work", async () => {
            var fixture = new ConsensusFixture { DesiredIdeas = 2 }; var runner = new JobRunner(fixture, new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))));
            runner.Start(Config(root, chat: true), "Agree two design points"); await Complete(runner); var job = runner.Snapshot()!;
            Assert(job.State == "planned" && job.Team!.Tasks.Count == 2 && job.Team.Tasks.All(t => t.State == "planned" && t.AgreedBy.Count == 2) && fixture.WorkVersions.Count == 0);
        });
        await test("blocker review never approves incomplete work and expires old agreement for a remedy", () => {
            var task = new TeamTask { AgreedBy = ["A", "B"] };
            TaskBlocker.Capture(task, new() { Message = "Wrong runtime", Outcome = "blocked" }, "A");
            Reject(() => TaskBlocker.ApplyReview(task, new() { Message = "Approved blocker", Outcome = "approve" }, "B", 2));
            Reject(() => TaskBlocker.ApplyReview(task, new() { Message = "Still blocked", Outcome = "blocked" }, "B", 2));
            Assert(task.State == "blocked" && !task.BlockConfirmed);
            TaskBlocker.ApplyReview(task, new() { Message = "A supported in-scope workaround", Outcome = "revise" }, "B", 2);
            Assert(task.State == "proposed" && task.NeedsScopeReview && task.ProposalVersion == 2 && task.AgreedBy.Count == 0 && task.Revisions == 1 && !task.BlockConfirmed);
            return Task.CompletedTask;
        });
        await test("blocked execution goes to the other peer before retrying a newly agreed version", async () => {
            var fixture = new ConsensusFixture { BlockFirstWork = true }; var config = Config(root);
            var runner = new JobRunner(fixture, new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))));
            runner.Start(config, "Complete the small idea"); await Complete(runner); var job = runner.Snapshot()!;
            Assert(job.State == "completed" && fixture.BlockReviews == 1 && fixture.BlockReviewAgents.Single() == "B");
            Assert(fixture.WorkVersions.SequenceEqual(new[] { 1, 3 }) && fixture.ReviewCalls == 1 && job.Team!.Tasks.Single().AgreedBy.Count == 2);
        });
        await test("confirmed external blocker stays partial and reports the exact required action", async () => {
            var fixture = new ConsensusFixture { BlockFirstWork = true, ConfirmBlock = true }; var config = Config(root);
            var store = new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))); var runner = new JobRunner(fixture, store);
            runner.Start(config, "Measure with matching runtime only"); await Complete(runner); var job = runner.Snapshot()!; var task = job.Team!.Tasks.Single();
            Assert(job.State == "partial" && job.Team.Calls == 4 && fixture.BlockReviews == 1 && fixture.ReviewCalls == 0);
            Assert(task.State == "blocked" && task.BlockConfirmed && task.BlockReviewedBy == config.Agents[1].Id && job.StopReason.Contains("matching runtime"));
            Assert(Export.PlainText(job).Contains(task.RequiredAction) && Export.Markdown(job).Contains(task.BlockedReason));
            Assert(store.ReadConversationJobs(job.ConversationId).Single().Team!.Tasks.Single().BlockConfirmed);
        });
        await test("repeated blocker remedies stop at revision budget without claiming completion", async () => {
            var fixture = new ConsensusFixture { BlockEveryWork = true }; var runner = new JobRunner(fixture, new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))));
            runner.Start(Config(root), "Bounded remediation"); await Complete(runner); var job = runner.Snapshot()!; var task = job.Team!.Tasks.Single();
            Assert(job.State == "partial" && task.State == "blocked" && task.BlockConfirmed && task.Revisions == 2);
            Assert(fixture.WorkVersions.Count == 3 && fixture.BlockReviews == 3 && task.RequiredAction.Contains("giới hạn"));
        });
        await test("resuming a saved blocker reconciles new scope before voting or invoking a writer", async () => {
            var store = new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))); var config = Config(root);
            var initial = new JobRunner(new ConsensusFixture { BlockFirstWork = true, ConfirmBlock = true }, store);
            initial.Start(config, "Original task"); await Complete(initial); var previous = initial.Snapshot()!;
            previous.Team!.Tasks.Single().Description = "Old plan: do not replace the running process.";
            previous.Team.Directives.Add(new string('x', 20000)); // Older context must not truncate the newest authorization.
            previous.Team.Tasks.Insert(0, new() { Id = "I0", Title = "Already reviewed", State = "done", IndependentReview = true,
                RequiresWorkspace = false, AgreedBy = config.Agents.Select(a => a.Id).ToList() });
            var fixture = new ConsensusFixture(); var runner = new JobRunner(fixture, store);
            const string request = "You may stop the old process to test the research copy; backup connectivity is for research only.";
            runner.Start(config, request, previous: previous); await Complete(runner); var job = runner.Snapshot()!;
            string prompt = fixture.Prompts.First(); int current = prompt.IndexOf("YÊU CẦU NGƯỜI DÙNG HIỆN HÀNH:", StringComparison.Ordinal);
            Assert(job.State == "completed" && fixture.BlockReviews == 0 && !prompt.Contains("KIỂM CHỨNG VƯỚNG MẮC"));
            Assert(current > prompt.IndexOf("Old plan:", StringComparison.Ordinal) && prompt[current..].Contains(request));
            Assert(prompt[prompt.IndexOf("BỔ SUNG THEO THỨ TỰ CŨ → MỚI:", StringComparison.Ordinal)..].Contains(request));
            Assert(fixture.WorkVersions.Single() == 3 && job.Team!.Tasks[1].AgreedBy.Count == 2 && !job.Team.Tasks[1].NeedsScopeReview);
            Assert(job.Team!.Tasks[0].State == "done" && job.Team.Tasks[0].ProposalVersion == 1 && job.Team.Tasks[0].IndependentReview);
            Assert(!job.Team.Tasks[1].Description.Contains("Old plan:") && previous.Team.Tasks[1].State == "blocked");
            Assert(store.ReadConversationJobs(job.ConversationId).First(j => j.Id == job.Id).Team!.Tasks[1].Description == job.Team.Tasks[1].Description);
        });
        await test("scope reconciliation forbids stale votes until a complete amendment is agreed", () => {
            var task = new TeamTask { Id = "I1", NeedsScopeReview = true, State = "proposed", ProposalVersion = 2 };
            var team = new TeamRun { Tasks = [task] }; string[] members = ["A", "B"];
            Reject(() => IdeaConsensus.Apply(team, Vote(task, true), "A", members));
            Assert(task.NeedsScopeReview && task.AgreedBy.Count == 0);
            IdeaConsensus.Apply(team, new() { Message = "Reconcile the authorization", TaskAmendments = [Proposal("I1", 3)] }, "A", members);
            Assert(!task.NeedsScopeReview && task.State == "proposed" && task.AgreedBy.SequenceEqual(new[] { "A" }));
            IdeaConsensus.Apply(team, Vote(task, true), "B", members); Assert(task.State == "ready"); return Task.CompletedTask;
        });
        await test("new instruction during blocker review discards the stale verdict and reconciles scope", async () => {
            var fixture = new ConsensusFixture { BlockFirstWork = true, ConfirmBlock = true };
            var runner = new JobRunner(fixture, new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))));
            fixture.DuringBlockReview = () => {
                runner.AddTeamInstruction("The prerequisite may now be replaced for this test.");
                Assert(runner.Snapshot()!.Turns.Single(t => t.Kind == "user").State == "queued");
            };
            runner.Start(Config(root), "Original constraints"); await Complete(runner); var job = runner.Snapshot()!;
            Assert(job.State == "completed" && fixture.BlockReviews == 1 && fixture.WorkVersions.SequenceEqual(new[] { 1, 3 }));
            Assert(job.Team!.PendingDirectives.Count == 0 && job.Team.Directives.Last().Contains("may now be replaced"));
            Assert(job.Turns.Single(t => t.Kind == "user").State == "success" && job.Turns.Single(t => t.Kind == "user").Text.Contains("may now be replaced"));
            Assert(ConversationTimeline.TeamInputs(job, null).Count == 0 && Export.PlainText(job).Contains("may now be replaced"));
            Assert(!job.Team.Tasks.Single().NeedsScopeReview && !job.Team.Tasks.Single().BlockConfirmed);
        });
        await test("resuming consensus reopens unfinished proposal with fresh approvals", async () => {
            var store = new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))); var config = Config(root);
            var first = new JobRunner(new ConsensusFixture { AlwaysDisagree = true }, store); var limited = Config(root, 4); limited.Agents = config.Agents;
            first.Start(limited, "Original task"); await Complete(first); var previous = first.Snapshot()!;
            var fixture = new ConsensusFixture { DesiredIdeas = 1 }; var runner = new JobRunner(fixture, store);
            runner.Start(config, "Clarify and continue", previous: previous); await Complete(runner); var job = runner.Snapshot()!;
            Assert(job.State == "completed" && job.ConversationId == previous.ConversationId && job.Team!.Tasks.Single().ProposalVersion == 2);
            Assert(job.Team!.Tasks.Single().AgreedBy.Count == 2 && fixture.WorkVersions.Single() == 2);
        });
    }

    private sealed class ConsensusFixture : IAgentExecutor
    {
        public int DesiredIdeas { get; init; } = 1;
        public bool ReviseFirst { get; init; }
        public bool AlwaysDisagree { get; init; }
        public bool FailPeer { get; init; }
        public bool QuotaPeer { get; init; }
        public bool BlockFirstWork { get; init; }
        public bool BlockEveryWork { get; init; }
        public bool ConfirmBlock { get; init; }
        public bool TimeoutWork { get; init; }
        public Action? DuringBlockReview { get; set; }
        public int BlockReviews { get; private set; }
        public List<string> BlockReviewAgents { get; } = [];
        public int PlanningCalls { get; private set; }
        public int ReviewCalls { get; private set; }
        public bool NextIdeaAfterDone { get; private set; }
        public List<int> WorkVersions { get; } = [];
        public List<string> Prompts { get; } = [];
        public Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Prompts.Add(prompt);
            if (agent.Name == "B" && QuotaPeer) throw new AgentQuotaException();
            if (agent.Name == "B" && FailPeer) throw new IOException("fixture peer failure");
            const string marker = "BẢNG TASK:\n"; int start = prompt.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            int end = prompt.IndexOf("\nTÀI LIỆU GẦN ĐÂY:", start, StringComparison.Ordinal);
            var board = JsonSerializer.Deserialize<List<TeamTask>>(prompt[start..end], Json.Options)!;
            TeamReply reply;
            if (prompt.Contains("Đây là lượt THỰC HIỆN task.")) {
                if (TimeoutWork) { text("Reported work saved before timeout"); throw new OperationCanceledException("fixture work timeout"); }
                WorkVersions.Add(IdeaConsensus.Active(new() { Tasks = board })!.ProposalVersion);
                reply = BlockEveryWork || BlockFirstWork && WorkVersions.Count == 1 ?
                    new() { Message = "Runtime does not match; cannot measure.", Outcome = "blocked", BlockedReason = "Wrong runtime version.", RequiredAction = "Select the matching runtime." } :
                    new() { Message = "Implemented only the agreed idea.", Outcome = "done" };
            }
            else if (prompt.Contains("KIỂM CHỨNG VƯỚNG MẮC — TASK CHƯA HOÀN TẤT.")) {
                BlockReviews++; BlockReviewAgents.Add(agent.Name);
                DuringBlockReview?.Invoke();
                reply = ConfirmBlock ? new() { Message = "I independently confirm the prerequisite is missing.", Outcome = "blocked", BlockedReason = "The active runtime is wrong.", RequiredAction = "Select the matching runtime." } :
                    new() { Message = "An in-scope workaround is available; discuss it before execution.", Outcome = "revise" };
            }
            else if (prompt.Contains("Đây là lượt REVIEW ngang hàng.")) { ReviewCalls++; reply = new() { Message = "This idea meets its acceptance criterion.", Outcome = "approve" }; }
            else {
                PlanningCalls++; var task = board.FirstOrDefault(t => t.State is not ("done" or "planned"));
                if (task == null && board.Count < DesiredIdeas) {
                    if (board.Count > 0) NextIdeaAfterDone = board.All(t => t.State is "done" or "planned");
                    reply = new() { Message = "One next idea.", Tasks = [Proposal("I" + (board.Count + 1))] };
                } else if (task == null) reply = new() { Message = "All requested ideas are complete.", Finished = true };
                else if (task.NeedsScopeReview) reply = new() { Message = "Update scope to the current user authorization.", TaskAmendments = [Proposal(task.Id, task.ProposalVersion + 1)] };
                else if (ReviseFirst && task.Id == "I1" && task.ProposalVersion == 1 && agent.Name == "A") reply = new() { Message = "Revise this same idea.", TaskAmendments = [Proposal(task.Id, 2)] };
                else reply = Vote(task, !AlwaysDisagree && !(ReviseFirst && task.Id == "I1" && task.ProposalVersion == 1));
            }
            return Task.FromResult(JsonSerializer.Serialize(reply, Json.Options));
        }
    }
}
