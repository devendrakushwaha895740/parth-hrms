using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SampleHrms.Web.Services;
using SampleHrms.Web.Services.Agent;

namespace SampleHrms.Tests;

public class AgentOrchestratorTests
{
    [Fact]
    public void ToolCatalog_FiltersDataAndNavigationToolsByRole()
    {
        var catalog = new AgentToolCatalog();
        var employeeTools = catalog.GetTools("Employee");
        var hrTools = catalog.GetTools("HR");
        var employeeNavigation = employeeTools.Single(x => x.Name == "ui_navigate").Parameters.GetRawText();

        Assert.Contains(employeeTools, x => x.Name == "get_my_leave_history");
        Assert.DoesNotContain(employeeTools, x => x.Name == "list_pending_leaves");
        Assert.DoesNotContain(employeeTools, x => x.Name == "list_employees");
        Assert.DoesNotContain(hrTools, x => x.Name == "get_my_leave_history");
        Assert.Contains(hrTools, x => x.Name == "list_pending_leaves");
        Assert.False(employeeNavigation.Contains("leave-pending", StringComparison.Ordinal));

        using var args = JsonDocument.Parse("""{"pageKey":"attendance-manage"}""");
        Assert.False(catalog.IsAllowed(new ActingUser("emp-1", "Employee"), "ui_navigate", args.RootElement, out _));
        using var leaveTypesNav = JsonDocument.Parse("""{"agentId":"nav-leave-types"}""");
        Assert.False(catalog.IsAllowed(new ActingUser("emp-1", "Employee"), "ui_click", leaveTypesNav.RootElement, out _));
        using var adminField = JsonDocument.Parse("""{"agentId":"employee-password","value":"NotLogged"}""");
        Assert.False(catalog.IsAllowed(new ActingUser("emp-1", "Employee"), "ui_fill", adminField.RootElement, out _));
    }

    [Fact]
    public async Task FakeProvider_LeaveApplyToolSequence_ConfirmsBeforeSubmit()
    {
        var provider = new FakeLlmProvider(
            ToolCall("1", "ui_navigate", """{"pageKey":"leave-apply"}"""),
            ToolCall("2", "ui_read_page", "{}"),
            ToolCall("3", "ui_fill", """{"agentId":"leave-from-date","value":"2027-01-11"}"""),
            ToolCall("4", "ui_fill", """{"agentId":"leave-to-date","value":"2027-01-12"}"""),
            ToolCall("5", "ui_select", """{"agentId":"leave-type","value":"1"}"""),
            ToolCall("6", "ui_fill", """{"agentId":"leave-reason","value":"Family event"}"""),
            ToolCall("7", "ui_click", """{"agentId":"leave-submit"}"""),
            new LlmResponse(true, "Your leave request was submitted.", Array.Empty<LlmToolCall>(), null, null));
        var client = new FakeAgentClientGateway();
        var audit = new FakeAgentAuditLogger();
        var identity = new FakeAgentIdentityResolver();
        var orchestrator = CreateOrchestrator(provider, client, audit, identity, new ParthOptions(), "openrouter/test");
        var session = new AgentSession(Guid.NewGuid().ToString(), new ActingUser("emp-1", "Employee", "AI"), "Asha");

        await orchestrator.HandleMessageAsync(session, new ActingUser("emp-1", "Employee", "AI"), "Mujhe leave apply karni hai");

        Assert.Equal(8, provider.CallCount);
        Assert.Equal(new[] { "ui_navigate", "ui_read_page", "ui_fill", "ui_fill", "ui_select", "ui_fill", "ui_click" }, client.ToolNames);
        Assert.Equal(1, client.ConfirmationCount);
        Assert.True(client.Events.IndexOf("confirm") < client.Events.IndexOf("tool:ui_click"));
        Assert.Equal(7, audit.ToolNames.Count);
        Assert.Contains(client.AssistantMessages, text => text == "Your leave request was submitted.");
        Assert.Equal(7, session.Messages.Count(x => x.Kind == LlmMessageKind.ToolResult));
    }

    [Fact]
    public async Task SlowProvider_SendsOneNoticePerTask_AndRespectsDisabledSetting()
    {
        static ParthOptions CreateOptions(bool enabled) => new()
        {
            SlowNotice = new SlowNoticeOptions
            {
                Enabled = enabled,
                AfterSeconds = 0,
                FreeModelMessage = "slow free-model notice"
            }
        };

        async Task<FakeAgentClientGateway> RunTaskAsync(bool enabled)
        {
            var client = new FakeAgentClientGateway();
            var provider = new SlowFakeLlmProvider("openrouter/test:free", TimeSpan.FromMilliseconds(30),
                ToolCall("1", "get_leave_balances", "{}"),
                new LlmResponse(true, "Aapke leave balances yeh hain.", Array.Empty<LlmToolCall>(), null, null));
            var orchestrator = CreateOrchestrator(provider, client, new FakeAgentAuditLogger(),
                new FakeAgentIdentityResolver(), CreateOptions(enabled), "openrouter/test:free");
            var session = new AgentSession(Guid.NewGuid().ToString(), new ActingUser("emp-1", "Employee"), "Asha");

            await orchestrator.HandleMessageAsync(session, new ActingUser("emp-1", "Employee"), "Mere leave balances kya hain?");
            Assert.Equal(2, provider.CallCount);
            return client;
        }

        var enabledClient = await RunTaskAsync(enabled: true);
        var notice = Assert.Single(enabledClient.StepLogs.Where(x => x.IsSlowNotice));
        Assert.Equal("slow free-model notice", notice.Text);
        Assert.True(notice.SpeakInVoice);

        var disabledClient = await RunTaskAsync(enabled: false);
        Assert.DoesNotContain(disabledClient.StepLogs, x => x.IsSlowNotice);
    }

    private static AgentOrchestrator CreateOrchestrator(
        ILlmProvider provider,
        FakeAgentClientGateway client,
        FakeAgentAuditLogger audit,
        FakeAgentIdentityResolver identity,
        ParthOptions parthOptions,
        string model) =>
        new(provider, new FakeAgentToolExecutor(), new AgentToolCatalog(), client, audit, identity,
            NullLogger<AgentOrchestrator>.Instance, Options.Create(parthOptions),
            Options.Create(new OpenRouterOptions { Model = model }));

    private static LlmResponse ToolCall(string id, string name, string arguments)
    {
        using var argumentValue = JsonDocument.Parse(arguments);
        var messageJson = JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[]
            {
                new { id, type = "function", function = new { name, arguments } }
            }
        });
        using var message = JsonDocument.Parse(messageJson);
        return new LlmResponse(true, null,
            new[] { new LlmToolCall(id, name, arguments, argumentValue.RootElement.Clone(), null) },
            message.RootElement.Clone(), null);
    }

    private sealed class FakeLlmProvider(params LlmResponse[] responses) : ILlmProvider
    {
        private readonly Queue<LlmResponse> _responses = new(responses);
        public int CallCount { get; private set; }
        public bool IsConfigured => true;
        public Task<LlmResponse> CompleteAsync(string systemPrompt, IReadOnlyList<NormalizedLlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools, CancellationToken cancellationToken,
            Func<string, Task>? onRateLimited = null, Action<string>? onModelSelected = null)
        {
            CallCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class SlowFakeLlmProvider(
        string model,
        TimeSpan delay,
        params LlmResponse[] responses) : ILlmProvider
    {
        private readonly Queue<LlmResponse> _responses = new(responses);
        public int CallCount { get; private set; }
        public bool IsConfigured => true;

        public async Task<LlmResponse> CompleteAsync(string systemPrompt, IReadOnlyList<NormalizedLlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools, CancellationToken cancellationToken,
            Func<string, Task>? onRateLimited = null, Action<string>? onModelSelected = null)
        {
            CallCount++;
            onModelSelected?.Invoke(model);
            await Task.Delay(delay, cancellationToken);
            return _responses.Dequeue();
        }
    }

    private sealed class FakeAgentClientGateway : IAgentClientGateway
    {
        public List<string> ToolNames { get; } = new();
        public List<string> Events { get; } = new();
        public List<string> AssistantMessages { get; } = new();
        public List<StepLogCall> StepLogs { get; } = new();
        public int ConfirmationCount { get; private set; }

        public Task AssistantTextAsync(AgentSession session, string text) { AssistantMessages.Add(text); return Task.CompletedTask; }
        public Task StepLogAsync(AgentSession session, string text, bool speakInVoice = false, bool isSlowNotice = false)
        {
            StepLogs.Add(new StepLogCall(text, speakInVoice, isSlowNotice));
            return Task.CompletedTask;
        }
        public Task StateChangedAsync(AgentSession session, string state) => Task.CompletedTask;
        public Task ErrorAsync(AgentSession session, string message) => Task.CompletedTask;
        public Task<string> RequestToolAsync(AgentSession session, string toolCallId, string name, JsonElement arguments)
        {
            ToolNames.Add(name);
            Events.Add("tool:" + name);
            return Task.FromResult("""{"success":true,"page":"leave-apply","feedback":{"success":"Leave request submitted."}}""");
        }
        public Task<bool> ConfirmActionAsync(AgentSession session, string toolCallId, string summary)
        {
            ConfirmationCount++;
            Events.Add("confirm");
            return Task.FromResult(true);
        }
        public Task ResumeAsync(AgentSession session) => Task.CompletedTask;
    }

    private sealed record StepLogCall(string Text, bool SpeakInVoice, bool IsSlowNotice);

    private sealed class FakeAgentAuditLogger : IAgentAuditLogger
    {
        public List<string> ToolNames { get; } = new();
        public Task LogToolCallAsync(ActingUser actingUser, string toolName, string arguments, string result)
        { ToolNames.Add(toolName); return Task.CompletedTask; }
    }

    private sealed class FakeAgentIdentityResolver : IAgentIdentityResolver
    {
        public Task<AgentIdentity?> ResolveAsync(string userId) =>
            Task.FromResult<AgentIdentity?>(new AgentIdentity(new ActingUser(userId, "Employee", "AI"), "Asha"));
    }

    private sealed class FakeAgentToolExecutor : IAgentToolExecutor
    {
        public Task<string> ExecuteServerToolAsync(ActingUser actingUser, string name, JsonElement arguments) =>
            Task.FromResult("""{"success":true}""");
    }
}
