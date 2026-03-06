using System.Collections.Concurrent;
using ToolGate.Approvals.Abstractions;
using ToolGate.Approvals.Models;

namespace ToolGate.Approvals;

/// <summary>
/// In-memory implementation of <see cref="IApprovalStore"/> using concurrent dictionaries.
/// Supports atomic state transitions with first-writer-wins concurrency and lazy TTL expiration.
/// </summary>
/// <remarks>
/// This store is intended for development and testing. In long-running production scenarios,
/// consider implementing a durable <see cref="IApprovalStore"/> with database-backed storage.
/// </remarks>
internal sealed class InMemoryApprovalStore : IApprovalStore
{
    private readonly ConcurrentDictionary<string, ApprovalRequest> _requests = new();
    private readonly ConcurrentDictionary<string, string> _invocationIndex = new(); // invocationId -> approvalId
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public Task<ApprovalRequest> CreateAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_requests.TryAdd(request.ApprovalId, request))
        {
            throw new InvalidOperationException(
                $"Approval request '{request.ApprovalId}' already exists.");
        }

        _invocationIndex.TryAdd(request.InvocationId, request.ApprovalId);
        _locks.TryAdd(request.ApprovalId, new object());

        return Task.FromResult(request);
    }

    public Task<ApprovalRequest?> GetAsync(
        string approvalId,
        CancellationToken cancellationToken = default)
    {
        if (_requests.TryGetValue(approvalId, out var request))
        {
            CheckAndExpireUnderLock(approvalId, request);
            return Task.FromResult<ApprovalRequest?>(request);
        }

        return Task.FromResult<ApprovalRequest?>(null);
    }

    public Task<ApprovalRequest?> FindByInvocationIdAsync(
        string invocationId,
        CancellationToken cancellationToken = default)
    {
        if (_invocationIndex.TryGetValue(invocationId, out var approvalId) &&
            _requests.TryGetValue(approvalId, out var request))
        {
            CheckAndExpireUnderLock(approvalId, request);
            return Task.FromResult<ApprovalRequest?>(request);
        }

        return Task.FromResult<ApprovalRequest?>(null);
    }

    public Task<ApprovalActionResult> TransitionAsync(
        string approvalId,
        ApprovalState targetState,
        ApprovalAction action,
        CancellationToken cancellationToken = default)
    {
        // Validate: only terminal states are valid targets (ADR-004)
        if (targetState == ApprovalState.Pending)
        {
            return Task.FromResult(new ApprovalActionResult
            {
                Success = false,
                CurrentState = ApprovalState.Pending,
                FailureReason = "Cannot transition to Pending state"
            });
        }

        if (!_requests.TryGetValue(approvalId, out var request))
        {
            return Task.FromResult(new ApprovalActionResult
            {
                Success = false,
                CurrentState = ApprovalState.Pending,
                FailureReason = $"Approval request '{approvalId}' not found"
            });
        }

        var lockObj = _locks.GetOrAdd(approvalId, _ => new object());

        lock (lockObj)
        {
            // Lazy TTL expiration check
            if (request.State == ApprovalState.Pending && DateTimeOffset.UtcNow >= request.ExpiresAt)
            {
                request.State = ApprovalState.Expired;
                return Task.FromResult(new ApprovalActionResult
                {
                    Success = false,
                    CurrentState = ApprovalState.Expired,
                    FailureReason = "Approval request has expired"
                });
            }

            // First-writer-wins: only Pending → terminal transitions succeed
            if (request.State != ApprovalState.Pending)
            {
                return Task.FromResult(new ApprovalActionResult
                {
                    Success = false,
                    CurrentState = request.State,
                    FailureReason = $"Approval request is already in state '{request.State}'"
                });
            }

            request.State = targetState;

            return Task.FromResult(new ApprovalActionResult
            {
                Success = true,
                CurrentState = targetState
            });
        }
    }

    public Task<IReadOnlyList<ApprovalRequest>> QueryAsync(
        ApprovalQuery query,
        CancellationToken cancellationToken = default)
    {
        // Expire pending-but-past-TTL requests during scan
        foreach (var kvp in _requests)
            CheckAndExpireUnderLock(kvp.Key, kvp.Value);

        var results = _requests.Values.AsEnumerable();

        if (query.ToolName is not null)
            results = results.Where(r => r.ToolName == query.ToolName);

        if (query.State is not null)
            results = results.Where(r => r.State == query.State);

        if (query.RequestedBy is not null)
            results = results.Where(r => r.RequestedBy == query.RequestedBy);

        if (query.ContentHash is not null)
            results = results.Where(r => r.ContentHash == query.ContentHash);

        if (query.CreatedAfter is not null)
            results = results.Where(r => r.CreatedAt >= query.CreatedAfter);

        if (query.CreatedBefore is not null)
            results = results.Where(r => r.CreatedAt <= query.CreatedBefore);

        if (query.ExpiresAfter is not null)
            results = results.Where(r => r.ExpiresAt >= query.ExpiresAfter);

        var list = results
            .OrderByDescending(r => r.CreatedAt)
            .Take(query.Limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<ApprovalRequest>>(list);
    }

    /// <summary>
    /// Checks if a pending request has exceeded its TTL and transitions to Expired if so.
    /// Acquires the per-approval lock to avoid racing with TransitionAsync.
    /// </summary>
    private void CheckAndExpireUnderLock(string approvalId, ApprovalRequest request)
    {
        if (request.State != ApprovalState.Pending)
            return;

        if (DateTimeOffset.UtcNow < request.ExpiresAt)
            return;

        var lockObj = _locks.GetOrAdd(approvalId, _ => new object());
        lock (lockObj)
        {
            if (request.State == ApprovalState.Pending && DateTimeOffset.UtcNow >= request.ExpiresAt)
            {
                request.State = ApprovalState.Expired;
            }
        }
    }
}
