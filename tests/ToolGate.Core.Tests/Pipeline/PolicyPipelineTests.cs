using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Configuration;
using ToolGate.Core.Models;
using ToolGate.Core.Pipeline;

namespace ToolGate.Core.Tests.Pipeline;

public class PolicyPipelineTests
{
    private readonly IRedactionProvider _redaction = Substitute.For<IRedactionProvider>();
    private readonly IAuditSink _audit = Substitute.For<IAuditSink>();
    private readonly ILogger<PolicyPipeline> _logger = NullLogger<PolicyPipeline>.Instance;

    private PolicyPipeline CreatePipeline(
        IEnumerable<IToolPolicy> policies,
        ToolGateOptions? options = null)
    {
        var opts = Options.Create(options ?? new ToolGateOptions());
        return new PolicyPipeline(policies, _redaction, _audit, _logger, opts);
    }

    private static ToolCallContext CreateContext(string toolName = "test_tool") => new()
    {
        ToolName = toolName,
        InvocationId = Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow
    };

    private static IToolPolicy CreatePolicy(
        string name,
        PolicyOutcome outcome,
        int order = 0)
    {
        var policy = Substitute.For<IToolPolicy>();
        policy.Name.Returns(name);
        policy.Order.Returns(order);
        policy.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(outcome));
        return policy;
    }

    // --- Basic Decision Tests ---

    [Fact]
    public async Task SingleAllowPolicy_ReturnsAllow()
    {
        var policy = CreatePolicy("AllowAll", PolicyOutcome.AllowResult("Allowed"));
        var pipeline = CreatePipeline([policy]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Allow);
        decision.PolicyMatches.Should().HaveCount(1);
        decision.PolicyMatches[0].PolicyName.Should().Be("AllowAll");
    }

    [Fact]
    public async Task SingleDenyPolicy_ReturnsDeny()
    {
        var policy = CreatePolicy("DenyAll", PolicyOutcome.DenyResult("Blocked", "Tool is blocked"));
        var pipeline = CreatePipeline([policy]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Deny);
        decision.ReasonCode.Should().Be("Blocked");
        decision.ReasonText.Should().Be("Tool is blocked");
    }

    [Fact]
    public async Task SingleRequireApprovalPolicy_ReturnsRequireApproval()
    {
        var policy = CreatePolicy("RequireApproval", PolicyOutcome.RequireApprovalResult("High-risk"));
        var pipeline = CreatePipeline([policy]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.RequireApproval);
        decision.ReasonText.Should().Be("High-risk");
    }

    [Fact]
    public async Task SingleAbstainPolicy_ReturnsDefaultDecision()
    {
        var policy = CreatePolicy("Abstainer", PolicyOutcome.AbstainResult());
        var pipeline = CreatePipeline([policy]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Deny); // Default is Deny
        decision.ReasonCode.Should().Be("DefaultDecision");
    }

    [Fact]
    public async Task NoPolicies_ReturnsDefaultDecision()
    {
        var pipeline = CreatePipeline([]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Deny);
        decision.ReasonCode.Should().Be("DefaultDecision");
    }

    // --- Aggregation Tests ---

    [Fact]
    public async Task DenyShortCircuits_BeforeLaterAllow()
    {
        var deny = CreatePolicy("Denylist", PolicyOutcome.DenyResult("Blocked", "Blocked"), order: -1000);
        var allow = CreatePolicy("Allowlist", PolicyOutcome.AllowResult("Allowed"), order: -500);
        var pipeline = CreatePipeline([deny, allow]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Deny);
        decision.PolicyMatches.Should().HaveCount(1);
        decision.PolicyMatches[0].PolicyName.Should().Be("Denylist");

        // Allow policy should not have been called
        await allow.DidNotReceive()
            .EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequireApprovalWinsOverAllow()
    {
        var allow = CreatePolicy("Allowlist", PolicyOutcome.AllowResult("Allowed"), order: -500);
        var approval = CreatePolicy("HighRisk", PolicyOutcome.RequireApprovalResult("Risky"), order: 500);
        var pipeline = CreatePipeline([allow, approval]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.RequireApproval);
        decision.PolicyMatches.Should().HaveCount(2);
    }

    [Fact]
    public async Task AllAbstain_ReturnsDefaultDecision()
    {
        var p1 = CreatePolicy("P1", PolicyOutcome.AbstainResult());
        var p2 = CreatePolicy("P2", PolicyOutcome.AbstainResult());
        var pipeline = CreatePipeline([p1, p2]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Deny);
    }

    [Fact]
    public async Task DefaultDecisionCanBeConfiguredToAllow()
    {
        var p1 = CreatePolicy("P1", PolicyOutcome.AbstainResult());
        var pipeline = CreatePipeline([p1], new ToolGateOptions { DefaultDecision = Decision.Allow });

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Allow);
    }

    // --- Ordering Tests ---

    [Fact]
    public async Task PoliciesEvaluatedInOrderByOrderProperty()
    {
        var callOrder = new List<string>();

        var p1 = Substitute.For<IToolPolicy>();
        p1.Name.Returns("Late");
        p1.Order.Returns(100);
        p1.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(ci => { callOrder.Add("Late"); return Task.FromResult(PolicyOutcome.AbstainResult()); });

        var p2 = Substitute.For<IToolPolicy>();
        p2.Name.Returns("Early");
        p2.Order.Returns(-100);
        p2.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(ci => { callOrder.Add("Early"); return Task.FromResult(PolicyOutcome.AbstainResult()); });

        var pipeline = CreatePipeline([p1, p2]);
        await pipeline.EvaluateAsync(CreateContext());

        callOrder.Should().ContainInOrder("Early", "Late");
    }

    // --- Exception Handling Tests ---

    [Fact]
    public async Task PolicyException_FailClosed_ReturnsDeny()
    {
        var faulty = Substitute.For<IToolPolicy>();
        faulty.Name.Returns("FaultyPolicy");
        faulty.Order.Returns(0);
        faulty.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Bug"));

        var pipeline = CreatePipeline([faulty], new ToolGateOptions { FailMode = FailMode.FailClosed });

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Deny);
        decision.ReasonCode.Should().Be("PolicyException");
        decision.ReasonText.Should().Contain("FaultyPolicy");
    }

    [Fact]
    public async Task PolicyException_FailOpen_SkipsAndContinues()
    {
        var faulty = Substitute.For<IToolPolicy>();
        faulty.Name.Returns("FaultyPolicy");
        faulty.Order.Returns(-100);
        faulty.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Bug"));

        var allow = CreatePolicy("AllowAll", PolicyOutcome.AllowResult("OK"), order: 100);

        var pipeline = CreatePipeline([faulty, allow], new ToolGateOptions { FailMode = FailMode.FailOpen });

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Allow);
    }

    [Fact]
    public async Task OperationCancelledException_PropagatesImmediately()
    {
        var policy = Substitute.For<IToolPolicy>();
        policy.Name.Returns("CancellingPolicy");
        policy.Order.Returns(0);
        policy.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var pipeline = CreatePipeline([policy]);

        var act = () => pipeline.EvaluateAsync(CreateContext());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CancellationToken_PropagatedToPolicies()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        var policy = Substitute.For<IToolPolicy>();
        policy.Name.Returns("TokenCapture");
        policy.Order.Returns(0);
        policy.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedToken = ci.ArgAt<CancellationToken>(1);
                return Task.FromResult(PolicyOutcome.AllowResult());
            });

        var pipeline = CreatePipeline([policy]);
        await pipeline.EvaluateAsync(CreateContext(), cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    // --- PolicyMatches Collection ---

    [Fact]
    public async Task PolicyMatches_CollectedForNonAbstainOutcomes()
    {
        var abstain = CreatePolicy("Abstainer", PolicyOutcome.AbstainResult(), order: -1000);
        var allow = CreatePolicy("Allowlist", PolicyOutcome.AllowResult("OK"), order: -500);
        var pipeline = CreatePipeline([abstain, allow]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.PolicyMatches.Should().HaveCount(1);
        decision.PolicyMatches[0].PolicyName.Should().Be("Allowlist");
    }

    // --- Timing ---

    [Fact]
    public async Task EvaluationDuration_IsRecorded()
    {
        var policy = CreatePolicy("Slow", PolicyOutcome.AllowResult());
        var pipeline = CreatePipeline([policy]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.EvaluationDuration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        decision.EvaluatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    // --- Audit Sink ---

    [Fact]
    public async Task AuditSink_CalledOnEvaluation()
    {
        var policy = CreatePolicy("AllowAll", PolicyOutcome.AllowResult());
        var pipeline = CreatePipeline([policy]);

        await pipeline.EvaluateAsync(CreateContext());

        await _audit.Received(1).RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditSinkException_DoesNotBreakEvaluation()
    {
        _audit.RecordAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Audit failed"));

        var policy = CreatePolicy("AllowAll", PolicyOutcome.AllowResult());
        var pipeline = CreatePipeline([policy]);

        var decision = await pipeline.EvaluateAsync(CreateContext());

        decision.Decision.Should().Be(Decision.Allow);
    }
}
