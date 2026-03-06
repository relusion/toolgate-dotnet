using System.ComponentModel;

namespace ToolGate.Samples.ExpenseAgent.Tools;

public static class ApproveExpenseTool
{
    [Description("Approves a pending expense report")]
    public static string ApproveExpense(
        [Description("The expense ID to approve (e.g. EXP-1234)")] string expense_id,
        [Description("Optional notes from the approver")] string approver_notes)
    {
        if (ExpenseStore.TryApprove(expense_id, out _))
            return $"Expense {expense_id} approved with notes: {approver_notes}";
        return $"Expense {expense_id} not found or not in Pending status.";
    }
}
