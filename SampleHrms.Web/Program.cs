using SampleHrms.Web.Services;
using SampleHrms.Web.Services.Agent;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHrmsServices(builder.Configuration);

var app = builder.Build();
var llmProvider = app.Services.GetRequiredService<IOptions<LlmOptions>>().Value.Provider;
var openRouter = app.Services.GetRequiredService<IOptions<OpenRouterOptions>>().Value;
if (llmProvider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase) &&
    (string.IsNullOrWhiteSpace(openRouter.ApiKey) || string.IsNullOrWhiteSpace(openRouter.Model)))
{
    app.Logger.LogWarning("Parth AI is not configured. Fill OpenRouter:ApiKey and OpenRouter:Model in appsettings.Development.json.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHub<AgentHub>("/agentHub").RequireAuthorization();

await app.Services.SeedHrmsAsync();
app.Run();
