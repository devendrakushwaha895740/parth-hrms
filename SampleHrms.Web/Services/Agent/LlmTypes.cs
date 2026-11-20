using System.Text.Json;

namespace SampleHrms.Web.Services.Agent;

public enum LlmMessageKind
{
    Text,
    ToolCall,
    ToolResult
}

public sealed record NormalizedLlmMessage
{
    public required LlmMessageKind Kind { get; init; }
    public required string Role { get; init; }
    public string? Text { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public JsonElement? ToolArguments { get; init; }
    public JsonElement? RawAssistantMessage { get; init; }

    public static NormalizedLlmMessage User(string text) => new() { Kind = LlmMessageKind.Text, Role = "user", Text = text };
    public static NormalizedLlmMessage Assistant(string text) => new() { Kind = LlmMessageKind.Text, Role = "assistant", Text = text };
    public static NormalizedLlmMessage AssistantToolCalls(JsonElement raw, string? text) =>
        new() { Kind = LlmMessageKind.ToolCall, Role = "assistant", Text = text, RawAssistantMessage = raw.Clone() };
    public static NormalizedLlmMessage ToolResult(string callId, string name, string text) =>
        new() { Kind = LlmMessageKind.ToolResult, Role = "tool", ToolCallId = callId, ToolName = name, Text = text };
}

public sealed record LlmToolCall(string Id, string Name, string ArgumentsJson, JsonElement? Arguments, string? ParseError);

public sealed record LlmResponse(
    bool Success,
    string? Text,
    IReadOnlyList<LlmToolCall> ToolCalls,
    JsonElement? RawAssistantMessage,
    string? Error)
{
    public static LlmResponse Failure(string message) => new(false, null, Array.Empty<LlmToolCall>(), null, message);
}

public sealed record LlmToolDefinition(string Name, string Description, JsonElement Parameters);

public interface ILlmProvider
{
    bool IsConfigured { get; }
    Task<LlmResponse> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<NormalizedLlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken,
        Func<string, Task>? onRateLimited = null,
        Action<string>? onModelSelected = null);
}

public sealed class OpenRouterOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string FallbackModel { get; set; } = "";
    public int MaxTokens { get; set; } = 1024;
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class ParthOptions
{
    public SlowNoticeOptions SlowNotice { get; set; } = new();
}

public sealed class SlowNoticeOptions
{
    public bool Enabled { get; set; } = true;
    public int AfterSeconds { get; set; } = 7;
    public string FreeModelMessage { get; set; } =
        "Mujhe thoda samay lag raha hai kyunki abhi main demo ke liye free AI model use kar raha hu. Paid model me ye kaafi tez chalega.";
    public string GenericMessage { get; set; } = "Thoda samay lag raha hai, bas ek minute...";
    public string EnglishFreeModelMessage { get; set; } =
        "This is taking a little longer because I'm using a free AI model for this demo. A paid model should be much faster.";
    public string EnglishGenericMessage { get; set; } = "This is taking a little longer. Just a moment...";
}

public sealed class LlmOptions
{
    public string Provider { get; set; } = "OpenRouter";
}
