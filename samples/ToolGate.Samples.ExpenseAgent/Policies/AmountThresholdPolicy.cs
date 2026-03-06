using System.Globalization;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Models;

namespace ToolGate.Samples.ExpenseAgent.Policies;

/// <summary>
/// Graduated decisions for approve_expense based on the expense amount.
/// Prevents the AI from self-approving high-value expenses without human authorization.
/// Uses defensive parsing (ADR-003): ToString() + decimal.TryParse.
/// </summary>
public sealed class AmountThresholdPolicy : IToolPolicy
{
    public string Name => "AmountThreshold";
    public int Order => 100;

    public Task<PolicyOutcome> EvaluateAsync(
        ToolCallContext context, CancellationToken cancellationToken = default)
    {
        if (!context.ToolName.Equals("approve_expense", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(PolicyOutcome.AbstainResult());

        // Look up the expense to determine approval tier based on amount.
        // The expense_id argument tells us which expense is being approved.
        if (context.Arguments is null ||
            !context.Arguments.TryGetValue("expense_id", out var expenseIdObj) ||
            expenseIdObj is null)
        {
            return Task.FromResult(
                PolicyOutcome.DenyResult("MissingExpenseId", "Missing expense ID"));
        }

        var expenseId = expenseIdObj.ToString()!;
        var expenses = Tools.ExpenseStore.Query();
        var expense = expenses.FirstOrDefault(e =>
            e.Id.Equals(expenseId, StringComparison.OrdinalIgnoreCase));

        if (expense is null)
            return Task.FromResult(
                PolicyOutcome.DenyResult("ExpenseNotFound", $"Expense '{expenseId}' not found"));

        return expense.Amount switch
        {
            < 100m => Task.FromResult(PolicyOutcome.AllowResult("AutoApproved")),
            < 1000m => Task.FromResult(
                PolicyOutcome.RequireApprovalResult(
                    $"Approval of ${expense.Amount:N2} expense requires manager authorization")),
            _ => Task.FromResult(
                PolicyOutcome.RequireApprovalResult(
                    $"Approval of ${expense.Amount:N2} expense requires VP authorization"))
        };
    }
}
