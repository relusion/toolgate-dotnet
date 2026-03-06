namespace ToolGate.Policies.Configuration;

/// <summary>
/// Configuration for the denylist policy.
/// </summary>
public class DenylistOptions
{
    /// <summary>
    /// Tool names to deny (exact match, case-insensitive).
    /// </summary>
    public HashSet<string> DeniedTools { get; set; } = [];

    /// <summary>
    /// Glob-style patterns for tool names to deny (e.g., "delete_*", "*.destructive").
    /// </summary>
    public List<string> DeniedPatterns { get; set; } = [];
}
