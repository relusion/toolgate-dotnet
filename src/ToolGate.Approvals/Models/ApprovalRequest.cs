namespace ToolGate.Approvals.Models;

/// <summary>
/// Represents a human-in-the-loop approval request created when the policy pipeline
/// returns a RequireApproval decision.
/// </summary>
public class ApprovalRequest
{
    /// <summary>A unique, unguessable identifier for this approval request (GUID).</summary>
    public required string ApprovalId { get; init; }

    /// <summary>The invocation ID of the tool call that triggered this approval.</summary>
    public required string InvocationId { get; init; }

    /// <summary>The name of the tool that requires approval.</summary>
    public required string ToolName { get; init; }

    /// <summary>Redacted copy of the tool call arguments (sensitive values masked).</summary>
    public IDictionary<string, object?>? RedactedArguments { get; init; }

    /// <summary>The current state of this approval request. Only modified by the store.</summary>
    public ApprovalState State { get; internal set; }

    /// <summary>When this approval request was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When this approval request expires (CreatedAt + TTL).</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Correlation ID for distributed tracing.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The user or system that triggered the tool call.</summary>
    public string? RequestedBy { get; init; }

    /// <summary>The reason the tool call requires approval.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// SHA-256 hash of the tool name and arguments, used for content-based approval matching
    /// across conversation turns where the invocation ID changes.
    /// </summary>
    public string? ContentHash { get; init; }
}
