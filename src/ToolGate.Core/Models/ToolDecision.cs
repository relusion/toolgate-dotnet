namespace ToolGate.Core.Models;

/// <summary>
/// The result of evaluating a tool call through the policy pipeline.
/// Contains the aggregated decision, reason, matched policies, optional approval request,
/// and evaluation timing.
/// </summary>
public class ToolDecision
{
    /// <summary>The final aggregated decision (Allow, Deny, or RequireApproval).</summary>
    public required Decision Decision { get; init; }

    /// <summary>Machine-readable reason code for the decision.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Human-readable explanation of the decision.</summary>
    public string? ReasonText { get; init; }

    /// <summary>All policies that produced a non-Abstain outcome, in evaluation order.</summary>
    public IReadOnlyList<PolicyMatch> PolicyMatches { get; init; } = [];

    /// <summary>
    /// If the decision is <see cref="Decision.RequireApproval"/>, contains the approval request
    /// details including the approval ID and expiration time.
    /// </summary>
    public ApprovalInfo? ApprovalInfo { get; init; }

    /// <summary>When the evaluation completed.</summary>
    public DateTimeOffset EvaluatedAt { get; init; }

    /// <summary>How long the evaluation took.</summary>
    public TimeSpan EvaluationDuration { get; init; }
}

/// <summary>
/// Lightweight approval information embedded in a <see cref="ToolDecision"/>.
/// Contains the minimal fields needed for the caller to track the approval request.
/// </summary>
public class ApprovalInfo
{
    /// <summary>The unique identifier for the approval request.</summary>
    public required string ApprovalId { get; init; }

    /// <summary>When the approval request expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Optional metadata from external workflow integrations (e.g., JIRA ticket IDs, ServiceNow request numbers).
    /// String keys and values for serialization compatibility. Flows through to the LLM via the adapter.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
