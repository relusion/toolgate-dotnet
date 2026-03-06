using System.ComponentModel;

namespace ToolGate.Samples.ExpenseAgent.Tools;

public static class GenerateReportTool
{
    [Description("Generates an expense summary report for a given period")]
    public static string GenerateReport(
        [Description("Report period: weekly, monthly, or quarterly")] string period)
    {
        var all = ExpenseStore.Query();
        var total = all.Sum(e => e.Amount);
        var pending = all.Count(e => e.Status == "Pending");
        var byCategory = all.GroupBy(e => e.Category)
            .Select(g => $"{g.Key} ${g.Sum(e => e.Amount):N2}");
        var label = (period ?? "quarterly").ToLowerInvariant() switch
        {
            "weekly" => "Weekly",
            "monthly" => "Monthly",
            _ => "Quarterly"
        };
        return $"""
            {label} Expense Summary:
              Total: ${total:N2} across {all.Count} expenses
              By category: {string.Join(", ", byCategory)}
              Pending approvals: {pending}
            """;
    }
}
