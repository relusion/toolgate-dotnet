using System.Security.Claims;

namespace ToolGate.Core.Models;

/// <summary>
/// Represents the context of a tool call being evaluated by the policy pipeline.
/// Contains the tool name, arguments, identity, and optional metadata.
/// </summary>
public class ToolCallContext
{
    /// <summary>
    /// The name of the tool being invoked (e.g., "get_weather", "send_email").
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// A unique identifier for this specific tool call invocation.
    /// Used for approval correlation and resume-after-approval.
    /// </summary>
    public required string InvocationId { get; init; }

    /// <summary>
    /// When this tool call was initiated.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The arguments passed to the tool call. Matches MEAI's
    /// <c>FunctionCallContent.Arguments</c> type for zero-copy mapping.
    /// </summary>
    public IDictionary<string, object?>? Arguments { get; init; }

    /// <summary>
    /// Correlation ID for tracing the tool call across system boundaries.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Conversation or session ID grouping related tool calls.
    /// </summary>
    public string? ConversationId { get; init; }

    /// <summary>
    /// Optional user identifier for identity-based policy decisions.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Optional claims principal for rich identity-based policy decisions.
    /// </summary>
    public ClaimsPrincipal? Principal { get; init; }

    /// <summary>
    /// The framework that originated this tool call (e.g., "MEAI", "SemanticKernel").
    /// </summary>
    public string? Framework { get; init; }

    /// <summary>
    /// Optional agent identifier when multiple agents are in use.
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// The model that generated this tool call request.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// The LLM provider (e.g., "OpenAI", "Azure", "Anthropic").
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Optional tool metadata including risk level, tags, and categorization.
    /// </summary>
    public ToolMetadata? Metadata { get; init; }
}
