using FluentAssertions;
using Microsoft.Extensions.Options;
using ToolGate.Core.Models;
using ToolGate.Policies.Configuration;

namespace ToolGate.Policies.Tests;

public class AllowlistPolicyTests
{
    private static AllowlistPolicy CreatePolicy(Action<AllowlistOptions>? configure = null)
    {
        var options = new AllowlistOptions();
        configure?.Invoke(options);
        return new AllowlistPolicy(Options.Create(options));
    }

    private static ToolCallContext CreateContext(string toolName = "some_tool") => new()
    {
        ToolName = toolName,
        InvocationId = Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task AllowedTool_ReturnsAllow()
    {
        var policy = CreatePolicy(o => o.AllowedTools = ["read_file"]);
        var context = CreateContext("read_file");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Allow);
        outcome.ReasonText.Should().Contain("allowlisted");
    }

    [Fact]
    public async Task AllowedTool_CaseInsensitive_ReturnsAllow()
    {
        var policy = CreatePolicy(o => o.AllowedTools = ["Read_File"]);
        var context = CreateContext("READ_FILE");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Allow);
    }

    [Fact]
    public async Task UnlistedTool_DenyUnlistedTrue_ReturnsDeny()
    {
        var policy = CreatePolicy(o =>
        {
            o.AllowedTools = ["read_file"];
            o.DenyUnlisted = true;
        });
        var context = CreateContext("delete_file");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Deny);
        outcome.ReasonCode.Should().Be("NotAllowlisted");
        outcome.ReasonText.Should().Contain("not in the allowlist");
    }

    [Fact]
    public async Task UnlistedTool_DenyUnlistedFalse_ReturnsAbstain()
    {
        var policy = CreatePolicy(o =>
        {
            o.AllowedTools = ["read_file"];
            o.DenyUnlisted = false;
        });
        var context = CreateContext("delete_file");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
    }

    [Fact]
    public async Task EmptyAllowlist_DenyUnlistedTrue_ReturnsDeny()
    {
        var policy = CreatePolicy(o => o.DenyUnlisted = true);
        var context = CreateContext("any_tool");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Deny);
    }

    [Fact]
    public async Task EmptyAllowlist_DenyUnlistedFalse_ReturnsAbstain()
    {
        var policy = CreatePolicy();
        var context = CreateContext("any_tool");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
    }

    [Fact]
    public void Name_ShouldBeAllowlist()
    {
        var policy = CreatePolicy();
        policy.Name.Should().Be("Allowlist");
    }

    [Fact]
    public void Order_ShouldBeNegative500()
    {
        var policy = CreatePolicy();
        policy.Order.Should().Be(-500);
    }
}
