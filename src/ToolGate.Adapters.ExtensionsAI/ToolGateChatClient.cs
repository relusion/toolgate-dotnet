using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Diagnostics;
using ToolGate.Core.Models;

namespace ToolGate.Adapters.ExtensionsAI;

/// <summary>
/// MEAI adapter that intercepts <see cref="FunctionCallContent"/> in chat responses
/// and evaluates them against the ToolGate policy pipeline.
/// Denied calls are marked <see cref="FunctionCallContent.InformationalOnly"/> and
/// a proper <see cref="FunctionResultContent"/> tool-role message is appended to the response.
/// When all tool calls are blocked and <see cref="LlmFeedbackEnabled"/> is <c>true</c>,
/// makes an additional LLM call so the model can explain the denial to the user.
/// </summary>
public sealed partial class ToolGateChatClient : DelegatingChatClient
{
    private readonly IToolGate _toolGate;
    private readonly ILogger<ToolGateChatClient> _logger;

    /// <summary>
    /// When <c>true</c> (default), if all tool calls in a response are blocked,
    /// makes an additional LLM call so the model can explain the denial to the user.
    /// Set to <c>false</c> to disable the extra call and return denial results only.
    /// </summary>
    public bool LlmFeedbackEnabled { get; set; } = true;

    /// <summary>
    /// Initializes a new <see cref="ToolGateChatClient"/> wrapping the specified inner client.
    /// </summary>
    /// <param name="innerClient">The inner chat client to delegate to.</param>
    /// <param name="toolGate">The ToolGate policy engine for evaluating tool calls.</param>
    /// <param name="logger">Logger for adapter events.</param>
    public ToolGateChatClient(
        IChatClient innerClient,
        IToolGate toolGate,
        ILogger<ToolGateChatClient> logger)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(toolGate);
        ArgumentNullException.ThrowIfNull(logger);
        _toolGate = toolGate;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        var result = await ProcessToolCallsAsync(response.Messages, options, cancellationToken);

        if (result.BlockedCount > 0)
        {
            // Append a tool-role message with all blocked results
            response.Messages.Add(new ChatMessage(
                ChatRole.Tool,
                result.BlockedResults.Cast<AIContent>().ToList()));

            // If ALL calls were blocked, re-call the LLM so it can explain the denial
            if (result.AllowedCount == 0 && LlmFeedbackEnabled)
            {
                await RequestLlmFeedbackAsync(messages, response, options, cancellationToken);
            }
        }

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // v0.1: Streaming tool call interception is not yet supported.
        // Tool calls in streaming responses will pass through without policy evaluation.
        LogStreamingNotEvaluated(_logger);

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    private async Task<ProcessingResult> ProcessToolCallsAsync(
        IList<ChatMessage> responseMessages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var blockedResults = new List<FunctionResultContent>();
        int allowedCount = 0;

        foreach (var message in responseMessages)
        {
            var functionCalls = message.Contents
                .OfType<FunctionCallContent>()
                .ToList();

            if (functionCalls.Count == 0)
                continue;

            LogToolCallsIntercepted(_logger, functionCalls.Count);

            foreach (var toolCall in functionCalls)
            {
                var context = BuildContext(toolCall, options);
                var decision = await _toolGate.EvaluateAsync(context, cancellationToken);

                switch (decision.Decision)
                {
                    case Decision.Allow:
                        LogToolCallAllowed(_logger, toolCall.Name);
                        allowedCount++;
                        break;

                    case Decision.Deny:
                        LogToolCallBlocked(_logger, toolCall.Name, decision.ReasonText);
                        blockedResults.Add(BlockFunctionCall(toolCall, decision,
                            $"[ToolGate:Denied] {decision.ReasonText ?? "Tool call denied by policy"}"));
                        break;

                    case Decision.RequireApproval:
                    {
                        var approvalId = decision.ApprovalInfo?.ApprovalId ?? "unknown";
                        LogApprovalRequired(_logger, toolCall.Name, approvalId);
                        var resultText = $"[ToolGate:ApprovalRequired] {decision.ReasonText ?? "Tool call requires approval"}. ApprovalId: {approvalId}";
                        if (decision.ApprovalInfo?.Metadata is { Count: > 0 } metadata)
                        {
                            var metadataText = string.Join(", ", metadata.Select(kv => $"{kv.Key}: {kv.Value}"));
                            resultText += $". Metadata: {metadataText}";
                        }
                        blockedResults.Add(BlockFunctionCall(toolCall, decision, resultText));
                        break;
                    }
                }
            }
        }

        return new ProcessingResult(allowedCount, blockedResults.Count, blockedResults);
    }

    /// <summary>
    /// Marks a tool call as informational-only (prevents execution by FunctionInvokingChatClient)
    /// and creates a <see cref="FunctionResultContent"/> with the denial/approval text.
    /// </summary>
    private static FunctionResultContent BlockFunctionCall(
        FunctionCallContent toolCall,
        ToolDecision decision,
        string resultText)
    {
        toolCall.InformationalOnly = true;

        return new FunctionResultContent(toolCall.CallId, toolCall.Name)
        {
            Result = resultText
        };
    }

    /// <summary>
    /// Makes an additional LLM call with the denial context so the model can
    /// generate a natural-language explanation for the user.
    /// </summary>
    private async Task RequestLlmFeedbackAsync(
        IEnumerable<ChatMessage> originalMessages,
        ChatResponse response,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        try
        {
            LogLlmFeedbackRequested(_logger);

            // Build conversation: original messages + response messages (assistant + tool results)
            var feedbackMessages = originalMessages.ToList();
            feedbackMessages.AddRange(response.Messages);

            // Clone options and strip tools to prevent the LLM from making more tool calls
            var feedbackOptions = options?.Clone() ?? new ChatOptions();
            feedbackOptions.Tools = null;

            var feedbackResponse = await base.GetResponseAsync(
                feedbackMessages, feedbackOptions, cancellationToken);

            foreach (var msg in feedbackResponse.Messages)
                response.Messages.Add(msg);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLlmFeedbackFailed(_logger, ex);
        }
    }

    private static ToolCallContext BuildContext(FunctionCallContent toolCall, ChatOptions? options)
    {
        return new ToolCallContext
        {
            ToolName = toolCall.Name,
            InvocationId = toolCall.CallId,
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = toolCall.Arguments,
            CorrelationId = Activity.Current?.Id,
            Framework = "MEAI",
            ModelId = options?.ModelId
        };
    }

    private readonly record struct ProcessingResult(
        int AllowedCount,
        int BlockedCount,
        List<FunctionResultContent> BlockedResults);

    // Source-generated log messages

    [LoggerMessage(
        EventId = ToolGateEventIds.AdapterToolCallIntercepted,
        Level = LogLevel.Debug,
        Message = "Intercepted {Count} tool call(s) in chat response")]
    private static partial void LogToolCallsIntercepted(ILogger logger, int count);

    [LoggerMessage(
        EventId = ToolGateEventIds.AdapterToolCallAllowed,
        Level = LogLevel.Debug,
        Message = "Tool call '{ToolName}' allowed")]
    private static partial void LogToolCallAllowed(ILogger logger, string toolName);

    [LoggerMessage(
        EventId = ToolGateEventIds.AdapterToolCallBlocked,
        Level = LogLevel.Information,
        Message = "Tool call '{ToolName}' blocked: {Reason}")]
    private static partial void LogToolCallBlocked(ILogger logger, string toolName, string? reason);

    [LoggerMessage(
        EventId = ToolGateEventIds.AdapterApprovalRequired,
        Level = LogLevel.Information,
        Message = "Tool call '{ToolName}' requires approval (id: {ApprovalId})")]
    private static partial void LogApprovalRequired(ILogger logger, string toolName, string? approvalId);

    [LoggerMessage(
        EventId = ToolGateEventIds.AdapterStreamingNotEvaluated,
        Level = LogLevel.Warning,
        Message = "Streaming response requested — tool calls in streaming are not evaluated by ToolGate in v0.1")]
    private static partial void LogStreamingNotEvaluated(ILogger logger);

    [LoggerMessage(
        EventId = ToolGateEventIds.AdapterLlmFeedbackRequested,
        Level = LogLevel.Debug,
        Message = "All tool calls blocked — requesting LLM feedback text")]
    private static partial void LogLlmFeedbackRequested(ILogger logger);

    [LoggerMessage(
        EventId = ToolGateEventIds.AdapterLlmFeedbackFailed,
        Level = LogLevel.Warning,
        Message = "LLM feedback call failed — returning response with denial results only")]
    private static partial void LogLlmFeedbackFailed(ILogger logger, Exception exception);
}
