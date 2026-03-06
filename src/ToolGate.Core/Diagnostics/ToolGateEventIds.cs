namespace ToolGate.Core.Diagnostics;

/// <summary>
/// Stable event IDs for ToolGate structured log entries.
/// </summary>
/// <remarks>
/// Ranges: Core 1000-1999, Policies 2000-2999, Approvals 3000-3999, Adapters 4000-4999.
/// </remarks>
public static class ToolGateEventIds
{
    // ── Core 1000-1999 ──────────────────────────────────────────────────────

    /// <summary>Policy evaluation started for a tool call.</summary>
    public const int EvaluationStarted = 1001;

    /// <summary>Policy evaluation completed.</summary>
    public const int EvaluationCompleted = 1002;

    /// <summary>Tool call denied by a policy.</summary>
    public const int EvaluationDenied = 1003;

    /// <summary>Tool call allowed by the policy pipeline.</summary>
    public const int EvaluationAllowed = 1004;

    /// <summary>Tool call requires human approval.</summary>
    public const int EvaluationRequiresApproval = 1005;

    /// <summary>No policy matched; the default decision was applied.</summary>
    public const int EvaluationDefaultApplied = 1006;

    /// <summary>Tool call allowed due to a prior approved approval request.</summary>
    public const int EvaluationApprovalBypassed = 1007;

    /// <summary>A policy threw an exception during evaluation.</summary>
    public const int PolicyException = 1010;

    /// <summary>Fail mode (FailOpen/FailClosed) was activated due to a policy error.</summary>
    public const int FailModeActivated = 1011;

    /// <summary>An audit event was recorded by the audit sink.</summary>
    public const int AuditEventRecorded = 1020;

    /// <summary>The audit sink threw an exception (swallowed to not block evaluation).</summary>
    public const int AuditSinkError = 1099;

    // ── Policies 2000-2999 ──────────────────────────────────────────────────

    /// <summary>A policy was evaluated and returned a non-Abstain outcome.</summary>
    public const int PolicyEvaluated = 2001;

    /// <summary>The denylist policy matched a tool name or pattern.</summary>
    public const int DenylistMatched = 2010;

    /// <summary>The allowlist policy matched a tool name.</summary>
    public const int AllowlistMatched = 2020;

    /// <summary>The high-risk approval policy detected a high-risk tool.</summary>
    public const int HighRiskDetected = 2030;

    // ── Approvals 3000-3999 ─────────────────────────────────────────────────

    /// <summary>A new approval request was created.</summary>
    public const int ApprovalCreated = 3001;

    /// <summary>An approval request was approved.</summary>
    public const int ApprovalApproved = 3002;

    /// <summary>An approval request was denied.</summary>
    public const int ApprovalDenied = 3003;

    /// <summary>An approval request expired (TTL exceeded).</summary>
    public const int ApprovalExpired = 3004;

    /// <summary>An approval request was cancelled.</summary>
    public const int ApprovalCancelled = 3005;

    /// <summary>An approval was found via content hash matching (cross-turn resume).</summary>
    public const int ApprovalContentHashMatched = 3006;

    /// <summary>An approval state transition failed (already transitioned or expired).</summary>
    public const int ApprovalTransitionFailed = 3010;

    // ── Adapters 4000-4999 ──────────────────────────────────────────────────

    /// <summary>The MEAI adapter intercepted tool call(s) in a chat response.</summary>
    public const int AdapterToolCallIntercepted = 4001;

    /// <summary>The MEAI adapter blocked a tool call (replaced with denial result).</summary>
    public const int AdapterToolCallBlocked = 4002;

    /// <summary>The MEAI adapter allowed a tool call to proceed.</summary>
    public const int AdapterToolCallAllowed = 4003;

    /// <summary>The MEAI adapter flagged a tool call as requiring approval.</summary>
    public const int AdapterApprovalRequired = 4004;

    /// <summary>No tool calls were found in the chat response.</summary>
    public const int AdapterNoToolCalls = 4005;

    /// <summary>All tool calls were blocked; requesting LLM feedback text.</summary>
    public const int AdapterLlmFeedbackRequested = 4006;

    /// <summary>LLM feedback call failed; returning response with denial results only.</summary>
    public const int AdapterLlmFeedbackFailed = 4007;

    /// <summary>Streaming response was requested; tool calls are not evaluated in v0.1.</summary>
    public const int AdapterStreamingNotEvaluated = 4010;
}
