namespace ToolGate.Approvals.Models;

/// <summary>
/// Represents an action taken on an approval request (approve, deny, cancel, etc.).
/// </summary>
public class ApprovalAction
{
    /// <summary>The target state for this action.</summary>
    public required ApprovalState TargetState { get; init; }

    /// <summary>The actor who performed this action (e.g., user ID, email).</summary>
    public string? Actor { get; init; }

    /// <summary>Optional comment explaining the action.</summary>
    public string? Comment { get; init; }

    /// <summary>When this action was performed.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
