using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ToolGate.Approvals.Abstractions;
using ToolGate.Approvals.DependencyInjection;
using ToolGate.Approvals.Models;
using ToolGate.Core.Abstractions;
using ToolGate.Core.DependencyInjection;
using ToolGate.Core.Models;
using ToolGate.Policies.DependencyInjection;

namespace ToolGate.Core.Tests.Integration;

public class PipelineIntegrationTests
{
    private static ServiceProvider BuildProvider(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolGateCore();
        services.AddToolGatePolicies();
        services.AddToolGateApprovals();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static ToolCallContext CreateContext(
        string toolName = "some_tool",
        string? invocationId = null,
        RiskLevel riskLevel = RiskLevel.Normal,
        HashSet<string>? tags = null) => new()
    {
        ToolName = toolName,
        InvocationId = invocationId ?? Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow,
        Metadata = new ToolMetadata
        {
            RiskLevel = riskLevel,
            Tags = tags ?? []
        }
    };

    [Fact]
    public async Task DenylistedTool_IsDenied()
    {
        using var sp = BuildProvider(services =>
        {
            services.AddToolGatePolicies(
                configureDenylist: o => o.DeniedTools = ["dangerous_tool"]);
        });

        var gate = sp.GetRequiredService<IToolGate>();
        var context = CreateContext("dangerous_tool");

        var decision = await gate.EvaluateAsync(context);

        decision.Decision.Should().Be(Decision.Deny);
        decision.ReasonCode.Should().Be("Denylisted");
        decision.PolicyMatches.Should().ContainSingle(m => m.PolicyName == "Denylist");
    }

    [Fact]
    public async Task AllowlistedTool_IsAllowed()
    {
        using var sp = BuildProvider(services =>
        {
            services.AddToolGatePolicies(
                configureAllowlist: o => o.AllowedTools = ["safe_tool"]);
        });

        var gate = sp.GetRequiredService<IToolGate>();
        var context = CreateContext("safe_tool");

        var decision = await gate.EvaluateAsync(context);

        decision.Decision.Should().Be(Decision.Allow);
    }

    [Fact]
    public async Task DenylistTakesPriority_OverAllowlist()
    {
        using var sp = BuildProvider(services =>
        {
            services.AddToolGatePolicies(
                configureDenylist: o => o.DeniedTools = ["dual_tool"],
                configureAllowlist: o => o.AllowedTools = ["dual_tool"]);
        });

        var gate = sp.GetRequiredService<IToolGate>();
        var context = CreateContext("dual_tool");

        var decision = await gate.EvaluateAsync(context);

        // Denylist has Order=-1000, Allowlist has Order=-500
        // Deny short-circuits before Allowlist is evaluated
        decision.Decision.Should().Be(Decision.Deny);
    }

    [Fact]
    public async Task HighRiskTool_RequiresApproval_WithApprovalInfo()
    {
        using var sp = BuildProvider();

        var gate = sp.GetRequiredService<IToolGate>();
        var context = CreateContext(riskLevel: RiskLevel.High);

        var decision = await gate.EvaluateAsync(context);

        decision.Decision.Should().Be(Decision.RequireApproval);
        decision.ApprovalInfo.Should().NotBeNull();
        decision.ApprovalInfo!.ApprovalId.Should().NotBeNullOrEmpty();
        decision.ApprovalInfo.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task HighRiskTool_ResumeAfterApproval_IsAllowed()
    {
        using var sp = BuildProvider();

        var gate = sp.GetRequiredService<IToolGate>();
        var approvalProvider = sp.GetRequiredService<IApprovalProvider>();
        var invocationId = Guid.NewGuid().ToString();

        // First call: RequireApproval
        var context = CreateContext(
            toolName: "risky_tool",
            invocationId: invocationId,
            riskLevel: RiskLevel.High);
        var decision1 = await gate.EvaluateAsync(context);
        decision1.Decision.Should().Be(Decision.RequireApproval);

        // Human approves
        var approvalId = decision1.ApprovalInfo!.ApprovalId;
        var approvalResult = await approvalProvider.ApproveAsync(approvalId, actor: "admin");
        approvalResult.Success.Should().BeTrue();

        // Second call with same invocationId: should be allowed via approval bypass
        var decision2 = await gate.EvaluateAsync(context);
        decision2.Decision.Should().Be(Decision.Allow);
        decision2.ReasonCode.Should().Be("ApprovalGranted");
    }

    [Fact]
    public async Task HighRiskTool_DeniedApproval_ReEvaluatesSameInvocation()
    {
        using var sp = BuildProvider();

        var gate = sp.GetRequiredService<IToolGate>();
        var approvalProvider = sp.GetRequiredService<IApprovalProvider>();
        var invocationId = Guid.NewGuid().ToString();

        // First call: RequireApproval
        var context = CreateContext(
            toolName: "risky_tool",
            invocationId: invocationId,
            riskLevel: RiskLevel.High);
        var decision1 = await gate.EvaluateAsync(context);
        decision1.Decision.Should().Be(Decision.RequireApproval);

        // Human denies
        var approvalId = decision1.ApprovalInfo!.ApprovalId;
        await approvalProvider.DenyAsync(approvalId, actor: "admin");

        // Re-evaluate with same invocationId — denied != approved, so pipeline re-runs policies
        var decision2 = await gate.EvaluateAsync(context);
        decision2.Decision.Should().Be(Decision.RequireApproval);
    }

    [Fact]
    public async Task NoMatchingPolicy_DefaultDeny()
    {
        using var sp = BuildProvider();

        var gate = sp.GetRequiredService<IToolGate>();
        var context = CreateContext("unknown_tool");

        var decision = await gate.EvaluateAsync(context);

        // Default is Deny (FailClosed)
        decision.Decision.Should().Be(Decision.Deny);
        decision.ReasonCode.Should().Be("DefaultDecision");
    }

    [Fact]
    public async Task AllowlistWithDenyUnlisted_BlocksUnknownTools()
    {
        using var sp = BuildProvider(services =>
        {
            services.AddToolGatePolicies(
                configureAllowlist: o =>
                {
                    o.AllowedTools = ["safe_tool"];
                    o.DenyUnlisted = true;
                });
        });

        var gate = sp.GetRequiredService<IToolGate>();

        var allowed = await gate.EvaluateAsync(CreateContext("safe_tool"));
        allowed.Decision.Should().Be(Decision.Allow);

        var denied = await gate.EvaluateAsync(CreateContext("unknown_tool"));
        denied.Decision.Should().Be(Decision.Deny);
        denied.ReasonCode.Should().Be("NotAllowlisted");
    }

    [Fact]
    public async Task DenylistPattern_BlocksMatchingTools()
    {
        using var sp = BuildProvider(services =>
        {
            services.AddToolGatePolicies(
                configureDenylist: o => o.DeniedPatterns = ["delete_*"]);
        });

        var gate = sp.GetRequiredService<IToolGate>();

        var denied = await gate.EvaluateAsync(CreateContext("delete_database"));
        denied.Decision.Should().Be(Decision.Deny);

        var allowed = await gate.EvaluateAsync(
            CreateContext(toolName: "read_file", riskLevel: RiskLevel.Low));
        // No allowlist, no deny match, no high risk — default decision
        allowed.Decision.Should().Be(Decision.Deny); // default is Deny
    }

    [Fact]
    public async Task ApprovalExpired_DoesNotBypass()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolGateCore(o => o.DefaultApprovalTtl = TimeSpan.FromMilliseconds(1));
        services.AddToolGatePolicies();
        services.AddToolGateApprovals();
        using var sp = services.BuildServiceProvider();

        var gate = sp.GetRequiredService<IToolGate>();
        var invocationId = Guid.NewGuid().ToString();

        // First call: RequireApproval with very short TTL
        var context = CreateContext(
            toolName: "risky_tool",
            invocationId: invocationId,
            riskLevel: RiskLevel.High);
        var decision1 = await gate.EvaluateAsync(context);
        decision1.Decision.Should().Be(Decision.RequireApproval);

        // Wait for TTL to expire
        await Task.Delay(10);

        // Trying to approve should fail (expired)
        var approvalProvider = sp.GetRequiredService<IApprovalProvider>();
        var approvalResult = await approvalProvider.ApproveAsync(decision1.ApprovalInfo!.ApprovalId);
        approvalResult.Success.Should().BeFalse();
        approvalResult.CurrentState.Should().Be(ApprovalState.Expired);
    }

    [Fact]
    public async Task HighRiskTool_ResumeAfterApproval_DifferentInvocationId_SameContent_IsAllowed()
    {
        using var sp = BuildProvider();

        var gate = sp.GetRequiredService<IToolGate>();
        var approvalProvider = sp.GetRequiredService<IApprovalProvider>();

        var args = new Dictionary<string, object?> { ["amount"] = 500, ["currency"] = "USD" };

        // First call with invocationId "inv-1": RequireApproval
        var context1 = new ToolCallContext
        {
            ToolName = "submit_expense",
            InvocationId = "inv-1",
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = args,
            Metadata = new ToolMetadata { RiskLevel = RiskLevel.High }
        };
        var decision1 = await gate.EvaluateAsync(context1);
        decision1.Decision.Should().Be(Decision.RequireApproval);

        // Human approves
        var approvalId = decision1.ApprovalInfo!.ApprovalId;
        var approvalResult = await approvalProvider.ApproveAsync(approvalId, actor: "admin");
        approvalResult.Success.Should().BeTrue();

        // Second call with DIFFERENT invocationId "inv-2" but SAME ToolName+Args:
        // Should be allowed via content hash matching
        var context2 = new ToolCallContext
        {
            ToolName = "submit_expense",
            InvocationId = "inv-2",
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = args,
            Metadata = new ToolMetadata { RiskLevel = RiskLevel.High }
        };
        var decision2 = await gate.EvaluateAsync(context2);
        decision2.Decision.Should().Be(Decision.Allow);
        decision2.ReasonCode.Should().Be("ApprovalGranted");
    }

    [Fact]
    public async Task HighRiskTool_DifferentInvocationId_DifferentArgs_RequiresNewApproval()
    {
        using var sp = BuildProvider();

        var gate = sp.GetRequiredService<IToolGate>();
        var approvalProvider = sp.GetRequiredService<IApprovalProvider>();

        // First call: approve with amount=500
        var context1 = new ToolCallContext
        {
            ToolName = "submit_expense",
            InvocationId = "inv-1",
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = new Dictionary<string, object?> { ["amount"] = 500 },
            Metadata = new ToolMetadata { RiskLevel = RiskLevel.High }
        };
        var decision1 = await gate.EvaluateAsync(context1);
        decision1.Decision.Should().Be(Decision.RequireApproval);
        await approvalProvider.ApproveAsync(decision1.ApprovalInfo!.ApprovalId, actor: "admin");

        // Second call: different args (amount=1000) — should NOT match content hash
        var context2 = new ToolCallContext
        {
            ToolName = "submit_expense",
            InvocationId = "inv-2",
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = new Dictionary<string, object?> { ["amount"] = 1000 },
            Metadata = new ToolMetadata { RiskLevel = RiskLevel.High }
        };
        var decision2 = await gate.EvaluateAsync(context2);
        decision2.Decision.Should().Be(Decision.RequireApproval);
    }
}
