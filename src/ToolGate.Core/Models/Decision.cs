namespace ToolGate.Core.Models;

/// <summary>
/// The final aggregated decision for a tool call, after all policies have been evaluated.
/// Unlike <see cref="PolicyDecision"/>, this does not include Abstain —
/// a final decision must be conclusive.
/// </summary>
public enum Decision
{
    /// <summary>The tool call is allowed to proceed.</summary>
    Allow,

    /// <summary>The tool call is denied.</summary>
    Deny,

    /// <summary>The tool call requires human approval before proceeding.</summary>
    RequireApproval
}
