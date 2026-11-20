using System.Collections.Concurrent;

namespace SampleHrms.Web.Services.Agent;

public sealed class AgentSession
{
    public AgentSession(string id, ActingUser actingUser, string userName)
    {
        Id = id;
        UserId = actingUser.UserId;
        Role = actingUser.Role;
        UserName = userName;
    }

    public string Id { get; }
    public string UserId { get; }
    public string Role { get; private set; }
    public string UserName { get; private set; }
    public List<NormalizedLlmMessage> Messages { get; } = new();
    public SemaphoreSlim ProcessingLock { get; } = new(1, 1);
    public object SyncRoot { get; } = new();
    public PendingAgentTool? PendingTool { get; set; }
    public PendingAgentConfirmation? PendingConfirmation { get; set; }
    public TaskCompletionSource<string>? ToolResult { get; set; }
    public TaskCompletionSource<bool>? ConfirmationResult { get; set; }

    public void UpdateIdentity(ActingUser actingUser, string userName)
    {
        Role = actingUser.Role;
        UserName = userName;
    }
}

public sealed record PendingAgentTool(string ToolCallId, string Name, string ArgumentsJson);
public sealed record PendingAgentConfirmation(string ActionId, string ToolCallId, string Summary);

public interface IAgentSessionStore
{
    AgentSession GetOrCreate(string sessionId, ActingUser actingUser, string userName);
    AgentSession? Get(string sessionId, ActingUser actingUser);
}
