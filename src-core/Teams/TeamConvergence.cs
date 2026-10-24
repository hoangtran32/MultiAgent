using System.Text.Json;

namespace MultiAgentHub.Core;

/// <summary>Tracks changes to agreed work, rather than treating new prose as progress.</summary>
public sealed class TeamConvergence
{
    private string previous = "";
    public int UnchangedRounds { get; private set; }
    public void Observe(TeamRun team)
    {
        string state = JsonSerializer.Serialize(new {
            team.CodingStage, team.CodingRoleRevision, directives = team.Directives.Count,
            tasks = team.Tasks.Select(t => new { t.Id, t.ProposalVersion, t.Title, t.Description, t.Acceptance,
                t.State, votes = t.AgreedBy.Order(StringComparer.Ordinal), t.Verification, t.Revisions, t.BlockConfirmed })
        }, Json.Options);
        UnchangedRounds = state == previous ? UnchangedRounds + 1 : 0;
        previous = state;
    }
}
