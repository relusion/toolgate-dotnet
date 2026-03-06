using ToolGate.Core.Models;

namespace ToolGate.Core.Abstractions;

/// <summary>
/// The primary entry point for evaluating tool calls against the policy pipeline.
/// Returns a <see cref="ToolDecision"/> indicating whether the tool call is allowed,
/// denied, or requires approval.
/// </summary>
/// <example>
/// <code>
/// var context = new ToolCallContext
/// {
///     ToolName = "get_weather",
///     InvocationId = Guid.NewGuid().ToString(),
///     Timestamp = DateTimeOffset.UtcNow
/// };
/// var decision = await toolGate.EvaluateAsync(context);
/// if (decision.Decision == Decision.Allow) { /* proceed */ }
/// </code>
/// </example>
public interface IToolGate
{
    /// <summary>
    /// Evaluates a tool call against all registered policies and returns an aggregated decision.
    /// </summary>
    /// <param name="context">The tool call context including tool name, arguments, and identity.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ToolDecision"/> containing the decision, reasons, and matched policies.</returns>
    Task<ToolDecision> EvaluateAsync(
        ToolCallContext context,
        CancellationToken cancellationToken = default);
}
