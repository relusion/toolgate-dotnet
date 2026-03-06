namespace ToolGate.Core.Models;

/// <summary>
/// Records that a specific policy produced a non-Abstain decision during evaluation.
/// Collected in <see cref="ToolDecision.PolicyMatches"/> for audit and diagnostics.
/// </summary>
public class PolicyMatch
{
    /// <summary>The name of the policy that produced this match.</summary>
    public required string PolicyName { get; init; }

    /// <summary>The decision returned by the policy.</summary>
    public required PolicyDecision Decision { get; init; }

    /// <summary>Machine-readable reason code from the policy.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Human-readable reason from the policy.</summary>
    public string? ReasonText { get; init; }
}
