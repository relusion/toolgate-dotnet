namespace ToolGate.Core.Models;

/// <summary>
/// Optional metadata associated with a tool, such as risk level, tags, and categorization.
/// </summary>
public class ToolMetadata
{
    /// <summary>
    /// The risk level assigned to this tool. Defaults to <see cref="RiskLevel.Normal"/>.
    /// </summary>
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Normal;

    /// <summary>
    /// Freeform tags for categorization and policy matching (e.g., "high-risk", "pii").
    /// </summary>
    public HashSet<string> Tags { get; init; } = [];

    /// <summary>
    /// Optional category grouping (e.g., "database", "email", "filesystem").
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional owner identifier for the tool.
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// Extensible bag of custom properties for domain-specific metadata.
    /// </summary>
    public IDictionary<string, object?>? CustomProperties { get; init; }
}
