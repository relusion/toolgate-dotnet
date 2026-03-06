namespace ToolGate.Core.Models;

/// <summary>
/// Indicates the risk level of a tool operation.
/// </summary>
public enum RiskLevel
{
    /// <summary>Low risk — routine, read-only operations.</summary>
    Low,

    /// <summary>Normal risk — standard operations (default).</summary>
    Normal,

    /// <summary>High risk — operations that modify state or access sensitive data.</summary>
    High,

    /// <summary>Critical risk — destructive or irreversible operations.</summary>
    Critical
}
