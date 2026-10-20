using MultiAgentHub.Core;

internal static class ProgressTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Progress presentation assertion failed."); }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        JobRecord Job() => new() { Mode = "consensus", Timestamp = now.AddMinutes(-2), Team = new() { Phase = "work", ActiveTaskId = "I2", Calls = 3,
            Tasks = [new() { Id = "I1", State = "done" }, new() { Id = "I2", Title = "Measure a bounded change", State = "running" }] },
            Results = [new() { AgentId = "A", Name = "A", State = "running" }, new() { AgentId = "B", Name = "B", State = "success" }],
            Turns = [new() { Name = "A", AgentId = "A", TaskId = "I2", State = "running", Role = "Coder" }] };
        await test("progress identifies active agent, task and reviewed count separately from call budget", () => {
            var state = RunProgress.From(Job(), true, now);
            Assert(state.IsRunning && !state.CanContinue && state.Title == "Đang thực hiện task");
            Assert(state.Detail.Contains("A — I2") && state.Done == 1 && state.Total == 2 && state.Progress.Contains("1/2 task đã review"));
            Assert(state.Timing.Contains("3/30 lần gọi (giới hạn)") && state.Agents.Contains("B: đã góp ý, chờ lượt")); return Task.CompletedTask;
        });
        await test("unlimited budgets round trip without being shown as a reached zero limit", () => {
            var options = System.Text.Json.JsonSerializer.Deserialize<TeamOptions>("{\"maxCalls\":0,\"durationMinutes\":0}", Json.Options)!;
            options.Validate(); Assert(options.HasCallCapacity(800) && options.TurnTimeoutMinutes("review") == 0 && options.TurnTimeoutMinutes("work") == 0);
            options.ExchangeTimeoutMinutes = 45; options.WorkTimeoutMinutes = 90;
            var copy = options.Copy(); copy.Validate(); Assert(copy.TurnTimeoutMinutes("review") == 45 && copy.TurnTimeoutMinutes("work") == 90);
            var job = Job(); job.Team!.Options = options;
            Assert(RunProgress.From(job, true, now).Timing.Contains("không giới hạn lượt") && !RunProgress.From(job, true, now).Timing.Contains("/0"));
            return Task.CompletedTask;
        });
        await test("all tasks reviewed does not imply a running session has finished", () => {
            var job = Job(); job.Team!.Tasks[1].State = "done"; job.Team.Phase = "roundtable";
            var state = RunProgress.From(job, true, now); Assert(state.Done == 2 && state.Title == "Đang thống nhất ý" && state.IsRunning);
            job.State = "completed"; job.Team.Phase = "finished";
            Assert(RunProgress.From(job, true, now).Title == "Đang lưu kết quả");
            var final = RunProgress.From(job, false, now); Assert(final.Title == "Hoàn tất phiên" && !final.IsRunning && !final.CanContinue); return Task.CompletedTask;
        });
        await test("stopped progress names the failed agent and unfinished task without claiming completion", () => {
            var job = Job(); job.State = "partial"; job.Team!.Phase = "finished"; job.Team.Tasks[1].State = "ready";
            job.Turns[0].State = "timeout"; job.Turns[0].Error = "CLI exceeded 30 minutes"; job.Results[0].State = "timeout";
            var state = RunProgress.From(job, false, now);
            Assert(!state.IsRunning && state.CanContinue && state.Tone == "error" && state.Title.Contains("chưa xong"));
            Assert(state.Reason == "A: CLI exceeded 30 minutes" && state.Detail.Contains("I2") && state.Detail.Contains("chờ thực hiện"));
            job.Turns[0].State = "cancelled"; job.Results[0].State = "cancelled";
            Assert(RunProgress.From(job, false, now).Reason.Contains("A:")); return Task.CompletedTask;
        });
        await test("saved active-looking history is shown as interrupted while unverified output stays explicit", () => {
            var job = Job(); job.Seconds = 42;
            var state = RunProgress.From(job, false, now.AddHours(1)); Assert(state.Title.Contains("gián đoạn") && state.CanContinue && state.Timing.StartsWith("0 phút 42"));
            job.State = "completed"; state = RunProgress.From(job, false, now); Assert(state.CanContinue && state.Title.Contains("còn task chưa xong") && job.State == "completed");
            job.State = "unverified"; job.Team!.Tasks.ForEach(t => t.State = "done");
            state = RunProgress.From(job, false, now); Assert(state.Title.Contains("chưa kiểm chứng") && state.Tone == "warning"); return Task.CompletedTask;
        });
        await test("planning progress counts planned ideas without presenting code as implemented", () => {
            var job = Job(); job.State = "planned"; job.Surface = "chat"; job.Team!.Phase = "finished"; job.Team.Tasks.ForEach(t => t.State = "planned");
            var state = RunProgress.From(job, false, now); Assert(state.Done == 2 && state.Progress.Contains("ý đã chốt kế hoạch") && state.Title.Contains("chưa thực hiện")); return Task.CompletedTask;
        });
        await test("discussion pause and manual waiting are distinct from doing work or finishing", () => {
            var job = new JobRecord { Mode = "discussion", DiscussionPhase = "resting", NextTurnAt = now.AddSeconds(20), Results = [new() { Name = "A", State = "success" }] };
            var state = RunProgress.From(job, true, now); Assert(state.Title == "Đang nghỉ giữa lượt" && state.Detail.Contains("20 giây") && state.Total == 0);
            job.Mode = "parallel"; job.Results[0].State = "waiting";
            state = RunProgress.From(job, true, now); Assert(state.Title.Contains("chờ bạn dán") && state.Detail.Contains("Chi tiết") && state.Done == 0);
            job.Results.Add(new() { Name = "B", State = "running" });
            state = RunProgress.From(job, true, now); Assert(state.Title == "Đang làm việc" && state.Detail.Contains("B") && state.Reason.Contains("dán kết quả")); return Task.CompletedTask;
        });
        await test("quota and failed agent answers do not increase completed work count", () => {
            var job = new JobRecord { Mode = "parallel", State = "partial", Results = [new() { Name = "A", State = "success" }, new() { Name = "B", State = "quota" }, new() { Name = "C", State = "error", Error = "Connection failed" }] };
            var state = RunProgress.From(job, false, now); Assert(state.Done == 1 && state.Total == 3 && state.CanContinue && state.Reason.Contains("C: Connection failed"));
            var task = new TeamTask { State = "proposed", NeedsScopeReview = true }; Assert(RunProgress.TaskState(task).Contains("cập nhật phạm vi")); return Task.CompletedTask;
        });
        await test("loading names only running providers and stops for history, waiting and finished runs", () => {
            var job = Job(); Assert(RunProgress.Activity(job, true) == "A đang xử lý…");
            job.Results[1].State = "running"; Assert(RunProgress.Activity(job, true) == "A + B đang xử lý…");
            Assert(RunProgress.Activity(job, false) == ""); job.State = "partial"; Assert(RunProgress.Activity(job, true) == "");
            job.State = "running"; job.Results.ForEach(r => r.State = "waiting"); Assert(RunProgress.Activity(job, true) == "");
            job.Team!.Phase = "verifying"; Assert(RunProgress.Activity(job, true).Contains("kiểm tra")); return Task.CompletedTask;
        });
        await test("conversation inputs omit inherited resume history and inputs already represented as messages", () => {
            var previous = new JobRecord { Team = new() { Directives = ["Old input", "Another old input"] } };
            var job = new JobRecord { Prompt = "Continue", Team = new() { Directives = [..previous.Team.Directives, "Yêu cầu khi tiếp tục: Continue", "New legacy input", "Live input"], PendingDirectives = ["Queued input"] },
                Turns = [new() { Kind = "user", Text = "Live input", State = "success" }, new() { Kind = "user", Text = "Queued input", State = "queued" }] };
            Assert(ConversationTimeline.TeamInputs(job, previous).SequenceEqual(new[] { "New legacy input" }));
            job.Turns.RemoveAt(1); Assert(ConversationTimeline.TeamInputs(job, previous).Last() == "[Đang chờ áp dụng] Queued input");
            Assert(previous.Team.Directives.Count == 2 && job.Team.Directives.Count == 5); return Task.CompletedTask;
        });
    }
}
