using Microsoft.Extensions.Logging;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Diagnostics;

namespace ToolGate.Core.Audit;

/// <summary>
/// Default audit sink that writes structured log entries via <see cref="ILogger"/>.
/// </summary>
internal sealed partial class LoggerAuditSink : IAuditSink
{
    private readonly ILogger<LoggerAuditSink> _logger;

    public LoggerAuditSink(ILogger<LoggerAuditSink> logger)
    {
        _logger = logger;
    }

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LogAuditEvent(
            _logger,
            auditEvent.EventType,
            auditEvent.ToolName,
            auditEvent.InvocationId,
            auditEvent.Decision,
            auditEvent.CorrelationId,
            auditEvent.ApprovalId,
            auditEvent.Duration);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = ToolGateEventIds.AuditEventRecorded,
        Level = LogLevel.Information,
        Message = "Audit: {EventType} for tool '{ToolName}' invocation '{InvocationId}' decision={Decision} correlation={CorrelationId} approval={ApprovalId} duration={Duration}")]
    private static partial void LogAuditEvent(
        ILogger logger,
        AuditEventType eventType,
        string toolName,
        string invocationId,
        Models.Decision? decision,
        string? correlationId,
        string? approvalId,
        TimeSpan? duration);
}
