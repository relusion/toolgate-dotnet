namespace ToolGate.Policies.Configuration;

/// <summary>
/// Configuration for the allowlist policy.
/// </summary>
public class AllowlistOptions
{
    /// <summary>
    /// Tool names that are explicitly allowed (case-insensitive).
    /// </summary>
    public HashSet<string> AllowedTools { get; set; } = [];

    /// <summary>
    /// When true, tools not in the allowlist are denied.
    /// When false, unlisted tools receive Abstain (other policies decide).
    /// </summary>
    public bool DenyUnlisted { get; set; }
}
