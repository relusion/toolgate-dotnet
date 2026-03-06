using FluentAssertions;
using ToolGate.Approvals.Models;

namespace ToolGate.Approvals.Tests.Models;

public class ApprovalModelTests
{
    [Fact]
    public void ApprovalRequest_RequiredProperties_CanBeConstructed()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new ApprovalRequest
        {
            ApprovalId = Guid.NewGuid().ToString(),
            InvocationId = "inv-001",
            ToolName = "send_email",
            State = ApprovalState.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30)
        };

        request.State.Should().Be(ApprovalState.Pending);
        request.ToolName.Should().Be("send_email");
        request.ExpiresAt.Should().BeAfter(request.CreatedAt);
    }

    [Fact]
    public void ApprovalRequest_OptionalProperties_DefaultToNull()
    {
        var request = new ApprovalRequest
        {
            ApprovalId = "test",
            InvocationId = "inv-002",
            ToolName = "test_tool",
            State = ApprovalState.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        request.RedactedArguments.Should().BeNull();
        request.CorrelationId.Should().BeNull();
        request.RequestedBy.Should().BeNull();
        request.Reason.Should().BeNull();
    }

    [Fact]
    public void ApprovalAction_HasDefaults()
    {
        var action = new ApprovalAction
        {
            TargetState = ApprovalState.Approved
        };

        action.TargetState.Should().Be(ApprovalState.Approved);
        action.Actor.Should().BeNull();
        action.Comment.Should().BeNull();
        action.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ApprovalActionResult_Success()
    {
        var result = new ApprovalActionResult
        {
            Success = true,
            CurrentState = ApprovalState.Approved
        };

        result.Success.Should().BeTrue();
        result.CurrentState.Should().Be(ApprovalState.Approved);
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public void ApprovalActionResult_Failure()
    {
        var result = new ApprovalActionResult
        {
            Success = false,
            CurrentState = ApprovalState.Approved,
            FailureReason = "Already approved"
        };

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("Already approved");
    }

    [Theory]
    [InlineData(ApprovalState.Pending)]
    [InlineData(ApprovalState.Approved)]
    [InlineData(ApprovalState.Denied)]
    [InlineData(ApprovalState.Expired)]
    [InlineData(ApprovalState.Cancelled)]
    public void ApprovalState_AllValuesExist(ApprovalState state)
    {
        state.Should().BeDefined();
    }
}
