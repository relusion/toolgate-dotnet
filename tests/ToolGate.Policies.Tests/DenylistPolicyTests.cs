using FluentAssertions;
using Microsoft.Extensions.Options;
using ToolGate.Core.Models;
using ToolGate.Policies.Configuration;

namespace ToolGate.Policies.Tests;

public class DenylistPolicyTests
{
    private static DenylistPolicy CreatePolicy(Action<DenylistOptions>? configure = null)
    {
        var options = new DenylistOptions();
        configure?.Invoke(options);
        return new DenylistPolicy(Options.Create(options));
    }

    private static ToolCallContext CreateContext(string toolName = "some_tool") => new()
    {
        ToolName = toolName,
        InvocationId = Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ExactMatch_ReturnsDeny()
    {
        var policy = CreatePolicy(o => o.DeniedTools = ["delete_file"]);
        var context = CreateContext("delete_file");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Deny);
        outcome.ReasonCode.Should().Be("Denylisted");
        outcome.ReasonText.Should().Contain("delete_file");
    }

    [Fact]
    public async Task ExactMatch_CaseInsensitive_ReturnsDeny()
    {
        var policy = CreatePolicy(o => o.DeniedTools = ["Delete_File"]);
        var context = CreateContext("DELETE_FILE");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Deny);
    }

    [Fact]
    public async Task NoMatch_ReturnsAbstain()
    {
        var policy = CreatePolicy(o => o.DeniedTools = ["delete_file"]);
        var context = CreateContext("read_file");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
    }

    [Fact]
    public async Task WildcardPattern_MatchesPrefix_ReturnsDeny()
    {
        var policy = CreatePolicy(o => o.DeniedPatterns = ["delete_*"]);
        var context = CreateContext("delete_database");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Deny);
        outcome.ReasonText.Should().Contain("pattern");
    }

    [Fact]
    public async Task WildcardPattern_MatchesSuffix_ReturnsDeny()
    {
        var policy = CreatePolicy(o => o.DeniedPatterns = ["*.destructive"]);
        var context = CreateContext("file.destructive");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Deny);
    }

    [Fact]
    public async Task WildcardPattern_NoMatch_ReturnsAbstain()
    {
        var policy = CreatePolicy(o => o.DeniedPatterns = ["delete_*"]);
        var context = CreateContext("read_file");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
    }

    [Fact]
    public async Task SingleCharPattern_Matches_ReturnsDeny()
    {
        var policy = CreatePolicy(o => o.DeniedPatterns = ["delete_?"]);
        var context = CreateContext("delete_x");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Deny);
    }

    [Fact]
    public async Task EmptyConfig_ReturnsAbstain()
    {
        var policy = CreatePolicy();
        var context = CreateContext("any_tool");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Abstain);
    }

    [Fact]
    public async Task ExactMatchTakesPriority_OverPatterns()
    {
        var policy = CreatePolicy(o =>
        {
            o.DeniedTools = ["delete_file"];
            o.DeniedPatterns = ["delete_*"];
        });
        var context = CreateContext("delete_file");

        var outcome = await policy.EvaluateAsync(context);

        outcome.Decision.Should().Be(PolicyDecision.Deny);
        outcome.ReasonCode.Should().Be("Denylisted");
        // Should match on exact name, not pattern
        outcome.ReasonText.Should().Contain("denylist").And.NotContain("pattern");
    }

    [Fact]
    public void Name_ShouldBeDenylist()
    {
        var policy = CreatePolicy();
        policy.Name.Should().Be("Denylist");
    }

    [Fact]
    public void Order_ShouldBeNegative1000()
    {
        var policy = CreatePolicy();
        policy.Order.Should().Be(-1000);
    }
}
