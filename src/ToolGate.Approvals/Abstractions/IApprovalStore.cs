using ToolGate.Approvals.Models;

namespace ToolGate.Approvals.Abstractions;

/// <summary>
/// Persists and manages approval request state transitions.
/// Implementations must guarantee atomic transitions and first-writer-wins concurrency.
/// </summary>
/// <remarks>
/// The in-memory implementation uses per-approval locks for atomicity.
/// Durable implementations should use database-level optimistic concurrency
/// (e.g., row versioning or conditional updates).
/// </remarks>
public interface IApprovalStore
{
    /// <summary>Creates a new approval request in the Pending state.</summary>
    Task<ApprovalRequest> CreateAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves an approval request by its unique ID.</summary>
    Task<ApprovalRequest?> GetAsync(
        string approvalId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds an approval request by the tool call invocation ID.</summary>
    Task<ApprovalRequest?> FindByInvocationIdAsync(
        string invocationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically transitions an approval request to the target state.
    /// Only succeeds if the current state is Pending and TTL has not expired.
    /// </summary>
    Task<ApprovalActionResult> TransitionAsync(
        string approvalId,
        ApprovalState targetState,
        ApprovalAction action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries approval requests matching the specified filter criteria.
    /// Results are ordered by <see cref="ApprovalRequest.CreatedAt"/> descending.
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> QueryAsync(
        ApprovalQuery query,
        CancellationToken cancellationToken = default);
}
