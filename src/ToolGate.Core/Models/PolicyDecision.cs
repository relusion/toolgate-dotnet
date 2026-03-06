namespace ToolGate.Core.Models;

/// <summary>
/// The decision returned by an individual policy evaluation.
/// </summary>
public enum PolicyDecision
{
    /// <summary>The policy has no opinion on this tool call.</summary>
    Abstain,

    /// <summary>The policy explicitly allows this tool call.</summary>
    Allow,

    /// <summary>The policy explicitly denies this tool call.</summary>
    Deny,

    /// <summary>The policy requires human approval before the tool call can proceed.</summary>
    RequireApproval
}
