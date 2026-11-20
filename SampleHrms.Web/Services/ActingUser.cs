namespace SampleHrms.Web.Services;

public record ActingUser(string UserId, string Role, string Source = "Web");
