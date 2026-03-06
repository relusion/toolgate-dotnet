namespace ToolGate.Approvals.Models;

/// <summary>
/// The lifecycle state of an approval request.
/// All states except <see cref="Pending"/> are terminal — no further transitions are valid.
/// </summary>
public enum ApprovalState
{
    /// <summary>Awaiting a human decision. Tool call is blocked.</summary>
    Pending,

    /// <summary>Human has authorized the tool call.</summary>
    Approved,

    /// <summary>Human has rejected the tool call.</summary>
    Denied,

    /// <summary>The approval request TTL has been exceeded without a decision.</summary>
    Expired,

    /// <summary>The approval request was withdrawn by the requesting system.</summary>
    Cancelled
}
