using System.Collections.Concurrent;

namespace ToolGate.Samples.ExpenseAgent.Tools;

public record Expense(string Id, string Category, decimal Amount, string Currency, string Description, string ReceiptUrl, string Status, DateTime SubmittedAt);

public static class ExpenseStore
{
    private static readonly ConcurrentDictionary<string, Expense> _expenses = new(
        new Dictionary<string, Expense>
        {
            ["EXP-4201"] = new("EXP-4201", "Meals", 45.00m, "USD", "Team lunch", "https://receipts.example.com/4201.jpg", "Approved", DateTime.UtcNow.AddDays(-5)),
            ["EXP-4315"] = new("EXP-4315", "Travel", 520.00m, "USD", "Flight to NYC", "https://receipts.example.com/4315.jpg", "Pending", DateTime.UtcNow.AddDays(-2)),
            ["EXP-4402"] = new("EXP-4402", "Office", 89.99m, "USD", "Keyboard", "https://receipts.example.com/4402.jpg", "Approved", DateTime.UtcNow.AddDays(-3)),
            ["EXP-4510"] = new("EXP-4510", "Equipment", 2100.00m, "USD", "MacBook charger", "https://receipts.example.com/4510.jpg", "Pending", DateTime.UtcNow.AddDays(-1)),
        });

    public static Expense Add(string category, decimal amount, string currency, string description, string receiptUrl, string status = "Pending")
    {
        var id = $"EXP-{Random.Shared.Next(1000, 9999)}";
        var expense = new Expense(id, category, amount, currency, description, receiptUrl, status, DateTime.UtcNow);
        _expenses[id] = expense;
        return expense;
    }

    public static bool TryApprove(string id, out Expense? expense)
    {
        if (_expenses.TryGetValue(id, out var existing) && existing.Status == "Pending")
        {
            expense = existing with { Status = "Approved" };
            _expenses[id] = expense;
            return true;
        }
        expense = _expenses.GetValueOrDefault(id);
        return false;
    }

    public static IReadOnlyList<Expense> Query(string? status = null)
    {
        var query = _expenses.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
            query = query.Where(e => e.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        return query.OrderByDescending(e => e.SubmittedAt).ToList();
    }
}
