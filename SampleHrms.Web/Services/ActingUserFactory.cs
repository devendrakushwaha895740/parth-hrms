using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace SampleHrms.Web.Services;

public static class ActingUserFactory
{
    public static ActingUser FromRequest(ClaimsPrincipal principal, HttpRequest request, string role)
    {
        var source = request.Headers["agent_source"].FirstOrDefault();
        if (source != "AI" && request.HasFormContentType)
            source = request.Form["agent_source"].FirstOrDefault();

        return new ActingUser(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", role,
            source == "AI" ? "AI" : "Web");
    }
}
