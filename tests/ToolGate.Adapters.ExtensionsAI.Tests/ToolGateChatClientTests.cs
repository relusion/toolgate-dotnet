using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Models;

namespace ToolGate.Adapters.ExtensionsAI.Tests;

public class ToolGateChatClientTests
{
    private readonly IToolGate _toolGate = Substitute.For<IToolGate>();
    private readonly ILogger<ToolGateChatClient> _logger = NullLogger<ToolGateChatClient>.Instance;

    private ToolGateChatClient CreateClient(IChatClient inner)
    {
        return new ToolGateChatClient(inner, _toolGate, _logger);
    }

    /// <summary>
    /// Creates a mock inner client that returns a fixed response for every call.
    /// </summary>
    private ToolGateChatClient CreateClient(ChatResponse innerResponse)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(innerResponse));
        return CreateClient(inner);
    }

    /// <summary>
    /// Creates a mock inner client that returns different responses on sequential calls.
    /// Used for testing the LLM feedback re-call path.
    /// </summary>
    private ToolGateChatClient CreateClientWithSequentialResponses(
        ChatResponse firstResponse,
        ChatResponse secondResponse)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(firstResponse),
                Task.FromResult(secondResponse));
        return CreateClient(inner);
    }

    private static ChatResponse CreateResponseWithToolCall(
        string toolName = "test_tool",
        string callId = "call-1",
        IDictionary<string, object?>? arguments = null)
    {
        var functionCall = new FunctionCallContent(callId, toolName, arguments);
        var message = new ChatMessage(ChatRole.Assistant, [functionCall]);
        return new ChatResponse([message]);
    }

    private static ChatResponse CreateResponseWithText(string text = "Hello world")
    {
        var message = new ChatMessage(ChatRole.Assistant, text);
        return new ChatResponse([message]);
    }

    [Fact]
    public async Task AllowedToolCall_PassesThrough()
    {
        var response = CreateResponseWithToolCall();
        var client = CreateClient(response);

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.Allow,
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var result = await client.GetResponseAsync([]);

        // FunctionCallContent should still be there, not InformationalOnly
        result.Messages.Should().HaveCount(1);
        var fcc = result.Messages[0].Contents.Should().ContainSingle()
            .Which.Should().BeOfType<FunctionCallContent>().Subject;
        fcc.InformationalOnly.Should().BeFalse();
    }

    [Fact]
    public async Task DeniedToolCall_MarkedInformationalOnly_WithToolResultMessage()
    {
        var firstResponse = CreateResponseWithToolCall("dangerous_tool", "call-1");
        var feedbackResponse = CreateResponseWithText("I cannot execute that tool because it is denylisted.");
        var client = CreateClientWithSequentialResponses(firstResponse, feedbackResponse);

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.Deny,
                ReasonText = "Tool is denylisted",
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var result = await client.GetResponseAsync([]);

        // Message 0: assistant with InformationalOnly FunctionCallContent
        var assistantMsg = result.Messages[0];
        assistantMsg.Role.Should().Be(ChatRole.Assistant);
        var fcc = assistantMsg.Contents.Should().ContainSingle()
            .Which.Should().BeOfType<FunctionCallContent>().Subject;
        fcc.CallId.Should().Be("call-1");
        fcc.InformationalOnly.Should().BeTrue();

        // Message 1: tool-role message with denial FunctionResultContent
        var toolMsg = result.Messages[1];
        toolMsg.Role.Should().Be(ChatRole.Tool);
        var frc = toolMsg.Contents.Should().ContainSingle()
            .Which.Should().BeOfType<FunctionResultContent>().Subject;
        frc.CallId.Should().Be("call-1");
        frc.Result!.ToString().Should().Contain("Denied");
        frc.Result!.ToString().Should().Contain("Tool is denylisted");

        // Message 2: LLM feedback text
        result.Messages[2].Role.Should().Be(ChatRole.Assistant);
        result.Messages[2].Text.Should().Contain("denylisted");
    }

    [Fact]
    public async Task RequireApprovalToolCall_MarkedInformationalOnly_WithApprovalResult()
    {
        var firstResponse = CreateResponseWithToolCall("risky_tool", "call-2");
        var feedbackResponse = CreateResponseWithText("This tool requires approval.");
        var client = CreateClientWithSequentialResponses(firstResponse, feedbackResponse);

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.RequireApproval,
                ReasonText = "Tool requires approval",
                ApprovalInfo = new ApprovalInfo
                {
                    ApprovalId = "ap-123",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                },
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var result = await client.GetResponseAsync([]);

        // Message 0: assistant with InformationalOnly FunctionCallContent
        var fcc = result.Messages[0].Contents.OfType<FunctionCallContent>().Single();
        fcc.CallId.Should().Be("call-2");
        fcc.InformationalOnly.Should().BeTrue();

        // Message 1: tool-role message with approval FunctionResultContent
        var frc = result.Messages[1].Contents.OfType<FunctionResultContent>().Single();
        frc.CallId.Should().Be("call-2");
        frc.Result!.ToString().Should().Contain("ApprovalRequired");
        frc.Result!.ToString().Should().Contain("ap-123");
    }

    [Fact]
    public async Task NoToolCalls_PassesThroughUnchanged()
    {
        var response = CreateResponseWithText("Just a text response");
        var client = CreateClient(response);

        var result = await client.GetResponseAsync([]);

        result.Messages.Should().HaveCount(1);
        result.Messages[0].Text.Should().Be("Just a text response");

        await _toolGate.DidNotReceive().EvaluateAsync(
            Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MultipleToolCalls_MixedAllowAndDeny_DeniedMarkedInformationalOnly()
    {
        var call1 = new FunctionCallContent("call-1", "safe_tool");
        var call2 = new FunctionCallContent("call-2", "dangerous_tool");
        var message = new ChatMessage(ChatRole.Assistant, [call1, call2]);
        var response = new ChatResponse([message]);
        var client = CreateClient(response);

        _toolGate.EvaluateAsync(
                Arg.Is<ToolCallContext>(c => c.ToolName == "safe_tool"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.Allow,
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        _toolGate.EvaluateAsync(
                Arg.Is<ToolCallContext>(c => c.ToolName == "dangerous_tool"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.Deny,
                ReasonText = "Denied",
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var result = await client.GetResponseAsync([]);

        // Message 0: assistant with both FunctionCallContent items
        result.Messages[0].Contents.Should().HaveCount(2);
        var allowedFcc = result.Messages[0].Contents[0].Should().BeOfType<FunctionCallContent>().Subject;
        allowedFcc.InformationalOnly.Should().BeFalse();

        var deniedFcc = result.Messages[0].Contents[1].Should().BeOfType<FunctionCallContent>().Subject;
        deniedFcc.InformationalOnly.Should().BeTrue();

        // Message 1: tool-role message with denied FunctionResultContent
        result.Messages.Should().HaveCount(2);
        var frc = result.Messages[1].Contents.OfType<FunctionResultContent>().Single();
        frc.CallId.Should().Be("call-2");
    }

    [Fact]
    public async Task MixedAllowAndDeny_NoRecall()
    {
        var call1 = new FunctionCallContent("call-1", "safe_tool");
        var call2 = new FunctionCallContent("call-2", "dangerous_tool");
        var message = new ChatMessage(ChatRole.Assistant, [call1, call2]);
        var response = new ChatResponse([message]);

        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
        var client = CreateClient(inner);

        _toolGate.EvaluateAsync(
                Arg.Is<ToolCallContext>(c => c.ToolName == "safe_tool"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolDecision { Decision = Decision.Allow, EvaluatedAt = DateTimeOffset.UtcNow });

        _toolGate.EvaluateAsync(
                Arg.Is<ToolCallContext>(c => c.ToolName == "dangerous_tool"),
                Arg.Any<CancellationToken>())
            .Returns(new ToolDecision { Decision = Decision.Deny, ReasonText = "Denied", EvaluatedAt = DateTimeOffset.UtcNow });

        await client.GetResponseAsync([]);

        // Inner client should only be called once (no re-call)
        await inner.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllDenied_LlmFeedbackEnabled_MakesRecall()
    {
        var firstResponse = CreateResponseWithToolCall("dangerous_tool", "call-1");
        var feedbackResponse = CreateResponseWithText("That tool is blocked.");

        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(firstResponse),
                Task.FromResult(feedbackResponse));
        var client = CreateClient(inner);

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.Deny,
                ReasonText = "Denied",
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var result = await client.GetResponseAsync([]);

        // Inner client should be called twice (initial + feedback re-call)
        await inner.Received(2).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());

        // Final response should include the feedback text
        result.Messages.Should().HaveCount(3); // assistant + tool result + feedback
        result.Messages[2].Text.Should().Be("That tool is blocked.");
    }

    [Fact]
    public async Task AllDenied_LlmFeedbackDisabled_NoRecall()
    {
        var response = CreateResponseWithToolCall("dangerous_tool", "call-1");

        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
        var client = CreateClient(inner);
        client.LlmFeedbackEnabled = false;

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.Deny,
                ReasonText = "Denied",
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var result = await client.GetResponseAsync([]);

        // Inner client should only be called once (no re-call)
        await inner.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());

        // Response has assistant + tool result, but no feedback text
        result.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task AllDenied_RecallFails_ReturnsResponseWithDenials()
    {
        var firstResponse = CreateResponseWithToolCall("dangerous_tool", "call-1");

        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(firstResponse),
                Task.FromException<ChatResponse>(new InvalidOperationException("LLM unavailable")));
        var client = CreateClient(inner);

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.Deny,
                ReasonText = "Denied",
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var result = await client.GetResponseAsync([]);

        // Should not throw — graceful degradation
        result.Messages.Should().HaveCount(2); // assistant + tool result, no feedback
        result.Messages[0].Contents.OfType<FunctionCallContent>().Single().InformationalOnly.Should().BeTrue();
        result.Messages[1].Contents.OfType<FunctionResultContent>().Single().Result!.ToString().Should().Contain("Denied");
    }

    [Fact]
    public async Task AllDenied_RecallOptions_ToolsStripped()
    {
        var firstResponse = CreateResponseWithToolCall("dangerous_tool", "call-1");
        var feedbackResponse = CreateResponseWithText("Blocked.");

        ChatOptions? capturedOptions = null;
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedOptions = ci.Arg<ChatOptions?>();
                return firstResponse;
            }, ci =>
            {
                capturedOptions = ci.Arg<ChatOptions?>();
                return feedbackResponse;
            });
        var client = CreateClient(inner);

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.Deny,
                ReasonText = "Denied",
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var tools = new List<AITool> { AIFunctionFactory.Create(() => "test", name: "test_fn") };
        await client.GetResponseAsync([], new ChatOptions { Tools = tools });

        // The re-call options should have Tools = null
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Tools.Should().BeNull();
    }

    [Fact]
    public async Task ContextBuilding_MapsFieldsCorrectly()
    {
        var args = new Dictionary<string, object?> { ["param1"] = "value1" };
        var response = CreateResponseWithToolCall("my_tool", "call-42", args);
        var client = CreateClient(response);

        ToolCallContext? capturedContext = null;
        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedContext = ci.Arg<ToolCallContext>();
                return new ToolDecision
                {
                    Decision = Decision.Allow,
                    EvaluatedAt = DateTimeOffset.UtcNow
                };
            });

        await client.GetResponseAsync([], new ChatOptions { ModelId = "gpt-4" });

        capturedContext.Should().NotBeNull();
        capturedContext!.ToolName.Should().Be("my_tool");
        capturedContext.InvocationId.Should().Be("call-42");
        capturedContext.Arguments.Should().BeSameAs(args); // zero-copy
        capturedContext.Framework.Should().Be("MEAI");
        capturedContext.ModelId.Should().Be("gpt-4");
    }

    [Fact]
    public async Task CancellationToken_PropagatedToToolGate()
    {
        var response = CreateResponseWithToolCall();
        var client = CreateClient(response);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.Allow,
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        await client.GetResponseAsync([], cancellationToken: token);

        await _toolGate.Received(1).EvaluateAsync(Arg.Any<ToolCallContext>(), token);
    }

    [Fact]
    public async Task RequireApproval_WithMetadata_SurfacesMetadataInResult()
    {
        var firstResponse = CreateResponseWithToolCall("risky_tool", "call-3");
        var feedbackResponse = CreateResponseWithText("Approval required.");
        var client = CreateClientWithSequentialResponses(firstResponse, feedbackResponse);

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.RequireApproval,
                ReasonText = "Expense requires manager approval",
                ApprovalInfo = new ApprovalInfo
                {
                    ApprovalId = "ap-456",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                    Metadata = new Dictionary<string, string>
                    {
                        ["WorkflowId"] = "JIRA-789",
                        ["TicketUrl"] = "https://jira.example.com/JIRA-789"
                    }
                },
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var result = await client.GetResponseAsync([]);

        var frc = result.Messages[1].Contents.OfType<FunctionResultContent>().Single();
        var resultText = frc.Result!.ToString()!;
        resultText.Should().Contain("ApprovalRequired");
        resultText.Should().Contain("ap-456");
        resultText.Should().Contain("Metadata:");
        resultText.Should().Contain("WorkflowId: JIRA-789");
        resultText.Should().Contain("TicketUrl: https://jira.example.com/JIRA-789");
    }

    [Fact]
    public async Task RequireApproval_WithoutMetadata_NoMetadataInResult()
    {
        var firstResponse = CreateResponseWithToolCall("risky_tool", "call-4");
        var feedbackResponse = CreateResponseWithText("Approval required.");
        var client = CreateClientWithSequentialResponses(firstResponse, feedbackResponse);

        _toolGate.EvaluateAsync(Arg.Any<ToolCallContext>(), Arg.Any<CancellationToken>())
            .Returns(new ToolDecision
            {
                Decision = Decision.RequireApproval,
                ReasonText = "Tool requires approval",
                ApprovalInfo = new ApprovalInfo
                {
                    ApprovalId = "ap-simple",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
                },
                EvaluatedAt = DateTimeOffset.UtcNow
            });

        var result = await client.GetResponseAsync([]);

        var frc = result.Messages[1].Contents.OfType<FunctionResultContent>().Single();
        var resultText = frc.Result!.ToString()!;
        resultText.Should().Contain("ap-simple");
        resultText.Should().NotContain("Metadata:");
    }
}
