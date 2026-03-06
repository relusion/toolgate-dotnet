using Microsoft.Extensions.Options;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Models;
using ToolGate.Policies.Configuration;

namespace ToolGate.Policies;

/// <summary>
/// Requires human approval for tools with high risk levels or "high-risk" tags.
/// Order: 500 (late evaluation — after custom policies).
/// </summary>
public class HighRiskApprovalPolicy : IToolPolicy
{
    private readonly HighRiskOptions _options;

    /// <summary>Initializes a new instance with the configured high-risk options.</summary>
    public HighRiskApprovalPolicy(IOptions<HighRiskOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public string Name => "HighRiskApproval";

    /// <inheritdoc />
    public int Order => 500;

    /// <inheritdoc />
    public Task<PolicyOutcome> EvaluateAsync(
        ToolCallContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Metadata is not null &&
            (context.Metadata.RiskLevel >= _options.MinimumRiskLevel ||
             context.Metadata.Tags.Contains("high-risk")))
        {
            return Task.FromResult(PolicyOutcome.RequireApprovalResult(
                $"Tool '{context.ToolName}' requires approval (risk level: {context.Metadata.RiskLevel})"));
        }

        return Task.FromResult(PolicyOutcome.AbstainResult());
    }
}
