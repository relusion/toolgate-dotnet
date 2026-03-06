using ToolGate.Core.Models;

namespace ToolGate.Core.Abstractions;

/// <summary>
/// A policy that evaluates a tool call and returns a decision.
/// Implement this interface to create custom policies.
/// </summary>
/// <remarks>
/// Policies are pure functions — they receive a <see cref="ToolCallContext"/> and return
/// a <see cref="PolicyOutcome"/> without side effects. The pipeline evaluator handles
/// aggregation, ordering, and short-circuiting.
/// </remarks>
/// <example>
/// <code>
/// public class RateLimitPolicy : IToolPolicy
/// {
///     public string Name => "RateLimit";
///     public int Order => 100;
///
///     public Task&lt;PolicyOutcome&gt; EvaluateAsync(
///         ToolCallContext context, CancellationToken ct = default)
///     {
///         if (IsOverLimit(context.ToolName))
///             return Task.FromResult(PolicyOutcome.DenyResult("RateLimited", "Too many calls"));
///         return Task.FromResult(PolicyOutcome.AbstainResult());
///     }
/// }
/// </code>
/// </example>
public interface IToolPolicy
{
    /// <summary>
    /// A unique, human-readable name for this policy (used in logs, traces, and audit events).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines evaluation order. Lower values execute first.
    /// Built-in orders: Denylist (-1000), Allowlist (-500), Custom (0), HighRisk (500).
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Evaluates the tool call and returns a decision.
    /// </summary>
    /// <param name="context">The tool call context to evaluate.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// A <see cref="PolicyOutcome"/> — use the static factory methods
    /// (<see cref="PolicyOutcome.AllowResult"/>, <see cref="PolicyOutcome.DenyResult"/>,
    /// <see cref="PolicyOutcome.RequireApprovalResult"/>, <see cref="PolicyOutcome.AbstainResult"/>).
    /// </returns>
    Task<PolicyOutcome> EvaluateAsync(
        ToolCallContext context,
        CancellationToken cancellationToken = default);
}
