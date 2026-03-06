using FluentAssertions;
using ToolGate.Core.Models;

namespace ToolGate.Core.Tests.Models;

public class ToolMetadataTests
{
    [Fact]
    public void Defaults_AreNormal()
    {
        var metadata = new ToolMetadata();

        metadata.RiskLevel.Should().Be(RiskLevel.Normal);
        metadata.Tags.Should().BeEmpty();
        metadata.Category.Should().BeNull();
        metadata.Owner.Should().BeNull();
        metadata.CustomProperties.Should().BeNull();
    }

    [Fact]
    public void Tags_CanBePopulated()
    {
        var metadata = new ToolMetadata
        {
            Tags = ["high-risk", "pii", "financial"]
        };

        metadata.Tags.Should().HaveCount(3);
        metadata.Tags.Should().Contain("pii");
    }

    [Fact]
    public void CustomProperties_AreExtensible()
    {
        var metadata = new ToolMetadata
        {
            CustomProperties = new Dictionary<string, object?>
            {
                ["department"] = "engineering",
                ["maxRetries"] = 3
            }
        };

        metadata.CustomProperties.Should().ContainKey("department");
        metadata.CustomProperties!["maxRetries"].Should().Be(3);
    }
}
