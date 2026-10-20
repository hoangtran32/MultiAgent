namespace MultiAgentHub.Core;

public sealed class BrowserChatConnectionException(string agentId, string message, Exception inner) : InvalidOperationException(message, inner)
{
    public string AgentId { get; } = agentId;
}
