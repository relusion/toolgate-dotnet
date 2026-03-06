using System.ComponentModel;

namespace ToolGate.Samples.ExpenseAgent.Tools;

public static class SubmitExpenseTool
{
    [Description("Submits a new expense report for reimbursement")]
    public static string SubmitExpense(
        [Description("Expense category (e.g. travel, meals, equipment, office)")] string category,
        [Description("Expense amount in the specified currency")] decimal amount,
        [Description("Currency code (e.g. USD, EUR)")] string currency,
        [Description("Brief description of the expense")] string description,
        [Description("URL of the uploaded receipt image")] string receipt_url)
    {
        var status = amount < 100m ? "Approved" : "Pending";
        var expense = ExpenseStore.Add(category, amount, currency, description, receipt_url, status);
        return $"Expense {expense.Id} submitted for ${expense.Amount:N2} ({expense.Category}). Status: {expense.Status}";
    }
}
