namespace ToolGate.Approvals.Models;

/// <summary>
/// Filter criteria for querying approval requests.
/// All non-null properties are combined with AND semantics.
/// Results are ordered by <see cref="ApprovalRequest.CreatedAt"/> descending.
/// </summary>
public class ApprovalQuery
{
    /// <summary>Exact match on tool name.</summary>
    public string? ToolName { get; init; }

    /// <summary>Exact match on approval state.</summary>
    public ApprovalState? State { get; init; }

    /// <summary>Exact match on the user or system that triggered the tool call.</summary>
    public string? RequestedBy { get; init; }

    /// <summary>Exact match on the content hash (SHA-256 of tool name + arguments).</summary>
    public string? ContentHash { get; init; }

    /// <summary>Only return requests created on or after this timestamp.</summary>
    public DateTimeOffset? CreatedAfter { get; init; }

    /// <summary>Only return requests created on or before this timestamp.</summary>
    public DateTimeOffset? CreatedBefore { get; init; }

    /// <summary>Only return requests whose expiration is on or after this timestamp (i.e., not yet expired).</summary>
    public DateTimeOffset? ExpiresAfter { get; init; }

    /// <summary>Maximum number of results to return. Defaults to 50.</summary>
    public int Limit { get; init; } = 50;
}
