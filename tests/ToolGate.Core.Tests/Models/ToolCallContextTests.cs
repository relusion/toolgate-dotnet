using System.Security.Claims;
using FluentAssertions;
using ToolGate.Core.Models;

namespace ToolGate.Core.Tests.Models;

public class ToolCallContextTests
{
    [Fact]
    public void RequiredProperties_MustBeSet()
    {
        var context = new ToolCallContext
        {
            ToolName = "get_weather",
            InvocationId = "inv-001",
            Timestamp = DateTimeOffset.UtcNow
        };

        context.ToolName.Should().Be("get_weather");
        context.InvocationId.Should().Be("inv-001");
        context.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void OptionalProperties_DefaultToNull()
    {
        var context = new ToolCallContext
        {
            ToolName = "test",
            InvocationId = "inv-002",
            Timestamp = DateTimeOffset.UtcNow
        };

        context.Arguments.Should().BeNull();
        context.CorrelationId.Should().BeNull();
        context.ConversationId.Should().BeNull();
        context.UserId.Should().BeNull();
        context.Principal.Should().BeNull();
        context.Framework.Should().BeNull();
        context.AgentId.Should().BeNull();
        context.ModelId.Should().BeNull();
        context.Provider.Should().BeNull();
        context.Metadata.Should().BeNull();
    }

    [Fact]
    public void FullyPopulated_RetainsAllValues()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")]));

        var context = new ToolCallContext
        {
            ToolName = "send_email",
            InvocationId = "inv-003",
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = new Dictionary<string, object?> { ["to"] = "user@example.com" },
            CorrelationId = "corr-001",
            ConversationId = "conv-001",
            UserId = "user-123",
            Principal = principal,
            Framework = "MEAI",
            AgentId = "agent-1",
            ModelId = "gpt-4",
            Provider = "OpenAI",
            Metadata = new ToolMetadata
            {
                RiskLevel = RiskLevel.High,
                Tags = ["email", "high-risk"],
                Category = "communication"
            }
        };

        context.Arguments.Should().ContainKey("to");
        context.Framework.Should().Be("MEAI");
        context.Metadata!.RiskLevel.Should().Be(RiskLevel.High);
        context.Metadata.Tags.Should().Contain("high-risk");
    }
}
