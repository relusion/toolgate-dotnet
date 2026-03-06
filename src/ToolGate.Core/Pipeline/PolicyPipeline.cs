using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Configuration;
using ToolGate.Core.Diagnostics;
using ToolGate.Core.Models;

namespace ToolGate.Core.Pipeline;

/// <summary>
/// Sequential evaluator that iterates ordered policies and applies First-Deny-Wins aggregation.
/// </summary>
internal sealed partial class PolicyPipeline : IToolGate
{
    private readonly IReadOnlyList<IToolPolicy> _policies;
    private readonly IApprovalHandler? _approvalHandler;
    private readonly IRedactionProvider _redactionProvider;
    private readonly IAuditSink _auditSink;
    private readonly ILogger<PolicyPipeline> _logger;
    private readonly ToolGateOptions _options;

    public PolicyPipeline(
        IEnumerable<IToolPolicy> policies,
        IRedactionProvider redactionProvider,
        IAuditSink auditSink,
        ILogger<PolicyPipeline> logger,
        IOptions<ToolGateOptions> options,
        IApprovalHandler? approvalHandler = null)
    {
        _policies = policies.OrderBy(p => p.Order).ToList();
        _approvalHandler = approvalHandler;
        _redactionProvider = redactionProvider;
        _auditSink = auditSink;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ToolDecision> EvaluateAsync(
        ToolCallContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // EAR-10: Check for existing approved request (resume-after-approval flow)
        if (_approvalHandler is not null)
        {
            var isApproved = await _approvalHandler.IsApprovedAsync(
                context, cancellationToken);
            if (isApproved)
            {
                LogApprovalBypassed(_logger, context.ToolName, context.InvocationId);
                var bypassDecision = new ToolDecision
                {
                    Decision = Decision.Allow,
                    ReasonCode = "ApprovalGranted",
                    ReasonText = $"Tool '{context.ToolName}' allowed by prior approval",
                    EvaluatedAt = DateTimeOffset.UtcNow
                };
                var bypassRedactedArgs = _redactionProvider.Redact(context.Arguments);
                await EmitAuditAsync(context, bypassDecision, AuditEventType.ApprovalBypassed, bypassRedactedArgs, cancellationToken);
                return bypassDecision;
            }
        }

        using var activity = ToolGateDiagnostics.ActivitySource.StartActivity("ToolGate.Evaluate");
        activity?.SetTag("toolgate.tool_name", context.ToolName);

        // ADR-005: Redact once, reuse across all channels (trace tags, audit, logs)
        var redactedArgs = _redactionProvider.Redact(context.Arguments);

        var sw = Stopwatch.StartNew();
        var matches = new List<PolicyMatch>();
        PolicyDecision? aggregated = null;
        string? aggregatedReasonCode = null;
        string? aggregatedReasonText = null;

        LogEvaluationStarted(_logger, context.ToolName, context.InvocationId, _policies.Count);

        foreach (var policy in _policies)
        {
            PolicyOutcome outcome;
            try
            {
                outcome = await policy.EvaluateAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogPolicyException(_logger, policy.Name, context.ToolName, ex);

                if (_options.FailMode == FailMode.FailClosed)
                {
                    sw.Stop();
                    var failDecision = new ToolDecision
                    {
                        Decision = Decision.Deny,
                        ReasonCode = "PolicyException",
                        ReasonText = $"Policy '{policy.Name}' threw an exception: {ex.Message}",
                        PolicyMatches = matches,
                        EvaluatedAt = DateTimeOffset.UtcNow,
                        EvaluationDuration = sw.Elapsed
                    };

                    activity?.SetTag("toolgate.decision", "Deny");
                    activity?.SetTag("toolgate.error", "PolicyException");
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    ToolGateDiagnostics.ErrorCounter.Add(1, new KeyValuePair<string, object?>("error_type", "PolicyException"));
                    RecordEvaluationMetrics(failDecision);

                    await EmitAuditAsync(context, failDecision, AuditEventType.PolicyException, redactedArgs, cancellationToken);
                    return failDecision;
                }

                // FailOpen: skip this policy, but record the error for operational visibility
                ToolGateDiagnostics.ErrorCounter.Add(1, new KeyValuePair<string, object?>("error_type", "PolicyException_FailOpen"));
                continue;
            }

            if (outcome.Decision == PolicyDecision.Abstain)
                continue;

            matches.Add(new PolicyMatch
            {
                PolicyName = policy.Name,
                Decision = outcome.Decision,
                ReasonCode = outcome.ReasonCode,
                ReasonText = outcome.ReasonText
            });

            // Short-circuit on Deny (EAR-6)
            if (outcome.Decision == PolicyDecision.Deny)
            {
                sw.Stop();
                var denyDecision = new ToolDecision
                {
                    Decision = Decision.Deny,
                    ReasonCode = outcome.ReasonCode,
                    ReasonText = outcome.ReasonText,
                    PolicyMatches = matches,
                    EvaluatedAt = DateTimeOffset.UtcNow,
                    EvaluationDuration = sw.Elapsed
                };

                LogEvaluationDenied(_logger, context.ToolName, policy.Name, outcome.ReasonCode);
                activity?.SetTag("toolgate.decision", "Deny");
                activity?.SetTag("toolgate.policy_count", matches.Count);
                activity?.SetTag("toolgate.duration_ms", sw.Elapsed.TotalMilliseconds);

                RecordEvaluationMetrics(denyDecision);
                await EmitAuditAsync(context, denyDecision, AuditEventType.Evaluation, redactedArgs, cancellationToken);
                return denyDecision;
            }

            if (outcome.Decision == PolicyDecision.RequireApproval)
            {
                aggregated = PolicyDecision.RequireApproval;
                aggregatedReasonCode = outcome.ReasonCode;
                aggregatedReasonText = outcome.ReasonText;
            }
            else if (aggregated is null)
            {
                aggregated = PolicyDecision.Allow;
            }
        }

        sw.Stop();

        // Aggregate final decision (EAR-8)
        ToolDecision decision;
        if (aggregated == PolicyDecision.RequireApproval)
        {
            // EAR-9: Create approval request if handler is available
            ApprovalInfo? approvalInfo = null;
            if (_approvalHandler is not null)
            {
                approvalInfo = await _approvalHandler.RequestApprovalAsync(
                    context, aggregatedReasonText, cancellationToken);
            }

            decision = new ToolDecision
            {
                Decision = Decision.RequireApproval,
                ReasonCode = aggregatedReasonCode,
                ReasonText = aggregatedReasonText,
                PolicyMatches = matches,
                ApprovalInfo = approvalInfo,
                EvaluatedAt = DateTimeOffset.UtcNow,
                EvaluationDuration = sw.Elapsed
            };

            LogEvaluationRequiresApproval(_logger, context.ToolName);
        }
        else if (aggregated == PolicyDecision.Allow)
        {
            decision = new ToolDecision
            {
                Decision = Decision.Allow,
                PolicyMatches = matches,
                EvaluatedAt = DateTimeOffset.UtcNow,
                EvaluationDuration = sw.Elapsed
            };

            LogEvaluationAllowed(_logger, context.ToolName);
        }
        else
        {
            // No policy matched — apply default (EAR-8)
            decision = new ToolDecision
            {
                Decision = _options.DefaultDecision,
                ReasonCode = "DefaultDecision",
                ReasonText = $"No policy matched; applied default decision: {_options.DefaultDecision}",
                PolicyMatches = matches,
                EvaluatedAt = DateTimeOffset.UtcNow,
                EvaluationDuration = sw.Elapsed
            };

            LogEvaluationDefaultApplied(_logger, context.ToolName, _options.DefaultDecision);
        }

        activity?.SetTag("toolgate.decision", decision.Decision.ToString());
        activity?.SetTag("toolgate.policy_count", matches.Count);
        activity?.SetTag("toolgate.duration_ms", sw.Elapsed.TotalMilliseconds);
        RecordEvaluationMetrics(decision);

        await EmitAuditAsync(context, decision, AuditEventType.Evaluation, redactedArgs, cancellationToken);
        return decision;
    }

    private static void RecordEvaluationMetrics(ToolDecision decision)
    {
        var decisionTag = new KeyValuePair<string, object?>("decision", decision.Decision.ToString());
        ToolGateDiagnostics.EvaluationCounter.Add(1, decisionTag);
        ToolGateDiagnostics.EvaluationDuration.Record(decision.EvaluationDuration.TotalMilliseconds, decisionTag);
    }

    private async Task EmitAuditAsync(
        ToolCallContext context,
        ToolDecision decision,
        AuditEventType eventType,
        IDictionary<string, object?>? redactedArgs,
        CancellationToken cancellationToken)
    {
        var auditEvent = new AuditEvent
        {
            EventType = eventType,
            ToolName = context.ToolName,
            InvocationId = context.InvocationId,
            Decision = decision.Decision,
            RedactedArguments = redactedArgs,
            PolicyMatches = decision.PolicyMatches,
            CorrelationId = context.CorrelationId,
            Duration = decision.EvaluationDuration
        };

        try
        {
            await _auditSink.RecordAsync(auditEvent, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditSinkError(_logger, ex);
        }
    }

    // Source-generated log messages

    [LoggerMessage(
        EventId = ToolGateEventIds.EvaluationStarted,
        Level = LogLevel.Debug,
        Message = "Evaluating tool '{ToolName}' invocation '{InvocationId}' against {PolicyCount} policies")]
    private static partial void LogEvaluationStarted(ILogger logger, string toolName, string invocationId, int policyCount);

    [LoggerMessage(
        EventId = ToolGateEventIds.EvaluationDenied,
        Level = LogLevel.Information,
        Message = "Tool '{ToolName}' denied by policy '{PolicyName}' (reason: {ReasonCode})")]
    private static partial void LogEvaluationDenied(ILogger logger, string toolName, string policyName, string? reasonCode);

    [LoggerMessage(
        EventId = ToolGateEventIds.EvaluationAllowed,
        Level = LogLevel.Debug,
        Message = "Tool '{ToolName}' allowed by policy pipeline")]
    private static partial void LogEvaluationAllowed(ILogger logger, string toolName);

    [LoggerMessage(
        EventId = ToolGateEventIds.EvaluationRequiresApproval,
        Level = LogLevel.Information,
        Message = "Tool '{ToolName}' requires approval")]
    private static partial void LogEvaluationRequiresApproval(ILogger logger, string toolName);

    [LoggerMessage(
        EventId = ToolGateEventIds.EvaluationDefaultApplied,
        Level = LogLevel.Information,
        Message = "Tool '{ToolName}' — no policy matched, applying default decision: {DefaultDecision}")]
    private static partial void LogEvaluationDefaultApplied(ILogger logger, string toolName, Decision defaultDecision);

    [LoggerMessage(
        EventId = ToolGateEventIds.PolicyException,
        Level = LogLevel.Error,
        Message = "Policy '{PolicyName}' threw an exception while evaluating tool '{ToolName}'")]
    private static partial void LogPolicyException(ILogger logger, string policyName, string toolName, Exception ex);

    [LoggerMessage(
        EventId = ToolGateEventIds.EvaluationApprovalBypassed,
        Level = LogLevel.Information,
        Message = "Tool '{ToolName}' allowed by prior approval (invocation: {InvocationId})")]
    private static partial void LogApprovalBypassed(ILogger logger, string toolName, string invocationId);

    [LoggerMessage(
        EventId = ToolGateEventIds.AuditSinkError,
        Level = LogLevel.Warning,
        Message = "Audit sink threw an exception")]
    private static partial void LogAuditSinkError(ILogger logger, Exception ex);
}
