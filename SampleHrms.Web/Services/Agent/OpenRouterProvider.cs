using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace SampleHrms.Web.Services.Agent;

public sealed class OpenRouterProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterOptions _options;

    public OpenRouterProvider(HttpClient httpClient, IOptions<OpenRouterOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.Model);

    public async Task<LlmResponse> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<NormalizedLlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken,
        Func<string, Task>? onRateLimited = null,
        Action<string>? onModelSelected = null)
    {
        if (!IsConfigured) return LlmResponse.Failure("AI is not configured yet");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 180)));
        try
        {
            var primary = await SendWithRetryAsync(_options.Model, systemPrompt, messages, tools, timeout.Token,
                onRateLimited, onModelSelected);
            if (primary.Success || IsCredentialOrCreditFailure(primary.Error)) return primary;

            var fallbackModel = _options.FallbackModel.Trim();
            if (fallbackModel.Length == 0 || fallbackModel.Equals(_options.Model.Trim(), StringComparison.Ordinal))
                return primary;

            return await SendWithRetryAsync(fallbackModel, systemPrompt, messages, tools, timeout.Token,
                onRateLimited, onModelSelected);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LlmResponse.Failure("OpenRouter request timed out. Please try again.");
        }
        catch (HttpRequestException)
        {
            return LlmResponse.Failure("OpenRouter could not be reached. Please try again.");
        }
        catch (JsonException)
        {
            return LlmResponse.Failure("OpenRouter returned an invalid response. Please try again.");
        }
    }

    private static bool IsCredentialOrCreditFailure(string? error) =>
        error?.StartsWith("OpenRouter API key is invalid", StringComparison.Ordinal) == true ||
        error?.StartsWith("OpenRouter credits are finished", StringComparison.Ordinal) == true;

    private async Task<LlmResponse> SendWithRetryAsync(
        string model,
        string systemPrompt,
        IReadOnlyList<NormalizedLlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken,
        Func<string, Task>? onRateLimited,
        Action<string>? onModelSelected)
    {
        try
        {
            onModelSelected?.Invoke(model);
            var response = await SendAsync(model, systemPrompt, messages, tools, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && onRateLimited != null)
                await onRateLimited(model);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken);
                response = await SendAsync(model, systemPrompt, messages, tools, cancellationToken);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && onRateLimited != null)
                    await onRateLimited(model);
            }
            return response.Result;
        }
        catch (HttpRequestException)
        {
            return LlmResponse.Failure("OpenRouter could not be reached. Please try again.");
        }
        catch (JsonException)
        {
            return LlmResponse.Failure("OpenRouter returned an invalid response. Please try again.");
        }
    }

    private async Task<(HttpStatusCode StatusCode, LlmResponse Result)> SendAsync(
        string model,
        string systemPrompt,
        IReadOnlyList<NormalizedLlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "http://localhost:5037");
        request.Headers.TryAddWithoutValidation("X-Title", "SampleHrms Parth");
        request.Content = JsonContent.Create(CreateRequest(model, systemPrompt, messages, tools));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return (response.StatusCode, LlmResponse.Failure(GetStatusMessage(response.StatusCode)));

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var choice = document.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
            ? content.GetString()
            : null;
        var calls = new List<LlmToolCall>();

        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in toolCalls.EnumerateArray())
            {
                var callId = call.GetProperty("id").GetString() ?? "";
                var function = call.GetProperty("function");
                var name = function.GetProperty("name").GetString() ?? "";
                var argumentJson = function.GetProperty("arguments").GetString() ?? "";
                try
                {
                    using var argumentsDocument = JsonDocument.Parse(argumentJson);
                    calls.Add(new LlmToolCall(callId, name, argumentJson, argumentsDocument.RootElement.Clone(), null));
                }
                catch (JsonException)
                {
                    calls.Add(new LlmToolCall(callId, name, argumentJson, null, "Tool arguments were not valid JSON."));
                }
            }
        }

        return (response.StatusCode, new LlmResponse(true, text, calls, message.Clone(), null));
    }

    private JsonObject CreateRequest(
        string model,
        string systemPrompt,
        IReadOnlyList<NormalizedLlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools)
    {
        var requestMessages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt }
        };

        foreach (var message in messages)
        {
            if (message.Kind == LlmMessageKind.ToolCall && message.RawAssistantMessage.HasValue)
            {
                requestMessages.Add(JsonNode.Parse(message.RawAssistantMessage.Value.GetRawText()));
                continue;
            }

            var item = new JsonObject { ["role"] = message.Role };
            if (message.Kind == LlmMessageKind.ToolResult)
            {
                item["tool_call_id"] = message.ToolCallId;
                item["content"] = message.Text;
            }
            else
            {
                item["content"] = message.Text;
            }
            requestMessages.Add(item);
        }

        var toolArray = new JsonArray();
        foreach (var tool in tools)
        {
            toolArray.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText())
                }
            });
        }

        return new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = Math.Clamp(_options.MaxTokens, 1, 4096),
            ["messages"] = requestMessages,
            ["tools"] = toolArray,
            ["tool_choice"] = "auto"
        };
    }

    private static string GetStatusMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "OpenRouter API key is invalid.",
        HttpStatusCode.PaymentRequired => "OpenRouter credits are finished or billing is unavailable.",
        HttpStatusCode.TooManyRequests => "OpenRouter is rate limited. Please try again shortly.",
        >= HttpStatusCode.InternalServerError => "OpenRouter is temporarily unavailable. Please try again.",
        _ => $"OpenRouter request failed (HTTP {(int)statusCode})."
    };
}

public sealed class ClaudeProvider : ILlmProvider
{
    public bool IsConfigured => false;
    public Task<LlmResponse> CompleteAsync(string systemPrompt, IReadOnlyList<NormalizedLlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools, CancellationToken cancellationToken,
        Func<string, Task>? onRateLimited = null, Action<string>? onModelSelected = null) =>
        Task.FromResult(LlmResponse.Failure("Claude provider is not configured yet."));
}

public sealed class GeminiProvider : ILlmProvider
{
    public bool IsConfigured => false;
    public Task<LlmResponse> CompleteAsync(string systemPrompt, IReadOnlyList<NormalizedLlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools, CancellationToken cancellationToken,
        Func<string, Task>? onRateLimited = null, Action<string>? onModelSelected = null) =>
        Task.FromResult(LlmResponse.Failure("Gemini provider is not configured yet."));
}
