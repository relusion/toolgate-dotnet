using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ToolGate.Approvals.Abstractions;
using ToolGate.Approvals.Models;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Configuration;
using ToolGate.Core.Models;

namespace ToolGate.Approvals.Tests;

public class DefaultApprovalProviderTests
{
    private readonly IApprovalStore _store = Substitute.For<IApprovalStore>();
    private readonly IRedactionProvider _redactionProvider = Substitute.For<IRedactionProvider>();
    private readonly ToolGateOptions _options = new() { DefaultApprovalTtl = TimeSpan.FromMinutes(15) };
    private readonly DefaultApprovalProvider _provider;

    public DefaultApprovalProviderTests()
    {
        _store.CreateAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<ApprovalRequest>()));

        _provider = new DefaultApprovalProvider(
            _store,
            _redactionProvider,
            Options.Create(_options));
    }

    private static ToolCallContext CreateContext(string toolName = "test_tool") => new()
    {
        ToolName = toolName,
        InvocationId = "inv-123",
        Timestamp = DateTimeOffset.UtcNow,
        Arguments = new Dictionary<string, object?> { ["key"] = "value" },
        CorrelationId = "corr-456",
        UserId = "user@example.com"
    };

    [Fact]
    public async Task CreateRequestAsync_BuildsRequestWithGuidId()
    {
        var context = CreateContext();

        var result = await _provider.CreateRequestAsync(context, "High risk tool");

        result.ApprovalId.Should().NotBeNullOrEmpty();
        result.ApprovalId.Should().HaveLength(32); // GUID without hyphens
    }

    [Fact]
    public async Task CreateRequestAsync_SetsCorrectFields()
    {
        var context = CreateContext();

        var result = await _provider.CreateRequestAsync(context, "Requires human review");

        result.InvocationId.Should().Be("inv-123");
        result.ToolName.Should().Be("test_tool");
        result.State.Should().Be(ApprovalState.Pending);
        result.CorrelationId.Should().Be("corr-456");
        result.RequestedBy.Should().Be("user@example.com");
        result.Reason.Should().Be("Requires human review");
    }

    [Fact]
    public async Task CreateRequestAsync_AppliesTtlFromOptions()
    {
        var context = CreateContext();

        var before = DateTimeOffset.UtcNow;
        var result = await _provider.CreateRequestAsync(context, null);
        var after = DateTimeOffset.UtcNow;

        result.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        var expectedExpiry = result.CreatedAt + TimeSpan.FromMinutes(15);
        result.ExpiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateRequestAsync_RedactsArguments()
    {
        var redacted = new Dictionary<string, object?> { ["key"] = "[REDACTED]" };
        _redactionProvider.Redact(Arg.Any<IDictionary<string, object?>?>()).Returns(redacted);

        var context = CreateContext();
        var result = await _provider.CreateRequestAsync(context, null);

        result.RedactedArguments.Should().BeSameAs(redacted);
        _redactionProvider.Received(1).Redact(context.Arguments);
    }

    [Fact]
    public async Task CreateRequestAsync_DelegatesToStore()
    {
        var context = CreateContext();
        await _provider.CreateRequestAsync(context, null);

        await _store.Received(1).CreateAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindByInvocationIdAsync_DelegatesToStore()
    {
        var expected = new ApprovalRequest
        {
            ApprovalId = "ap-1",
            InvocationId = "inv-1",
            ToolName = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        };
        _store.FindByInvocationIdAsync("inv-1", Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _provider.FindByInvocationIdAsync("inv-1");

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task ApproveAsync_TransitionsToApproved()
    {
        var expectedResult = new ApprovalActionResult { Success = true, CurrentState = ApprovalState.Approved };
        _store.TransitionAsync("ap-1", ApprovalState.Approved, Arg.Any<ApprovalAction>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var result = await _provider.ApproveAsync("ap-1", actor: "admin", comment: "LGTM");

        result.Success.Should().BeTrue();
        await _store.Received(1).TransitionAsync(
            "ap-1",
            ApprovalState.Approved,
            Arg.Is<ApprovalAction>(a =>
                a.TargetState == ApprovalState.Approved &&
                a.Actor == "admin" &&
                a.Comment == "LGTM"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DenyAsync_TransitionsToDenied()
    {
        var expectedResult = new ApprovalActionResult { Success = true, CurrentState = ApprovalState.Denied };
        _store.TransitionAsync("ap-1", ApprovalState.Denied, Arg.Any<ApprovalAction>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var result = await _provider.DenyAsync("ap-1", actor: "admin", comment: "Too risky");

        result.Success.Should().BeTrue();
        await _store.Received(1).TransitionAsync(
            "ap-1",
            ApprovalState.Denied,
            Arg.Is<ApprovalAction>(a =>
                a.TargetState == ApprovalState.Denied &&
                a.Actor == "admin" &&
                a.Comment == "Too risky"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRequestAsync_SetsContentHash()
    {
        var context = CreateContext();
        var result = await _provider.CreateRequestAsync(context, null);

        result.ContentHash.Should().NotBeNullOrEmpty();
        result.ContentHash.Should().HaveLength(64); // SHA-256 hex
    }

    [Fact]
    public async Task CreateRequestAsync_ContentHashUsesOriginalArguments()
    {
        // Verify content hash is computed from original (pre-redaction) arguments
        var context = CreateContext();
        var expectedHash = ContentHasher.Compute(context.ToolName, context.Arguments);

        var result = await _provider.CreateRequestAsync(context, null);

        result.ContentHash.Should().Be(expectedHash);
    }

    [Fact]
    public async Task QueryAsync_DelegatesToStore()
    {
        var query = new ApprovalQuery { ToolName = "test_tool" };
        var expected = new List<ApprovalRequest>().AsReadOnly();
        _store.QueryAsync(query, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _provider.QueryAsync(query);

        result.Should().BeSameAs(expected);
        await _store.Received(1).QueryAsync(query, Arg.Any<CancellationToken>());
    }
}
