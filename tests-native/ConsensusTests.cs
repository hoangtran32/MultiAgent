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
            Assert(task.State == "proposed" && task.ProposalVersion == 2 && task.AgreedBy.Count == 0 && task.Revisions == 1 && !task.BlockConfirmed);
            return Task.CompletedTask;
        });
        await test("blocked execution goes to the other peer before retrying a newly agreed version", async () => {
            var fixture = new ConsensusFixture { BlockFirstWork = true }; var config = Config(root);
            var runner = new JobRunner(fixture, new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))));
            runner.Start(config, "Complete the small idea"); await Complete(runner); var job = runner.Snapshot()!;
            Assert(job.State == "completed" && fixture.BlockReviews == 1 && fixture.BlockReviewAgents.Single() == "B");
            Assert(fixture.WorkVersions.SequenceEqual(new[] { 1, 2 }) && fixture.ReviewCalls == 1 && job.Team!.Tasks.Single().AgreedBy.Count == 2);
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
        await test("resuming a saved blocker reviews the obstacle before invoking another writer", async () => {
            var store = new Storage(Path.Combine(root, Guid.NewGuid().ToString("N"))); var config = Config(root);
            var initial = new JobRunner(new ConsensusFixture { BlockFirstWork = true, ConfirmBlock = true }, store);
            initial.Start(config, "Original task"); await Complete(initial); var previous = initial.Snapshot()!;
            var fixture = new ConsensusFixture(); var runner = new JobRunner(fixture, store);
            runner.Start(config, "Prerequisite now available; continue", previous: previous); await Complete(runner); var job = runner.Snapshot()!;
            Assert(job.State == "completed" && fixture.Prompts.First().Contains("KIỂM CHỨNG VƯỚNG MẮC") && fixture.BlockReviewAgents.Single() == "B");
            Assert(fixture.WorkVersions.Single() == 3 && job.Team!.Tasks.Single().AgreedBy.Count == 2);
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
                WorkVersions.Add(IdeaConsensus.Active(new() { Tasks = board })!.ProposalVersion);
                reply = BlockEveryWork || BlockFirstWork && WorkVersions.Count == 1 ?
                    new() { Message = "Runtime does not match; cannot measure.", Outcome = "blocked", BlockedReason = "Wrong runtime version.", RequiredAction = "Select the matching runtime." } :
                    new() { Message = "Implemented only the agreed idea.", Outcome = "done" };
            }
            else if (prompt.Contains("KIỂM CHỨNG VƯỚNG MẮC — TASK CHƯA HOÀN TẤT.")) {
                BlockReviews++; BlockReviewAgents.Add(agent.Name);
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
                else if (ReviseFirst && task.Id == "I1" && task.ProposalVersion == 1 && agent.Name == "A") reply = new() { Message = "Revise this same idea.", TaskAmendments = [Proposal(task.Id, 2)] };
                else reply = Vote(task, !AlwaysDisagree && !(ReviseFirst && task.Id == "I1" && task.ProposalVersion == 1));
            }
            return Task.FromResult(JsonSerializer.Serialize(reply, Json.Options));
        }
    }
}
