using System.Text.Json;
using MultiAgentHub.Core;

internal static class DiscussionBudgetTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Discussion budget assertion failed."); }

    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        Storage Store() => new(Path.Combine(root, "discussion-budget-" + Guid.NewGuid().ToString("N")));
        AgentConfig Agent(string id) => new() { Id = id, Name = id, Provider = "codex", Connection = "codex-cli", Model = "fixture" };
        await test("discussion unlimited budgets persist and legacy finite settings keep their selected values", () => {
            var options = new DiscussionOptions(); options.Validate();
            Assert(options.HasTurnCapacity(1000000) && options.DurationMinutes == 0 && options.TurnTimeoutMinutes == 0 && options.PauseSeconds == 0);
            var restored = JsonSerializer.Deserialize<DiscussionOptions>(JsonSerializer.Serialize(options, Json.Options), Json.Options)!;
            Assert(restored.HasTurnCapacity(1000000) && restored.TurnUsage(42).Contains("không giới hạn") && !restored.TurnUsage(42).Contains("/0"));
            var legacy = JsonSerializer.Deserialize<DiscussionOptions>("{\"maxTurns\":48,\"durationMinutes\":480,\"pauseSeconds\":0}", Json.Options)!;
            Assert(legacy.MaxTurns == 48 && legacy.DurationMinutes == 480 && legacy.TurnTimeoutMinutes == 0 && !legacy.HasTurnCapacity(48));
            legacy.TurnTimeoutMinutes = 45; var copy = legacy.Copy(); copy.Validate();
            Assert(copy.TurnTimeoutMinutes == 45 && copy.MaxTurns == 48 && copy.DurationMinutes == 480);
            return Task.CompletedTask;
        });
        await test("unlimited discussion keeps alternating beyond old duration and finishes only after the active turn", async () => {
            var clock = new RecordingClock(); var fixture = new FinishFixture(clock); var runner = new JobRunner(fixture, Store(), clock);
            runner.Start(new() { Project = root, Surface = "chat", Mode = "discussion", Agents = [Agent("A"), Agent("B")], Discussion = new() }, "Research a topic");
            try {
                await fixture.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert(runner.Busy && runner.Snapshot()!.Deadline == null && fixture.Names.SequenceEqual(new[] { "A", "B" }));
                runner.FinishDiscussion(); Assert(!fixture.SecondToken.IsCancellationRequested);
                fixture.Release.TrySetResult(); await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10));
                var job = runner.Snapshot()!;
                Assert(job.State == "completed" && job.Turns.Count(t => t.Kind == "discussion") == 2 && job.Report == "summary");
                Assert(clock.DueTimes.Count == 0 && fixture.Names.SequenceEqual(new[] { "A", "B", "A" }));
            } finally { fixture.Release.TrySetResult(); runner.Cancel(); await runner.Completion!; }
        });
        await test("discussion configured turn timeout replaces the hidden ten-minute timer for discussion and summary", async () => {
            var clock = new RecordingClock(); var fixture = new InstantFixture(); var runner = new JobRunner(fixture, Store(), clock);
            runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("A")], Discussion = new() { MaxTurns = 1, DurationMinutes = 0, TurnTimeoutMinutes = 45 } }, "topic");
            await runner.Completion!;
            Assert(fixture.Count == 2 && clock.DueTimes.SequenceEqual(new[] { TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(45) }));
            Assert(runner.Snapshot()!.ReportByModel && runner.Snapshot()!.State == "completed");
        });
        await test("cancelling unlimited discussion preserves partial text and never starts a summary", async () => {
            var fixture = new CancelFixture(); var store = Store(); var runner = new JobRunner(fixture, store);
            runner.Start(new() { Project = root, Mode = "discussion", Agents = [Agent("A")], Discussion = new() }, "topic");
            try {
                await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); runner.Cancel();
                await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10));
                var job = runner.Snapshot()!;
                Assert(job.State == "cancelled" && job.Turns.Single().Text == "partial research" && job.Turns.Single().State == "cancelled");
                Assert(!job.ReportByModel && store.History().Single().Turns.Single().Text == "partial research");
            } finally { runner.Cancel(); await runner.Completion!; }
        });
    }

    private sealed class RecordingClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public List<TimeSpan> DueTimes { get; } = [];
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            DueTimes.Add(dueTime);
            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
        }
    }
    private sealed class FinishFixture(RecordingClock clock) : IAgentExecutor
    {
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Names { get; } = [];
        public CancellationToken SecondToken { get; private set; }
        public async Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
        {
            Names.Add(agent.Name);
            if (Names.Count == 1) { clock.Advance(TimeSpan.FromDays(2)); return "first idea"; }
            if (Names.Count == 2) { SecondToken = token; SecondStarted.TrySetResult(); await Release.Task.WaitAsync(token); return "peer response"; }
            return "summary";
        }
    }
    private sealed class InstantFixture : IAgentExecutor
    {
        public int Count { get; private set; }
        public Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
        { Count++; return Task.FromResult("idea-" + Count); }
    }
    private sealed class CancelFixture : IAgentExecutor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
        { text("partial research"); Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return "unreachable"; }
    }
}
