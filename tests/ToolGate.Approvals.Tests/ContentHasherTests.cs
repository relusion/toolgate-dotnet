using System.Text.Json;
using FluentAssertions;

namespace ToolGate.Approvals.Tests;

public class ContentHasherTests
{
    [Fact]
    public void SameToolAndArgs_SameHash()
    {
        var args1 = new Dictionary<string, object?> { ["amount"] = 500, ["currency"] = "USD" };
        var args2 = new Dictionary<string, object?> { ["amount"] = 500, ["currency"] = "USD" };

        var hash1 = ContentHasher.Compute("submit_expense", args1);
        var hash2 = ContentHasher.Compute("submit_expense", args2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void DifferentArgs_DifferentHash()
    {
        var args1 = new Dictionary<string, object?> { ["amount"] = 500 };
        var args2 = new Dictionary<string, object?> { ["amount"] = 1000 };

        var hash1 = ContentHasher.Compute("submit_expense", args1);
        var hash2 = ContentHasher.Compute("submit_expense", args2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void DifferentToolName_DifferentHash()
    {
        var args = new Dictionary<string, object?> { ["id"] = 1 };

        var hash1 = ContentHasher.Compute("tool_a", args);
        var hash2 = ContentHasher.Compute("tool_b", args);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void NullArguments_ProducesHash()
    {
        var hash = ContentHasher.Compute("test_tool", null);

        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void EmptyArguments_SameAsNull()
    {
        var hashNull = ContentHasher.Compute("test_tool", null);
        var hashEmpty = ContentHasher.Compute("test_tool", new Dictionary<string, object?>());

        hashNull.Should().Be(hashEmpty);
    }

    [Fact]
    public void ArgumentOrderDoesNotMatter()
    {
        var args1 = new Dictionary<string, object?> { ["b"] = 2, ["a"] = 1 };
        var args2 = new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 };

        var hash1 = ContentHasher.Compute("test_tool", args1);
        var hash2 = ContentHasher.Compute("test_tool", args2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ReturnsHex64Chars()
    {
        var hash = ContentHasher.Compute("test_tool", new Dictionary<string, object?> { ["key"] = "value" });

        // SHA-256 = 32 bytes = 64 hex characters
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void JsonElement_Arguments_ProduceConsistentHash()
    {
        // Simulate arguments coming from MEAI deserialization (JsonElement values)
        var json = """{"amount":500,"currency":"USD"}""";
        using var doc = JsonDocument.Parse(json);
        var jsonArgs = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            jsonArgs[prop.Name] = prop.Value;

        var plainArgs = new Dictionary<string, object?> { ["amount"] = 500, ["currency"] = "USD" };

        // JSON round-trip normalization ensures consistent hashing
        var hash1 = ContentHasher.Compute("test_tool", jsonArgs);
        var hash2 = ContentHasher.Compute("test_tool", plainArgs);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void NullValues_InArguments_ProducesConsistentHash()
    {
        var args1 = new Dictionary<string, object?> { ["key"] = null, ["other"] = "val" };
        var args2 = new Dictionary<string, object?> { ["key"] = null, ["other"] = "val" };

        var hash1 = ContentHasher.Compute("test_tool", args1);
        var hash2 = ContentHasher.Compute("test_tool", args2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void NestedObjects_ProduceConsistentHash()
    {
        var args1 = new Dictionary<string, object?>
        {
            ["config"] = new Dictionary<string, object?> { ["nested"] = "value" }
        };
        var args2 = new Dictionary<string, object?>
        {
            ["config"] = new Dictionary<string, object?> { ["nested"] = "value" }
        };

        var hash1 = ContentHasher.Compute("test_tool", args1);
        var hash2 = ContentHasher.Compute("test_tool", args2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void JsonElement_NestedObjects_MatchClrTypes()
    {
        // MEAI sends nested objects as JsonElement
        var json = """{"config":{"nested":"value"},"count":3}""";
        using var doc = JsonDocument.Parse(json);
        var jsonArgs = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            jsonArgs[prop.Name] = prop.Value;

        var clrArgs = new Dictionary<string, object?>
        {
            ["config"] = new Dictionary<string, object?> { ["nested"] = "value" },
            ["count"] = 3
        };

        var hash1 = ContentHasher.Compute("test_tool", jsonArgs);
        var hash2 = ContentHasher.Compute("test_tool", clrArgs);

        hash1.Should().Be(hash2);
    }
}
