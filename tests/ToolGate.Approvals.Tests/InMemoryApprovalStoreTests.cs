using FluentAssertions;
using ToolGate.Approvals.Models;

namespace ToolGate.Approvals.Tests;

public class InMemoryApprovalStoreTests
{
    private readonly InMemoryApprovalStore _store = new();

    private static ApprovalRequest CreateRequest(
        string? approvalId = null,
        string? invocationId = null,
        string? toolName = null,
        string? requestedBy = null,
        string? contentHash = null,
        DateTimeOffset? createdAt = null,
        TimeSpan? ttl = null) => new()
    {
        ApprovalId = approvalId ?? Guid.NewGuid().ToString("N"),
        InvocationId = invocationId ?? Guid.NewGuid().ToString(),
        ToolName = toolName ?? "test_tool",
        State = ApprovalState.Pending,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        ExpiresAt = (createdAt ?? DateTimeOffset.UtcNow) + (ttl ?? TimeSpan.FromMinutes(30)),
        RequestedBy = requestedBy,
        ContentHash = contentHash
    };

    [Fact]
    public async Task CreateAsync_StoresRequest()
    {
        var request = CreateRequest();

        var result = await _store.CreateAsync(request);

        result.Should().BeSameAs(request);

        var retrieved = await _store.GetAsync(request.ApprovalId);
        retrieved.Should().NotBeNull();
        retrieved!.ToolName.Should().Be("test_tool");
    }

    [Fact]
    public async Task CreateAsync_DuplicateId_Throws()
    {
        var request = CreateRequest(approvalId: "dup-id");
        await _store.CreateAsync(request);

        var duplicate = CreateRequest(approvalId: "dup-id");

        var act = () => _store.CreateAsync(duplicate);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dup-id*already exists*");
    }

    [Fact]
    public async Task CreateAsync_NullRequest_Throws()
    {
        var act = () => _store.CreateAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_NonExistentId_ReturnsNull()
    {
        var result = await _store.GetAsync("non-existent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByInvocationIdAsync_ReturnsMatchingRequest()
    {
        var request = CreateRequest(invocationId: "inv-123");
        await _store.CreateAsync(request);

        var result = await _store.FindByInvocationIdAsync("inv-123");

        result.Should().NotBeNull();
        result!.ApprovalId.Should().Be(request.ApprovalId);
    }

    [Fact]
    public async Task FindByInvocationIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _store.FindByInvocationIdAsync("non-existent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task TransitionAsync_PendingToApproved_Succeeds()
    {
        var request = CreateRequest();
        await _store.CreateAsync(request);

        var action = new ApprovalAction { TargetState = ApprovalState.Approved, Actor = "admin" };
        var result = await _store.TransitionAsync(request.ApprovalId, ApprovalState.Approved, action);

        result.Success.Should().BeTrue();
        result.CurrentState.Should().Be(ApprovalState.Approved);

        var updated = await _store.GetAsync(request.ApprovalId);
        updated!.State.Should().Be(ApprovalState.Approved);
    }

    [Fact]
    public async Task TransitionAsync_PendingToDenied_Succeeds()
    {
        var request = CreateRequest();
        await _store.CreateAsync(request);

        var action = new ApprovalAction { TargetState = ApprovalState.Denied };
        var result = await _store.TransitionAsync(request.ApprovalId, ApprovalState.Denied, action);

        result.Success.Should().BeTrue();
        result.CurrentState.Should().Be(ApprovalState.Denied);
    }

    [Fact]
    public async Task TransitionAsync_PendingToCancelled_Succeeds()
    {
        var request = CreateRequest();
        await _store.CreateAsync(request);

        var action = new ApprovalAction { TargetState = ApprovalState.Cancelled };
        var result = await _store.TransitionAsync(request.ApprovalId, ApprovalState.Cancelled, action);

        result.Success.Should().BeTrue();
        result.CurrentState.Should().Be(ApprovalState.Cancelled);
    }

    [Fact]
    public async Task TransitionAsync_AlreadyApproved_Fails_FirstWriterWins()
    {
        var request = CreateRequest();
        await _store.CreateAsync(request);

        var approve = new ApprovalAction { TargetState = ApprovalState.Approved };
        await _store.TransitionAsync(request.ApprovalId, ApprovalState.Approved, approve);

        // Second transition should fail
        var deny = new ApprovalAction { TargetState = ApprovalState.Denied };
        var result = await _store.TransitionAsync(request.ApprovalId, ApprovalState.Denied, deny);

        result.Success.Should().BeFalse();
        result.CurrentState.Should().Be(ApprovalState.Approved);
        result.FailureReason.Should().Contain("already in state");
    }

    [Fact]
    public async Task TransitionAsync_AlreadyDenied_Fails()
    {
        var request = CreateRequest();
        await _store.CreateAsync(request);

        var deny = new ApprovalAction { TargetState = ApprovalState.Denied };
        await _store.TransitionAsync(request.ApprovalId, ApprovalState.Denied, deny);

        var approve = new ApprovalAction { TargetState = ApprovalState.Approved };
        var result = await _store.TransitionAsync(request.ApprovalId, ApprovalState.Approved, approve);

        result.Success.Should().BeFalse();
        result.CurrentState.Should().Be(ApprovalState.Denied);
    }

    [Fact]
    public async Task TransitionAsync_NonExistentId_Fails()
    {
        var action = new ApprovalAction { TargetState = ApprovalState.Approved };
        var result = await _store.TransitionAsync("non-existent", ApprovalState.Approved, action);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not found");
    }

    [Fact]
    public async Task TransitionAsync_ExpiredRequest_Fails()
    {
        // Create a request that expires immediately
        var request = CreateRequest(ttl: TimeSpan.FromMilliseconds(-1));
        await _store.CreateAsync(request);

        var action = new ApprovalAction { TargetState = ApprovalState.Approved };
        var result = await _store.TransitionAsync(request.ApprovalId, ApprovalState.Approved, action);

        result.Success.Should().BeFalse();
        result.CurrentState.Should().Be(ApprovalState.Expired);
        result.FailureReason.Should().Contain("expired");
    }

    [Fact]
    public async Task GetAsync_ExpiredRequest_SetsExpiredState()
    {
        var request = CreateRequest(ttl: TimeSpan.FromMilliseconds(-1));
        await _store.CreateAsync(request);

        var retrieved = await _store.GetAsync(request.ApprovalId);

        retrieved.Should().NotBeNull();
        retrieved!.State.Should().Be(ApprovalState.Expired);
    }

    [Fact]
    public async Task FindByInvocationIdAsync_ExpiredRequest_SetsExpiredState()
    {
        var request = CreateRequest(invocationId: "inv-exp", ttl: TimeSpan.FromMilliseconds(-1));
        await _store.CreateAsync(request);

        var retrieved = await _store.FindByInvocationIdAsync("inv-exp");

        retrieved.Should().NotBeNull();
        retrieved!.State.Should().Be(ApprovalState.Expired);
    }

    [Fact]
    public async Task TransitionAsync_ToPending_Fails()
    {
        var request = CreateRequest();
        await _store.CreateAsync(request);

        var action = new ApprovalAction { TargetState = ApprovalState.Pending };
        var result = await _store.TransitionAsync(request.ApprovalId, ApprovalState.Pending, action);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Pending");
        // Request should still be in Pending state (no change)
        var current = await _store.GetAsync(request.ApprovalId);
        current!.State.Should().Be(ApprovalState.Pending);
    }

    [Fact]
    public async Task ConcurrentTransitions_OnlyOneSucceeds()
    {
        var request = CreateRequest();
        await _store.CreateAsync(request);

        var tasks = Enumerable.Range(0, 10).Select(i =>
        {
            var state = i % 2 == 0 ? ApprovalState.Approved : ApprovalState.Denied;
            var action = new ApprovalAction { TargetState = state, Actor = $"actor-{i}" };
            return _store.TransitionAsync(request.ApprovalId, state, action);
        });

        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Success).Should().Be(1);
        results.Count(r => !r.Success).Should().Be(9);
    }

    // ── QueryAsync tests ──────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_ByToolName_ReturnsMatching()
    {
        await _store.CreateAsync(CreateRequest(toolName: "tool_a"));
        await _store.CreateAsync(CreateRequest(toolName: "tool_b"));

        var results = await _store.QueryAsync(new ApprovalQuery { ToolName = "tool_a" });

        results.Should().ContainSingle().Which.ToolName.Should().Be("tool_a");
    }

    [Fact]
    public async Task QueryAsync_ByState_ReturnsMatching()
    {
        var req1 = CreateRequest();
        var req2 = CreateRequest();
        await _store.CreateAsync(req1);
        await _store.CreateAsync(req2);
        await _store.TransitionAsync(req1.ApprovalId, ApprovalState.Approved,
            new ApprovalAction { TargetState = ApprovalState.Approved });

        var results = await _store.QueryAsync(new ApprovalQuery { State = ApprovalState.Approved });

        results.Should().ContainSingle().Which.ApprovalId.Should().Be(req1.ApprovalId);
    }

    [Fact]
    public async Task QueryAsync_ByContentHash_ReturnsMatching()
    {
        await _store.CreateAsync(CreateRequest(contentHash: "HASH_A"));
        await _store.CreateAsync(CreateRequest(contentHash: "HASH_B"));

        var results = await _store.QueryAsync(new ApprovalQuery { ContentHash = "HASH_A" });

        results.Should().ContainSingle().Which.ContentHash.Should().Be("HASH_A");
    }

    [Fact]
    public async Task QueryAsync_ByRequestedBy_ReturnsMatching()
    {
        await _store.CreateAsync(CreateRequest(requestedBy: "alice"));
        await _store.CreateAsync(CreateRequest(requestedBy: "bob"));

        var results = await _store.QueryAsync(new ApprovalQuery { RequestedBy = "alice" });

        results.Should().ContainSingle().Which.RequestedBy.Should().Be("alice");
    }

    [Fact]
    public async Task QueryAsync_ByDateRange_ReturnsMatching()
    {
        var t1 = DateTimeOffset.UtcNow.AddHours(-2);
        var t2 = DateTimeOffset.UtcNow.AddHours(-1);
        var t3 = DateTimeOffset.UtcNow;

        await _store.CreateAsync(CreateRequest(createdAt: t1));
        await _store.CreateAsync(CreateRequest(createdAt: t2));
        await _store.CreateAsync(CreateRequest(createdAt: t3));

        var results = await _store.QueryAsync(new ApprovalQuery
        {
            CreatedAfter = t1.AddMinutes(1),
            CreatedBefore = t2.AddMinutes(1)
        });

        results.Should().ContainSingle().Which.CreatedAt.Should().Be(t2);
    }

    [Fact]
    public async Task QueryAsync_Limit_RespectsMaxResults()
    {
        for (var i = 0; i < 5; i++)
            await _store.CreateAsync(CreateRequest());

        var results = await _store.QueryAsync(new ApprovalQuery { Limit = 3 });

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task QueryAsync_OrderedByCreatedAtDescending()
    {
        var t1 = DateTimeOffset.UtcNow.AddHours(-2);
        var t2 = DateTimeOffset.UtcNow.AddHours(-1);
        var t3 = DateTimeOffset.UtcNow;

        await _store.CreateAsync(CreateRequest(createdAt: t1));
        await _store.CreateAsync(CreateRequest(createdAt: t3));
        await _store.CreateAsync(CreateRequest(createdAt: t2));

        var results = await _store.QueryAsync(new ApprovalQuery());

        results.Should().HaveCount(3);
        results[0].CreatedAt.Should().Be(t3);
        results[1].CreatedAt.Should().Be(t2);
        results[2].CreatedAt.Should().Be(t1);
    }

    [Fact]
    public async Task QueryAsync_CombinedFilters_AndSemantics()
    {
        var req1 = CreateRequest(toolName: "tool_a", contentHash: "HASH_1");
        var req2 = CreateRequest(toolName: "tool_a", contentHash: "HASH_2");
        var req3 = CreateRequest(toolName: "tool_b", contentHash: "HASH_1");
        await _store.CreateAsync(req1);
        await _store.CreateAsync(req2);
        await _store.CreateAsync(req3);

        // Transition req1 to Approved
        await _store.TransitionAsync(req1.ApprovalId, ApprovalState.Approved,
            new ApprovalAction { TargetState = ApprovalState.Approved });

        var results = await _store.QueryAsync(new ApprovalQuery
        {
            ContentHash = "HASH_1",
            State = ApprovalState.Approved
        });

        results.Should().ContainSingle().Which.ApprovalId.Should().Be(req1.ApprovalId);
    }

    [Fact]
    public async Task QueryAsync_ExpiresPendingRequestsDuringScan()
    {
        var request = CreateRequest(ttl: TimeSpan.FromMilliseconds(-1));
        await _store.CreateAsync(request);

        var results = await _store.QueryAsync(new ApprovalQuery { State = ApprovalState.Expired });

        results.Should().ContainSingle().Which.ApprovalId.Should().Be(request.ApprovalId);
    }

    [Fact]
    public async Task QueryAsync_ExpiresAfter_FiltersExpiredApprovals()
    {
        // Create one request that expires in the past and one in the future
        var pastExpiry = CreateRequest(
            createdAt: DateTimeOffset.UtcNow.AddHours(-2),
            ttl: TimeSpan.FromMinutes(30)); // expired 90 minutes ago
        var futureExpiry = CreateRequest(
            createdAt: DateTimeOffset.UtcNow,
            ttl: TimeSpan.FromHours(1)); // expires in 1 hour
        await _store.CreateAsync(pastExpiry);
        await _store.CreateAsync(futureExpiry);

        // Transition both to Approved
        await _store.TransitionAsync(pastExpiry.ApprovalId, ApprovalState.Approved,
            new ApprovalAction { TargetState = ApprovalState.Approved });
        await _store.TransitionAsync(futureExpiry.ApprovalId, ApprovalState.Approved,
            new ApprovalAction { TargetState = ApprovalState.Approved });

        // Query for non-expired approvals only
        var results = await _store.QueryAsync(new ApprovalQuery
        {
            State = ApprovalState.Approved,
            ExpiresAfter = DateTimeOffset.UtcNow
        });

        results.Should().ContainSingle().Which.ApprovalId.Should().Be(futureExpiry.ApprovalId);
    }

    [Fact]
    public async Task QueryAsync_NoMatches_ReturnsEmptyList()
    {
        await _store.CreateAsync(CreateRequest(toolName: "tool_a"));

        var results = await _store.QueryAsync(new ApprovalQuery { ToolName = "nonexistent" });

        results.Should().BeEmpty();
    }
}
