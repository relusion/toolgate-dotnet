using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ToolGate.Approvals.Abstractions;
using ToolGate.Approvals.Models;
using ToolGate.Core.Models;

namespace ToolGate.Approvals.Tests;

public class ApprovalHandlerTests
{
    private readonly IApprovalProvider _provider = Substitute.For<IApprovalProvider>();
    private readonly ApprovalHandler _handler;

    public ApprovalHandlerTests()
    {
        // Default: QueryAsync returns empty list (no content hash matches)
        _provider.QueryAsync(Arg.Any<ApprovalQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<ApprovalRequest>().AsReadOnly());

        _handler = new ApprovalHandler(_provider, NullLogger<ApprovalHandler>.Instance);
    }

    private static ToolCallContext CreateContext(
        string invocationId = "inv-1",
        string toolName = "test_tool",
        IDictionary<string, object?>? arguments = null) => new()
    {
        ToolName = toolName,
        InvocationId = invocationId,
        Timestamp = DateTimeOffset.UtcNow,
        Arguments = arguments
    };

    [Fact]
    public async Task IsApprovedAsync_InvocationIdMatch_ReturnsTrue()
    {
        var context = CreateContext();
        _provider.FindByInvocationIdAsync("inv-1", Arg.Any<CancellationToken>())
            .Returns(new ApprovalRequest
            {
                ApprovalId = "ap-1",
                InvocationId = "inv-1",
                ToolName = "test_tool",
                State = ApprovalState.Approved,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            });

        var result = await _handler.IsApprovedAsync(context);

        result.Should().BeTrue();
        // Should NOT call QueryAsync since InvocationId matched
        await _provider.DidNotReceive().QueryAsync(
            Arg.Any<ApprovalQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsApprovedAsync_PendingRequest_FallsBackToContentHash()
    {
        var context = CreateContext();
        _provider.FindByInvocationIdAsync("inv-1", Arg.Any<CancellationToken>())
            .Returns(new ApprovalRequest
            {
                ApprovalId = "ap-1",
                InvocationId = "inv-1",
                ToolName = "test_tool",
                State = ApprovalState.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            });

        var result = await _handler.IsApprovedAsync(context);

        result.Should().BeFalse();
        // Should have tried content hash fallback with expiration filter
        await _provider.Received(1).QueryAsync(
            Arg.Is<ApprovalQuery>(q =>
                q.State == ApprovalState.Approved &&
                q.ExpiresAfter != null &&
                q.Limit == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsApprovedAsync_ContentHashMatch_ReturnsTrue()
    {
        var args = new Dictionary<string, object?> { ["amount"] = 500 };
        var context = CreateContext(invocationId: "inv-2", arguments: args);

        // InvocationId lookup returns nothing
        _provider.FindByInvocationIdAsync("inv-2", Arg.Any<CancellationToken>())
            .Returns((ApprovalRequest?)null);

        // Content hash lookup returns an approved request
        var contentHash = ContentHasher.Compute("test_tool", args);
        _provider.QueryAsync(
                Arg.Is<ApprovalQuery>(q => q.ContentHash == contentHash && q.State == ApprovalState.Approved),
                Arg.Any<CancellationToken>())
            .Returns(new List<ApprovalRequest>
            {
                new()
                {
                    ApprovalId = "ap-1",
                    InvocationId = "inv-1",
                    ToolName = "test_tool",
                    State = ApprovalState.Approved,
                    ContentHash = contentHash,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                }
            }.AsReadOnly());

        var result = await _handler.IsApprovedAsync(context);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsApprovedAsync_BothPathsMiss_ReturnsFalse()
    {
        var context = CreateContext();
        _provider.FindByInvocationIdAsync("inv-1", Arg.Any<CancellationToken>())
            .Returns((ApprovalRequest?)null);

        var result = await _handler.IsApprovedAsync(context);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsApprovedAsync_DeniedRequest_FallsToContentHash_NoMatch_ReturnsFalse()
    {
        var context = CreateContext();
        _provider.FindByInvocationIdAsync("inv-1", Arg.Any<CancellationToken>())
            .Returns(new ApprovalRequest
            {
                ApprovalId = "ap-1",
                InvocationId = "inv-1",
                ToolName = "test_tool",
                State = ApprovalState.Denied,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            });

        var result = await _handler.IsApprovedAsync(context);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RequestApprovalAsync_ReturnsApprovalInfo()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        _provider.CreateRequestAsync(Arg.Any<ToolCallContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ApprovalRequest
            {
                ApprovalId = "ap-new",
                InvocationId = "inv-1",
                ToolName = "test_tool",
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt
            });

        var context = CreateContext();

        var result = await _handler.RequestApprovalAsync(context, "reason");

        result.ApprovalId.Should().Be("ap-new");
        result.ExpiresAt.Should().Be(expiresAt);
    }
}
