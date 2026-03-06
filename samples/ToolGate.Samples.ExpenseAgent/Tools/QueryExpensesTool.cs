using System.ComponentModel;

namespace ToolGate.Samples.ExpenseAgent.Tools;

public static class QueryExpensesTool
{
    [Description("Queries submitted expense reports with optional filters")]
    public static string QueryExpenses(
        [Description("Filter by status: pending, approved, rejected, or all")] string? status = null,
        [Description("Start date filter (ISO 8601, e.g. 2024-01-01)")] string? date_from = null,
        [Description("End date filter (ISO 8601, e.g. 2024-12-31)")] string? date_to = null)
    {
        var expenses = ExpenseStore.Query(status);
        if (expenses.Count == 0)
            return "No expense reports found matching the specified filters.";

        var lines = expenses.Select((e, i) =>
            $"{i + 1}. {e.Id} | ${e.Amount:N2} | {e.Category,-10} | {e.Description,-20} | Status: {e.Status}");
        return "Expense Reports:\n" + string.Join("\n", lines);
    }
}
