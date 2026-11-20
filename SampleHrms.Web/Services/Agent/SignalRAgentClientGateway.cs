using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

namespace SampleHrms.Web.Services.Agent;

public sealed class SignalRAgentClientGateway : IAgentClientGateway
{
    private readonly IHubContext<AgentHub> _hub;

    public SignalRAgentClientGateway(IHubContext<AgentHub> hub) => _hub = hub;

    public Task AssistantTextAsync(AgentSession session, string text) =>
        _hub.Clients.Group(session.Id).SendAsync("AssistantText", text);

    public Task StepLogAsync(AgentSession session, string text, bool speakInVoice = false, bool isSlowNotice = false) =>
        _hub.Clients.Group(session.Id).SendAsync("StepLog", text, speakInVoice, isSlowNotice);

    public Task StateChangedAsync(AgentSession session, string state) =>
        _hub.Clients.Group(session.Id).SendAsync("StateChanged", state);

    public Task ErrorAsync(AgentSession session, string message) =>
        _hub.Clients.Group(session.Id).SendAsync("Error", message);

    public async Task<string> RequestToolAsync(AgentSession session, string toolCallId, string name, JsonElement arguments)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (session.SyncRoot)
        {
            session.PendingTool = new PendingAgentTool(toolCallId, name, arguments.GetRawText());
            session.ToolResult = completion;
        }

        try
        {
            await _hub.Clients.Group(session.Id).SendAsync("ToolRequest", toolCallId, name, arguments);
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(5));
        }
        finally
        {
            lock (session.SyncRoot)
            {
                if (ReferenceEquals(session.ToolResult, completion))
                {
                    session.PendingTool = null;
                    session.ToolResult = null;
                }
            }
        }
    }

    public async Task<bool> ConfirmActionAsync(AgentSession session, string toolCallId, string summary)
    {
        var actionId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (session.SyncRoot)
        {
            session.PendingConfirmation = new PendingAgentConfirmation(actionId, toolCallId, summary);
            session.ConfirmationResult = completion;
        }

        try
        {
            await _hub.Clients.Group(session.Id).SendAsync("ConfirmRequest", actionId, summary);
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(5));
        }
        finally
        {
            lock (session.SyncRoot)
            {
                if (ReferenceEquals(session.ConfirmationResult, completion))
                {
                    session.PendingConfirmation = null;
                    session.ConfirmationResult = null;
                }
            }
        }
    }

    public async Task ResumeAsync(AgentSession session)
    {
        PendingAgentTool? pendingTool;
        PendingAgentConfirmation? pendingConfirmation;
        lock (session.SyncRoot)
        {
            pendingTool = session.PendingTool;
            pendingConfirmation = session.PendingConfirmation;
        }

        if (pendingConfirmation != null)
            await _hub.Clients.Group(session.Id).SendAsync("ConfirmRequest", pendingConfirmation.ActionId, pendingConfirmation.Summary);
        if (pendingTool != null)
        {
            using var arguments = JsonDocument.Parse(pendingTool.ArgumentsJson);
            await _hub.Clients.Group(session.Id).SendAsync("ToolRequest", pendingTool.ToolCallId, pendingTool.Name, arguments.RootElement.Clone());
        }
    }
}
