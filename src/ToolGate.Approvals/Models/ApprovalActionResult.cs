namespace ToolGate.Approvals.Models;

/// <summary>
/// The result of an approval state transition attempt.
/// </summary>
public class ApprovalActionResult
{
    /// <summary>Whether the state transition succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The current state after the transition attempt.</summary>
    public ApprovalState CurrentState { get; init; }

    /// <summary>If the transition failed, explains why (e.g., "Already approved", "Expired").</summary>
    public string? FailureReason { get; init; }
}
