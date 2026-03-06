using FluentAssertions;
using Microsoft.Extensions.Options;
using ToolGate.Core.Configuration;
using ToolGate.Core.Redaction;

namespace ToolGate.Core.Tests.Redaction;

public class DefaultRedactionProviderTests
{
    private DefaultRedactionProvider CreateProvider(RedactionOptions? redactionOpts = null)
    {
        var options = new ToolGateOptions();
        if (redactionOpts is not null)
            options.Redaction = redactionOpts;
        return new DefaultRedactionProvider(Options.Create(options));
    }

    [Fact]
    public void NullArguments_ReturnsNull()
    {
        var provider = CreateProvider();
        provider.Redact(null).Should().BeNull();
    }

    [Fact]
    public void EmptyArguments_ReturnsEmpty()
    {
        var provider = CreateProvider();
        var result = provider.Redact(new Dictionary<string, object?>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void KnownSensitiveKey_IsRedacted()
    {
        var provider = CreateProvider();
        var args = new Dictionary<string, object?>
        {
            ["password"] = "secret123",
            ["username"] = "admin"
        };

        var result = provider.Redact(args)!;

        result["password"].Should().Be("[REDACTED]");
        result["username"].Should().Be("admin");
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("PASSWORD")]
    [InlineData("pAsSwOrD")]
    public void SensitiveKeyMatching_IsCaseInsensitive(string key)
    {
        var provider = CreateProvider();
        var args = new Dictionary<string, object?> { [key] = "value" };

        var result = provider.Redact(args)!;

        result[key].Should().Be("[REDACTED]");
    }

    [Fact]
    public void AllDefaultSensitiveKeys_AreRedacted()
    {
        var provider = CreateProvider();
        var sensitiveKeys = new[]
        {
            "password", "secret", "token", "apikey", "api_key",
            "authorization", "credential", "connectionstring", "connection_string"
        };

        foreach (var key in sensitiveKeys)
        {
            var args = new Dictionary<string, object?> { [key] = "value" };
            var result = provider.Redact(args)!;
            result[key].Should().Be("[REDACTED]", because: $"key '{key}' is a default sensitive key");
        }
    }

    [Fact]
    public void NonSensitiveKeys_ArePreserved()
    {
        var provider = CreateProvider();
        var args = new Dictionary<string, object?>
        {
            ["city"] = "London",
            ["count"] = 42,
            ["data"] = null
        };

        var result = provider.Redact(args)!;

        result["city"].Should().Be("London");
        result["count"].Should().Be(42);
        result["data"].Should().BeNull();
    }

    [Fact]
    public void RegexPatterns_MatchAndRedact()
    {
        var provider = CreateProvider(new RedactionOptions
        {
            SensitiveKeys = [],
            SensitivePatterns = [".*_key$", "^secret_.*"]
        });

        var args = new Dictionary<string, object?>
        {
            ["api_key"] = "abc123",
            ["secret_value"] = "hidden",
            ["name"] = "visible"
        };

        var result = provider.Redact(args)!;

        result["api_key"].Should().Be("[REDACTED]");
        result["secret_value"].Should().Be("[REDACTED]");
        result["name"].Should().Be("visible");
    }

    [Fact]
    public void CustomSensitiveKeys_AddedViaOptions()
    {
        var redactionOpts = new RedactionOptions();
        redactionOpts.SensitiveKeys.Add("custom_secret");

        var provider = CreateProvider(redactionOpts);
        var args = new Dictionary<string, object?>
        {
            ["custom_secret"] = "value",
            ["password"] = "pass123" // default key still works
        };

        var result = provider.Redact(args)!;

        result["custom_secret"].Should().Be("[REDACTED]");
        result["password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void NoSensitiveKeysOrPatterns_ReturnsOriginalArguments()
    {
        var provider = CreateProvider(new RedactionOptions
        {
            SensitiveKeys = [],
            SensitivePatterns = []
        });

        var args = new Dictionary<string, object?> { ["password"] = "visible" };
        var result = provider.Redact(args)!;

        result["password"].Should().Be("visible");
    }

    [Fact]
    public void Redact_ReturnsNewDictionary_DoesNotModifyOriginal()
    {
        var provider = CreateProvider();
        var args = new Dictionary<string, object?> { ["password"] = "secret" };

        var result = provider.Redact(args)!;

        result.Should().NotBeSameAs(args);
        args["password"].Should().Be("secret");
    }
}
