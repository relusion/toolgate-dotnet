using ToolGate.Core.Abstractions;
using ToolGate.Core.Models;

namespace ToolGate.Samples.ExpenseAgent.Policies;

/// <summary>
/// Allows safe tools (read-only queries and expense submission) without further evaluation.
/// Submission is safe because it only records intent — approval is the gated action.
/// </summary>
public sealed class ReadOnlyPolicy : IToolPolicy
{
    public string Name => "SafeTools";
    public int Order => -100;

    private static readonly HashSet<string> SafeTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "query_expenses",
        "generate_report",
        "submit_expense"
    };

    public Task<PolicyOutcome> EvaluateAsync(
        ToolCallContext context, CancellationToken cancellationToken = default)
    {
        if (SafeTools.Contains(context.ToolName))
            return Task.FromResult(PolicyOutcome.AllowResult("SafeTool"));

        return Task.FromResult(PolicyOutcome.AbstainResult());
    }
}
