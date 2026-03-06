using Microsoft.Extensions.Logging;
using ToolGate.Approvals.Abstractions;
using ToolGate.Approvals.Models;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Diagnostics;
using ToolGate.Core.Models;

namespace ToolGate.Approvals;

/// <summary>
/// Bridges <see cref="IApprovalHandler"/> (consumed by the Core pipeline) with
/// <see cref="IApprovalProvider"/> (business logic in the Approvals package).
/// Supports dual-path lookup: invocation ID (same-turn) and content hash (cross-turn).
/// </summary>
internal sealed partial class ApprovalHandler : IApprovalHandler
{
    private readonly IApprovalProvider _provider;
    private readonly ILogger<ApprovalHandler> _logger;

    public ApprovalHandler(IApprovalProvider provider, ILogger<ApprovalHandler> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<bool> IsApprovedAsync(
        ToolCallContext context,
        CancellationToken cancellationToken = default)
    {
        // Path 1: Same-turn lookup by InvocationId (fast path)
        var byInvocation = await _provider.FindByInvocationIdAsync(context.InvocationId, cancellationToken);
        if (byInvocation is { State: ApprovalState.Approved })
            return true;

        // Path 2: Cross-turn lookup by content hash
        var contentHash = ContentHasher.Compute(context.ToolName, context.Arguments);
        var approved = await _provider.QueryAsync(
            new ApprovalQuery
            {
                ContentHash = contentHash,
                State = ApprovalState.Approved,
                ExpiresAfter = DateTimeOffset.UtcNow,
                Limit = 1
            },
            cancellationToken);

        if (approved.Count > 0)
        {
            LogContentHashMatched(_logger, context.ToolName, contentHash);
            return true;
        }

        return false;
    }

    public async Task<ApprovalInfo> RequestApprovalAsync(
        ToolCallContext context,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var request = await _provider.CreateRequestAsync(context, reason, cancellationToken);
        return new ApprovalInfo
        {
            ApprovalId = request.ApprovalId,
            ExpiresAt = request.ExpiresAt
        };
    }

    [LoggerMessage(
        EventId = ToolGateEventIds.ApprovalContentHashMatched,
        Level = LogLevel.Information,
        Message = "Tool '{ToolName}' matched approved request via content hash '{ContentHash}' (cross-turn resume)")]
    private static partial void LogContentHashMatched(ILogger logger, string toolName, string contentHash);
}
