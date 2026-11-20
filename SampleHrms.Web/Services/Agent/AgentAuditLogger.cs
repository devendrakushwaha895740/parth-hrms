using SampleHrms.Web.Data;
using SampleHrms.Web.Models;

namespace SampleHrms.Web.Services.Agent;

public sealed class AgentAuditLogger : IAgentAuditLogger
{
    private readonly ApplicationDbContext _db;

    public AgentAuditLogger(ApplicationDbContext db) => _db = db;

    public async Task LogToolCallAsync(ActingUser actingUser, string toolName, string arguments, string result)
    {
        var safeArguments = arguments;
        if (toolName == "ui_fill" && arguments.Contains("employee-password", StringComparison.Ordinal))
            safeArguments = "[REDACTED: account password field]";
        var details = $"Tool={toolName}; Args={Trim(safeArguments)}; Result={Trim(result)}";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actingUser.UserId,
            Action = "AgentToolCall",
            Details = details,
            Source = "AI"
        });
        await _db.SaveChangesAsync();
    }

    private static string Trim(string value) => value.Length <= 6000 ? value : value[..6000];
}
