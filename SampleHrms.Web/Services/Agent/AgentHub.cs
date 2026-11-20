using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using SampleHrms.Web.Models;
using SampleHrms.Web.Services;

namespace SampleHrms.Web.Services.Agent;

[Authorize]
public sealed class AgentHub : Hub
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAgentSessionStore _sessions;
    private readonly IAgentClientGateway _client;
    private readonly AgentOrchestrator _orchestrator;
    private readonly AgentRateLimiter _rateLimiter;

    public AgentHub(
        UserManager<ApplicationUser> users,
        IAgentSessionStore sessions,
        IAgentClientGateway client,
        AgentOrchestrator orchestrator,
        AgentRateLimiter rateLimiter)
    {
        _users = users;
        _sessions = sessions;
        _client = client;
        _orchestrator = orchestrator;
        _rateLimiter = rateLimiter;
    }

    public async Task SendMessage(string sessionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000)
        {
            await Clients.Caller.SendAsync("Error", "Please send a message under 4,000 characters.");
            return;
        }
        var user = await GetActingUserAsync();
        if (user == null) return;
        if (!_rateLimiter.TryConsume(user.Value.ActingUser.UserId))
        {
            await Clients.Caller.SendAsync("Error", "You have sent too many messages. Please wait a minute and try again.");
            return;
        }
        if (!Guid.TryParse(sessionId, out var parsed))
        {
            await Clients.Caller.SendAsync("Error", "Invalid assistant session.");
            return;
        }
        AgentSession session;
        try
        {
            session = _sessions.GetOrCreate(parsed.ToString("D"), user.Value.ActingUser, user.Value.UserName);
        }
        catch (UnauthorizedAccessException)
        {
            await Clients.Caller.SendAsync("Error", "This assistant session belongs to another user.");
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, session.Id);
        await _orchestrator.HandleMessageAsync(session, user.Value.ActingUser, text.Trim());
    }

    public async Task SubmitToolResult(string sessionId, string toolCallId, string result)
    {
        var user = await GetActingUserAsync();
        if (user == null) return;
        var session = _sessions.Get(sessionId, user.Value.ActingUser);
        if (session == null || result == null || result.Length > 20000) return;

        lock (session.SyncRoot)
        {
            if (session.PendingTool?.ToolCallId == toolCallId && session.ToolResult != null)
                session.ToolResult.TrySetResult(result);
        }
    }

    public async Task ConfirmAction(string sessionId, string actionId, bool approved)
    {
        var user = await GetActingUserAsync();
        if (user == null) return;
        var session = _sessions.Get(sessionId, user.Value.ActingUser);
        if (session == null) return;

        lock (session.SyncRoot)
        {
            if (session.PendingConfirmation?.ActionId == actionId && session.ConfirmationResult != null)
                session.ConfirmationResult.TrySetResult(approved);
        }
    }

    public async Task Resume(string sessionId)
    {
        var user = await GetActingUserAsync();
        if (user == null) return;
        var session = _sessions.Get(sessionId, user.Value.ActingUser);
        if (session == null)
        {
            await Clients.Caller.SendAsync("Error", "This assistant session expired. Please start a new message.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, session.Id);
        var hasPendingAction = session.PendingTool != null || session.PendingConfirmation != null;
        await _client.StateChangedAsync(session, hasPendingAction ? "acting" : "idle");
        await _client.ResumeAsync(session);
    }

    private async Task<(ActingUser ActingUser, string UserName)?> GetActingUserAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) throw new HubException("Authentication is required.");
        var user = await _users.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            await Clients.Caller.SendAsync("Error", "Your account is inactive.");
            return null;
        }

        var roles = await _users.GetRolesAsync(user);
        var role = roles.Contains("Admin") ? "Admin" : roles.Contains("HR") ? "HR" :
            roles.Contains("Employee") ? "Employee" : "";
        if (role.Length == 0)
        {
            await Clients.Caller.SendAsync("Error", "Your account does not have an HRMS role.");
            return null;
        }
        return (new ActingUser(user.Id, role, "AI"), user.FullName);
    }
}
