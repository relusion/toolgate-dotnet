namespace ToolGate.Core.Models;

/// <summary>
/// The result of a single policy evaluation, containing the decision and an optional reason.
/// Use the static factory methods to create instances.
/// </summary>
public class PolicyOutcome
{
    /// <summary>
    /// The decision made by the policy.
    /// </summary>
    public PolicyDecision Decision { get; init; }

    /// <summary>
    /// A machine-readable reason code (e.g., "Denylisted", "NotAllowlisted").
    /// </summary>
    public string? ReasonCode { get; init; }

    /// <summary>
    /// A human-readable explanation of the decision.
    /// </summary>
    public string? ReasonText { get; init; }

    /// <summary>Creates an Allow outcome with an optional reason.</summary>
    public static PolicyOutcome AllowResult(string? reason = null) => new()
    {
        Decision = PolicyDecision.Allow,
        ReasonText = reason
    };

    /// <summary>Creates a Deny outcome with a reason code and explanation.</summary>
    public static PolicyOutcome DenyResult(string code, string reason) => new()
    {
        Decision = PolicyDecision.Deny,
        ReasonCode = code,
        ReasonText = reason
    };

    /// <summary>Creates a RequireApproval outcome with an optional reason.</summary>
    public static PolicyOutcome RequireApprovalResult(string? reason = null) => new()
    {
        Decision = PolicyDecision.RequireApproval,
        ReasonText = reason
    };

    /// <summary>Creates an Abstain outcome — the policy has no opinion.</summary>
    public static PolicyOutcome AbstainResult() => new()
    {
        Decision = PolicyDecision.Abstain
    };
}
