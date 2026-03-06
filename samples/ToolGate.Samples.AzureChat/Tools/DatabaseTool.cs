using System.ComponentModel;

namespace ToolGate.Samples.AzureChat.Tools;

public static class DatabaseTool
{
    [Description("Deletes the specified database permanently")]
    public static string DeleteDatabase(
        [Description("Database name to delete")] string database)
    {
        // This tool is blocked by ToolGate's denylist policy.
        // It should never execute.
        return $"Database '{database}' deleted.";
    }
}
