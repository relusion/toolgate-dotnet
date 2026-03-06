using ToolGate.Core.Models;

namespace ToolGate.Core.Abstractions;

/// <summary>
/// Narrow interface for approval operations needed by the policy pipeline.
/// Defined in Core to avoid a circular dependency between Core and Approvals.
/// The Approvals package provides the default implementation.
/// </summary>
public interface IApprovalHandler
{
    /// <summary>
    /// Checks whether an existing approved request allows this tool call.
    /// First checks by invocation ID (same-turn fast path), then falls back to
    /// content-based matching via content hash (cross-turn resume).
    /// </summary>
    /// <returns>True if the tool call has an approved request; false otherwise.</returns>
    Task<bool> IsApprovedAsync(ToolCallContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new approval request for a tool call that requires human approval (EAR-9).
    /// </summary>
    /// <returns>The approval info to embed in the <see cref="ToolDecision"/>.</returns>
    Task<ApprovalInfo> RequestApprovalAsync(
        ToolCallContext context,
        string? reason,
        CancellationToken cancellationToken = default);
}
