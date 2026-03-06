using ToolGate.Core.Models;

namespace ToolGate.Core.Abstractions;

/// <summary>
/// Receives audit events for tool call evaluations and approval lifecycle changes.
/// Implement to send audit records to external systems (databases, message queues, etc.).
/// </summary>
public interface IAuditSink
{
    /// <summary>
    /// Records an audit event. Called after every tool call evaluation and approval state change.
    /// </summary>
    /// <param name="auditEvent">The audit event to record.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// An audit event capturing a tool call evaluation or approval lifecycle change.
/// All argument values are pre-redacted before reaching this type.
/// </summary>
public class AuditEvent
{
    /// <summary>The type of audit event.</summary>
    public required AuditEventType EventType { get; init; }

    /// <summary>The tool name associated with this event.</summary>
    public required string ToolName { get; init; }

    /// <summary>The invocation ID for the tool call.</summary>
    public required string InvocationId { get; init; }

    /// <summary>The final decision (for evaluation events).</summary>
    public Decision? Decision { get; init; }

    /// <summary>Redacted arguments from the tool call.</summary>
    public IDictionary<string, object?>? RedactedArguments { get; init; }

    /// <summary>Policy matches for the evaluation.</summary>
    public IReadOnlyList<PolicyMatch>? PolicyMatches { get; init; }

    /// <summary>Correlation ID for distributed tracing.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>When this event occurred.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>How long the evaluation took (for evaluation events).</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Approval ID (for approval-related events).</summary>
    public string? ApprovalId { get; init; }

    /// <summary>The actor who performed an approval action.</summary>
    public string? Actor { get; init; }
}

/// <summary>
/// The type of audit event.
/// </summary>
public enum AuditEventType
{
    /// <summary>A tool call was evaluated through the policy pipeline.</summary>
    Evaluation,

    /// <summary>A tool call was allowed by an existing approval (pipeline bypassed).</summary>
    ApprovalBypassed,

    /// <summary>An approval request was created.</summary>
    ApprovalCreated,

    /// <summary>An approval request was approved.</summary>
    ApprovalApproved,

    /// <summary>An approval request was denied.</summary>
    ApprovalDenied,

    /// <summary>An approval request expired.</summary>
    ApprovalExpired,

    /// <summary>A policy threw an exception during evaluation.</summary>
    PolicyException
}
