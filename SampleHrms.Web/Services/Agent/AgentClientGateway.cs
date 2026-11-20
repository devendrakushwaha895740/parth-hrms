using System.Text.Json;

namespace SampleHrms.Web.Services.Agent;

public interface IAgentClientGateway
{
    Task AssistantTextAsync(AgentSession session, string text);
    Task StepLogAsync(AgentSession session, string text, bool speakInVoice = false, bool isSlowNotice = false);
    Task StateChangedAsync(AgentSession session, string state);
    Task ErrorAsync(AgentSession session, string message);
    Task<string> RequestToolAsync(AgentSession session, string toolCallId, string name, JsonElement arguments);
    Task<bool> ConfirmActionAsync(AgentSession session, string toolCallId, string summary);
    Task ResumeAsync(AgentSession session);
}

public interface IAgentAuditLogger
{
    Task LogToolCallAsync(ActingUser actingUser, string toolName, string arguments, string result);
}
