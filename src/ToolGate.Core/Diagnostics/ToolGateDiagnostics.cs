using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ToolGate.Core.Diagnostics;

/// <summary>
/// Central diagnostics infrastructure for ToolGate tracing and metrics.
/// </summary>
public static class ToolGateDiagnostics
{
    /// <summary>
    /// The ActivitySource used for all ToolGate tracing.
    /// Subscribe to this source to capture ToolGate traces.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("ToolGate", "0.1.0");

    /// <summary>
    /// The Meter used for all ToolGate metrics.
    /// </summary>
    public static readonly Meter Meter = new("ToolGate", "0.1.0");

    /// <summary>Counter for evaluations, tagged by decision.</summary>
    public static readonly Counter<long> EvaluationCounter =
        Meter.CreateCounter<long>("toolgate.evaluations.count", description: "Number of tool call evaluations");

    /// <summary>Histogram for evaluation duration in milliseconds.</summary>
    public static readonly Histogram<double> EvaluationDuration =
        Meter.CreateHistogram<double>("toolgate.evaluations.duration", "ms", "Evaluation duration in milliseconds");

    /// <summary>Counter for errors during evaluation.</summary>
    public static readonly Counter<long> ErrorCounter =
        Meter.CreateCounter<long>("toolgate.errors.count", description: "Number of evaluation errors");

    /// <summary>Counter for approval state transitions.</summary>
    public static readonly Counter<long> ApprovalCounter =
        Meter.CreateCounter<long>("toolgate.approvals.count", description: "Number of approval state transitions");
}
