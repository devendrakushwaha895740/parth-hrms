using Microsoft.AspNetCore.Identity;
using SampleHrms.Web.Models;
using SampleHrms.Web.Services;

namespace SampleHrms.Web.Services.Agent;

public sealed record AgentIdentity(ActingUser ActingUser, string FullName);

public interface IAgentIdentityResolver
{
    Task<AgentIdentity?> ResolveAsync(string userId);
}

public sealed class AgentIdentityResolver : IAgentIdentityResolver
{
    private readonly UserManager<ApplicationUser> _users;

    public AgentIdentityResolver(UserManager<ApplicationUser> users) => _users = users;

    public async Task<AgentIdentity?> ResolveAsync(string userId)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null || !user.IsActive) return null;

        var roles = await _users.GetRolesAsync(user);
        var role = roles.Contains("Admin") ? "Admin" : roles.Contains("HR") ? "HR" :
            roles.Contains("Employee") ? "Employee" : "";
        return role.Length == 0 ? null : new AgentIdentity(new ActingUser(user.Id, role, "AI"), user.FullName);
    }
}
