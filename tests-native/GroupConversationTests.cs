using System.Text.RegularExpressions;
using MultiAgentHub.Core;

internal static class GroupConversationTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Group conversation assertion failed."); }
    private static AgentConfig Agent(string id) => new() { Id = id, Name = id, Provider = "custom", Connection = "custom-cli", Command = "fixture", Model = "website-selected" };
    private static async Task Until(Func<bool> condition) { using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (!condition()) await Task.Delay(5, timer.Token); }
    private static string Answer(string prompt, string text, string state = "continue", string next = "")
    {
        var marker = Regex.Match(prompt, @"<\|hub_turn_[a-f0-9]{32}\|>");
        return marker.Success ? text + "\n" + marker.Value + "{\"state\":\"" + state + "\",\"next\":\"" + next + "\"}" : text;
    }
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        Storage Store() => new(Path.Combine(root, "group-chat-" + Guid.NewGuid().ToString("N")));
        HubConfig Config(params string[] names) => new() { Surface = "chat", Mode = "discussion", Agents = names.Select(Agent).ToList() };
        await test("group chat prompt names peers and keeps mixed user/AI messages in order without forced research artifacts", () => {
            var a = Agent("A"); var b = Agent("B");
            var job = new JobRecord { Surface = "chat", Prompt = "hello", Turns = [
                new() { Name = "A", Text = "Initial answer", State = "success" },
                new() { Kind = "user", Text = "New direction", State = "success" },
                new() { Name = "B", Text = "Responding to new direction", State = "success" }] };
            string prompt = GroupConversation.Prompt(job, b, [a, b], new HashSet<string>(), "", false, GroupConversation.Marker());
            Assert(prompt.Contains("A | ID: A") && prompt.Contains("B | ID: B | chính bạn") && prompt.Contains("hello"));
            Assert(!prompt.Contains("SỔ Ý TƯỞNG") && !prompt.Contains("Luân phiên góc nhìn: đề xuất"));
            string history = GroupConversation.History(job);
            Assert(history.IndexOf("Initial answer") < history.IndexOf("New direction") && history.IndexOf("New direction") < history.IndexOf("Responding"));
            return Task.CompletedTask;
        });
        await test("control footer is hidden while streaming and only a matching valid footer can request idle", () => {
            string marker = GroupConversation.Marker();
            string wire = "Xin chào cả nhóm!" + marker + "{\"state\":\"wait\",\"next\":\"B\"}";
            var reply = GroupConversation.Parse(wire, marker);
            Assert(reply.Text == "Xin chào cả nhóm!" && reply.State == "wait" && reply.Next == "B");
            Assert(GroupConversation.VisibleText("Hi" + marker[..20], marker) == "Hi");
            Assert(GroupConversation.VisibleText(wire, marker) == reply.Text);
            Assert(GroupConversation.Parse("Missing control", marker).State == "continue");
            Assert(GroupConversation.Parse("Answer" + marker + "{\"state\":\"execute\"}", marker).State == "continue");
            Assert(GroupConversation.Parse("Answer" + GroupConversation.Marker() + "{\"state\":\"wait\"}", marker).State == "continue");
            return Task.CompletedTask;
        });
        await test("both chatbots greet user and peer then idle; new topic triggers autonomous exchange without another user message", async () => {
            var fixture = new Fixture((agent, n, prompt) => Task.FromResult(Answer(prompt,
                n <= 2 ? $"Chào bạn và {(agent.Id == "A" ? "B" : "A")}!" : $"Ý {n}: giải pháp cho chủ đề mới.",
                n <= 2 || n >= 5 ? "wait" : "continue")));
            var runner = new JobRunner(fixture, Store()); runner.Start(Config("A", "B"), "hello");
            try {
                await Until(() => runner.Snapshot()!.DiscussionPhase == "awaiting-user");
                var hello = runner.Snapshot()!;
                Assert(fixture.Count == 2 && hello.Turns.Count == 2 && runner.Busy && !runner.Completion!.IsCompleted);
                Assert(hello.Turns[0].Text == "Chào bạn và B!" && hello.Turns[1].Text == "Chào bạn và A!");
                Assert(fixture.Prompts[1].Contains(hello.Turns[0].Text));
                Assert(hello.Turns.All(t => !t.Text.Contains("hub_turn_") && t.ConversationState == "wait"));
                var progress = RunProgress.From(hello, true, DateTimeOffset.Now);
                Assert(progress.Title == "Chờ bạn" && progress.Tone == "idle" && RunProgress.Activity(hello, true) == "");
                runner.AddDiscussionInstruction("Hãy cùng tìm giải pháp cho chủ đề mới.");
                await Until(() => fixture.Count >= 6 && runner.Snapshot()!.DiscussionPhase == "awaiting-user");
                Assert(fixture.Count == 6 && fixture.Prompts.Skip(2).All(p => p.Contains("Hãy cùng tìm giải pháp")));
                Assert(runner.Snapshot()!.Report.Length == 0);
                runner.FinishDiscussion(); await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10));
                Assert(fixture.Count == 7 && runner.Snapshot()!.ReportByModel && runner.Snapshot()!.State == "completed");
            } finally { runner.Cancel(); await runner.Completion!; }
        });
        await test("addressed peer gets next turn without starving a third participant", async () => {
            var fixture = new Fixture((agent, n, prompt) => Task.FromResult(Answer(prompt, "Reply from " + agent.Id, "wait", n == 1 ? "C" : "A")));
            var runner = new JobRunner(fixture, Store()); runner.Start(Config("A", "B", "C"), "Chào cả nhóm");
            try {
                await Until(() => runner.Snapshot()!.DiscussionPhase == "awaiting-user");
                Assert(fixture.Names.SequenceEqual(new[] { "A", "C", "B" }));
            } finally { runner.Cancel(); await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10)); }
        });
        await test("a user interjection during a model call invalidates old wait votes without cancelling that call", async () => {
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously), release = new(TaskCreationOptions.RunContinuationsAsynchronously), resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken inFlight = default;
            var fixture = new Fixture(async (agent, n, prompt) => {
                if (n == 2) { started.TrySetResult(); await release.Task; }
                if (n == 3) resumed.TrySetResult();
                return Answer(prompt, "Reply " + n, "wait");
            }, token => inFlight = token);
            var runner = new JobRunner(fixture, Store()); runner.Start(Config("A", "B"), "hello");
            try {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); runner.AddDiscussionInstruction("Thay hướng: tìm phương án mới.");
                Assert(!inFlight.IsCancellationRequested); release.TrySetResult();
                await resumed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Until(() => runner.Snapshot()!.DiscussionPhase == "awaiting-user");
                Assert(fixture.Count == 4 && fixture.Prompts[2].Contains("Thay hướng"));
            } finally { release.TrySetResult(); runner.Cancel(); await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10)); }
        });
        await test("missing metadata never fakes group consensus or blocks the second chatbot", async () => {
            var third = new TaskCompletionSource();
            var fixture = new Fixture(async (agent, n, prompt) => {
                if (n >= 3) { third.TrySetResult(); await Task.Delay(Timeout.Infinite); }
                return "A plain response without protocol metadata";
            });
            // Use a bounded configured session so a broken fixture cannot leave a background turn.
            var config = Config("A", "B"); config.Discussion.MaxTurns = 2;
            var runner = new JobRunner(fixture, Store()); runner.Start(config, "A question");
            // The third call is the explicit end-of-budget summary, which can be cancelled.
            try {
                await third.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert(fixture.Names.Take(2).SequenceEqual(new[] { "A", "B" }) && runner.Snapshot()!.DiscussionPhase == "summarizing");
            } finally { runner.Cancel(); await runner.Completion!.WaitAsync(TimeSpan.FromSeconds(10)); }
        });
    }
    private sealed class Fixture(Func<AgentConfig, int, string, Task<string>> reply, Action<CancellationToken>? started = null) : IAgentExecutor
    {
        private int count;
        public int Count => Volatile.Read(ref count);
        public List<string> Prompts { get; } = [];
        public List<string> Names { get; } = [];
        public async Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token)
        {
            int n = Interlocked.Increment(ref count); Prompts.Add(prompt); Names.Add(agent.Id); started?.Invoke(token);
            string answer = await reply(agent, n, prompt).WaitAsync(token);
            text(answer); return answer;
        }
    }
}
