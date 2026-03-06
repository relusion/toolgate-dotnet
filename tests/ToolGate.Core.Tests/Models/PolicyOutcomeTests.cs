using FluentAssertions;
using ToolGate.Core.Models;

namespace ToolGate.Core.Tests.Models;

public class PolicyOutcomeTests
{
    [Fact]
    public void AllowResult_ReturnsAllowDecision()
    {
        var outcome = PolicyOutcome.AllowResult("Tool is allowlisted");

        outcome.Decision.Should().Be(PolicyDecision.Allow);
        outcome.ReasonText.Should().Be("Tool is allowlisted");
        outcome.ReasonCode.Should().BeNull();
    }

    [Fact]
    public void AllowResult_WithNoReason_ReturnsNullReason()
    {
        var outcome = PolicyOutcome.AllowResult();

        outcome.Decision.Should().Be(PolicyDecision.Allow);
        outcome.ReasonText.Should().BeNull();
    }

    [Fact]
    public void DenyResult_ReturnsDenyDecisionWithCodeAndReason()
    {
        var outcome = PolicyOutcome.DenyResult("Denylisted", "Tool is in the denylist");

        outcome.Decision.Should().Be(PolicyDecision.Deny);
        outcome.ReasonCode.Should().Be("Denylisted");
        outcome.ReasonText.Should().Be("Tool is in the denylist");
    }

    [Fact]
    public void RequireApprovalResult_ReturnsRequireApprovalDecision()
    {
        var outcome = PolicyOutcome.RequireApprovalResult("High-risk tool");

        outcome.Decision.Should().Be(PolicyDecision.RequireApproval);
        outcome.ReasonText.Should().Be("High-risk tool");
        outcome.ReasonCode.Should().BeNull();
    }

    [Fact]
    public void AbstainResult_ReturnsAbstainDecision()
    {
        var outcome = PolicyOutcome.AbstainResult();

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
        outcome.ReasonText.Should().BeNull();
        outcome.ReasonCode.Should().BeNull();
    }
}
