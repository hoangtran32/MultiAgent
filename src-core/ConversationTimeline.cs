namespace MultiAgentHub.Core;

/// <summary>Legacy supplemental inputs absent from message turns, without inherited resume history.</summary>
public static class ConversationTimeline
{
    public static List<string> TeamInputs(JobRecord job, JobRecord? previous)
    {
        if (job.Team is not { } team) return [];
        var earlier = previous?.Team?.Directives ?? [];
        int shared = 0;
        while (shared < earlier.Count && shared < team.Directives.Count && earlier[shared] == team.Directives[shared]) shared++;
        bool Represented(string input) => input == "Yêu cầu khi tiếp tục: " + job.Prompt || job.Turns.Any(t => t.Kind == "user" && t.Text == input);
        return team.Directives.Skip(shared).Where(d => !Represented(d))
            .Concat(team.PendingDirectives.Where(d => !Represented(d)).Select(d => "[Đang chờ áp dụng] " + d)).ToList();
    }
}
