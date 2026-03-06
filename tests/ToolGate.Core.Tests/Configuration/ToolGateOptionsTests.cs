using FluentAssertions;
using ToolGate.Core.Configuration;
using ToolGate.Core.Models;

namespace ToolGate.Core.Tests.Configuration;

public class ToolGateOptionsTests
{
    [Fact]
    public void Defaults_AreSecure()
    {
        var options = new ToolGateOptions();

        options.FailMode.Should().Be(FailMode.FailClosed);
        options.DefaultDecision.Should().Be(Decision.Deny);
        options.DefaultApprovalTtl.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Redaction_HasDefaultSensitiveKeys()
    {
        var options = new ToolGateOptions();

        options.Redaction.Should().NotBeNull();
        options.Redaction.SensitiveKeys.Should().Contain("password");
        options.Redaction.SensitiveKeys.Should().Contain("secret");
        options.Redaction.SensitiveKeys.Should().Contain("token");
        options.Redaction.SensitiveKeys.Should().Contain("apikey");
        options.Redaction.SensitiveKeys.Should().Contain("api_key");
        options.Redaction.SensitiveKeys.Should().Contain("authorization");
        options.Redaction.SensitiveKeys.Should().Contain("credential");
        options.Redaction.SensitiveKeys.Should().Contain("connectionstring");
        options.Redaction.SensitiveKeys.Should().Contain("connection_string");
    }

    [Fact]
    public void Redaction_SensitivePatterns_DefaultsToEmpty()
    {
        var options = new ToolGateOptions();

        options.Redaction.SensitivePatterns.Should().BeEmpty();
    }

    [Fact]
    public void Properties_CanBeCustomized()
    {
        var options = new ToolGateOptions
        {
            FailMode = FailMode.FailOpen,
            DefaultDecision = Decision.Allow,
            DefaultApprovalTtl = TimeSpan.FromHours(1)
        };

        options.FailMode.Should().Be(FailMode.FailOpen);
        options.DefaultDecision.Should().Be(Decision.Allow);
        options.DefaultApprovalTtl.Should().Be(TimeSpan.FromHours(1));
    }
}
