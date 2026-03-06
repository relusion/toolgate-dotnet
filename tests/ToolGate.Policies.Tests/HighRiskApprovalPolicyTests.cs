using FluentAssertions;
using Microsoft.Extensions.Options;
using ToolGate.Core.Models;
using ToolGate.Policies.Configuration;

namespace ToolGate.Policies.Tests;

public class HighRiskApprovalPolicyTests
{
    private static HighRiskApprovalPolicy CreatePolicy(Action<HighRiskOptions>? configure = null)
    {
        var options = new HighRiskOptions();
        configure?.Invoke(options);
        return new HighRiskApprovalPolicy(Options.Create(options));
    }

    private static ToolCallContext CreateContext(
        RiskLevel riskLevel = RiskLevel.Normal,
        HashSet<string>? tags = null) => new()
    {
        ToolName = "some_tool",
        InvocationId = Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow,
        Metadata = new ToolMetadata
        {
            RiskLevel = riskLevel,
            Tags = tags ?? []
        }
    };

    [Fact]
    public async Task HighRiskLevel_ReturnsRequireApproval()
    {
        var policy = CreatePolicy();
        var context = CreateContext(riskLevel: RiskLevel.High);

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.RequireApproval);
        outcome.ReasonText.Should().Contain("requires approval");
    }

    [Fact]
    public async Task CriticalRiskLevel_ReturnsRequireApproval()
    {
        var policy = CreatePolicy();
        var context = CreateContext(riskLevel: RiskLevel.Critical);

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.RequireApproval);
    }

    [Fact]
    public async Task NormalRiskLevel_ReturnsAbstain()
    {
        var policy = CreatePolicy();
        var context = CreateContext(riskLevel: RiskLevel.Normal);

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
    }

    [Fact]
    public async Task LowRiskLevel_ReturnsAbstain()
    {
        var policy = CreatePolicy();
        var context = CreateContext(riskLevel: RiskLevel.Low);

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
    }

    [Fact]
    public async Task HighRiskTag_ReturnsRequireApproval()
    {
        var policy = CreatePolicy();
        var context = CreateContext(tags: ["high-risk"]);

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.RequireApproval);
    }

    [Fact]
    public async Task HighRiskTag_WithLowRiskLevel_StillRequiresApproval()
    {
        var policy = CreatePolicy();
        var context = CreateContext(riskLevel: RiskLevel.Low, tags: ["high-risk"]);

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.RequireApproval);
    }

    [Fact]
    public async Task CustomMinimumRiskLevel_Normal_TriggersOnNormal()
    {
        var policy = CreatePolicy(o => o.MinimumRiskLevel = RiskLevel.Normal);
        var context = CreateContext(riskLevel: RiskLevel.Normal);

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.RequireApproval);
    }

    [Fact]
    public async Task CustomMinimumRiskLevel_Critical_IgnoresHigh()
    {
        var policy = CreatePolicy(o => o.MinimumRiskLevel = RiskLevel.Critical);
        var context = CreateContext(riskLevel: RiskLevel.High);

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
    }

    [Fact]
    public async Task NoMetadata_ReturnsAbstain()
    {
        var policy = CreatePolicy();
        var context = new ToolCallContext
        {
            ToolName = "some_tool",
            InvocationId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = null
        };

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
    }

    [Fact]
    public void Name_ShouldBeHighRiskApproval()
    {
        var policy = CreatePolicy();
        policy.Name.Should().Be("HighRiskApproval");
    }

    [Fact]
    public void Order_ShouldBe500()
    {
        var policy = CreatePolicy();
        policy.Order.Should().Be(500);
    }
}
