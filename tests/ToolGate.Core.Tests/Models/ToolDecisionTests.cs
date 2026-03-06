using FluentAssertions;
using ToolGate.Core.Models;

namespace ToolGate.Core.Tests.Models;

public class ToolDecisionTests
{
    [Fact]
    public void ToolDecision_RequiredDecision_CanBeConstructed()
    {
        var decision = new ToolDecision
        {
            Decision = Decision.Allow,
            EvaluatedAt = DateTimeOffset.UtcNow,
            EvaluationDuration = TimeSpan.FromMilliseconds(3)
        };

        decision.Decision.Should().Be(Decision.Allow);
        decision.PolicyMatches.Should().BeEmpty();
        decision.ApprovalInfo.Should().BeNull();
        decision.ReasonCode.Should().BeNull();
        decision.ReasonText.Should().BeNull();
    }

    [Fact]
    public void ToolDecision_WithPolicyMatches_RetainsAll()
    {
        var matches = new List<PolicyMatch>
        {
            new() { PolicyName = "Denylist", Decision = PolicyDecision.Abstain },
            new() { PolicyName = "Allowlist", Decision = PolicyDecision.Allow, ReasonText = "Allowed" }
        };

        var decision = new ToolDecision
        {
            Decision = Decision.Allow,
            PolicyMatches = matches,
            EvaluatedAt = DateTimeOffset.UtcNow,
            EvaluationDuration = TimeSpan.FromMilliseconds(1)
        };

        decision.PolicyMatches.Should().HaveCount(2);
        decision.PolicyMatches[0].PolicyName.Should().Be("Denylist");
        decision.PolicyMatches[1].PolicyName.Should().Be("Allowlist");
    }

    [Fact]
    public void ToolDecision_WithApprovalInfo_ContainsDetails()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var decision = new ToolDecision
        {
            Decision = Decision.RequireApproval,
            ApprovalInfo = new ApprovalInfo
            {
                ApprovalId = "abc-123",
                ExpiresAt = expiresAt
            },
            EvaluatedAt = DateTimeOffset.UtcNow,
            EvaluationDuration = TimeSpan.FromMilliseconds(2)
        };

        decision.ApprovalInfo.Should().NotBeNull();
        decision.ApprovalInfo!.ApprovalId.Should().Be("abc-123");
        decision.ApprovalInfo.ExpiresAt.Should().Be(expiresAt);
    }
}
