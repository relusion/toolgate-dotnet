using ToolGate.Core.Models;

namespace ToolGate.Policies.Configuration;

/// <summary>
/// Configuration for the high-risk approval policy.
/// </summary>
public class HighRiskOptions
{
    /// <summary>
    /// The minimum risk level that triggers RequireApproval.
    /// Defaults to <see cref="RiskLevel.High"/>.
    /// </summary>
    public RiskLevel MinimumRiskLevel { get; set; } = RiskLevel.High;
}
