using ToolGate.Core.Models;

namespace ToolGate.Core.Configuration;

/// <summary>
/// Top-level configuration for the ToolGate policy engine.
/// </summary>
public class ToolGateOptions
{
    /// <summary>
    /// Controls behavior when a policy throws an exception.
    /// Defaults to <see cref="FailMode.FailClosed"/> — errors block tool execution.
    /// </summary>
    public FailMode FailMode { get; set; } = FailMode.FailClosed;

    /// <summary>
    /// The decision applied when no policy returns a non-Abstain outcome.
    /// Defaults to <see cref="Decision.Deny"/> — unknown tool calls are blocked.
    /// </summary>
    public Decision DefaultDecision { get; set; } = Decision.Deny;

    /// <summary>
    /// Configuration for sensitive argument redaction.
    /// </summary>
    public RedactionOptions Redaction { get; set; } = new();

    /// <summary>
    /// Default time-to-live for approval requests.
    /// After this duration, pending approvals are treated as expired.
    /// </summary>
    public TimeSpan DefaultApprovalTtl { get; set; } = TimeSpan.FromMinutes(30);
}
