using ToolGate.Approvals.Models;
using ToolGate.Core.Models;

namespace ToolGate.Approvals.Abstractions;

/// <summary>
/// Business logic layer for approval operations. Mediates between the policy pipeline
/// and the underlying <see cref="IApprovalStore"/>.
/// </summary>
public interface IApprovalProvider
{
    /// <summary>
    /// Creates a new approval request for a tool call that requires human approval.
    /// Handles redaction, GUID generation, and TTL computation.
    /// </summary>
    Task<ApprovalRequest> CreateRequestAsync(
        ToolCallContext context,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an existing approval request by the tool call invocation ID.
    /// Used for resume-after-approval flow.
    /// </summary>
    Task<ApprovalRequest?> FindByInvocationIdAsync(
        string invocationId,
        CancellationToken cancellationToken = default);

    /// <summary>Approves a pending approval request.</summary>
    Task<ApprovalActionResult> ApproveAsync(
        string approvalId,
        string? actor = null,
        string? comment = null,
        CancellationToken cancellationToken = default);

    /// <summary>Denies a pending approval request.</summary>
    Task<ApprovalActionResult> DenyAsync(
        string approvalId,
        string? actor = null,
        string? comment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries approval requests matching the specified filter criteria.
    /// Results are ordered by <see cref="ApprovalRequest.CreatedAt"/> descending.
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> QueryAsync(
        ApprovalQuery query,
        CancellationToken cancellationToken = default);
}
