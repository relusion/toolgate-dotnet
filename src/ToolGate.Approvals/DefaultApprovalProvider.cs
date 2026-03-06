using Microsoft.Extensions.Options;
using ToolGate.Approvals.Abstractions;
using ToolGate.Approvals.Models;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Configuration;
using ToolGate.Core.Models;

namespace ToolGate.Approvals;

/// <summary>
/// Default business logic layer for approval operations.
/// Mediates between the policy pipeline and the underlying <see cref="IApprovalStore"/>.
/// Handles GUID generation, TTL computation, and argument redaction.
/// </summary>
internal sealed class DefaultApprovalProvider : IApprovalProvider
{
    private readonly IApprovalStore _store;
    private readonly IRedactionProvider _redactionProvider;
    private readonly ToolGateOptions _options;

    public DefaultApprovalProvider(
        IApprovalStore store,
        IRedactionProvider redactionProvider,
        IOptions<ToolGateOptions> options)
    {
        _store = store;
        _redactionProvider = redactionProvider;
        _options = options.Value;
    }

    public async Task<ApprovalRequest> CreateRequestAsync(
        ToolCallContext context,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new ApprovalRequest
        {
            ApprovalId = Guid.NewGuid().ToString("N"),
            InvocationId = context.InvocationId,
            ToolName = context.ToolName,
            RedactedArguments = _redactionProvider.Redact(context.Arguments),
            State = ApprovalState.Pending,
            CreatedAt = now,
            ExpiresAt = now + _options.DefaultApprovalTtl,
            CorrelationId = context.CorrelationId,
            RequestedBy = context.UserId,
            Reason = reason,
            ContentHash = ContentHasher.Compute(context.ToolName, context.Arguments)
        };

        return await _store.CreateAsync(request, cancellationToken);
    }

    public Task<ApprovalRequest?> FindByInvocationIdAsync(
        string invocationId,
        CancellationToken cancellationToken = default)
    {
        return _store.FindByInvocationIdAsync(invocationId, cancellationToken);
    }

    public Task<ApprovalActionResult> ApproveAsync(
        string approvalId,
        string? actor = null,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var action = new ApprovalAction
        {
            TargetState = ApprovalState.Approved,
            Actor = actor,
            Comment = comment
        };

        return _store.TransitionAsync(approvalId, ApprovalState.Approved, action, cancellationToken);
    }

    public Task<ApprovalActionResult> DenyAsync(
        string approvalId,
        string? actor = null,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var action = new ApprovalAction
        {
            TargetState = ApprovalState.Denied,
            Actor = actor,
            Comment = comment
        };

        return _store.TransitionAsync(approvalId, ApprovalState.Denied, action, cancellationToken);
    }

    public Task<IReadOnlyList<ApprovalRequest>> QueryAsync(
        ApprovalQuery query,
        CancellationToken cancellationToken = default)
    {
        return _store.QueryAsync(query, cancellationToken);
    }
}
