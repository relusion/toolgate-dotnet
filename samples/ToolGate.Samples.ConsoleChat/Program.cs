using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ToolGate.Adapters.ExtensionsAI.DependencyInjection;
using ToolGate.Approvals.Abstractions;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Models;

// ── 1. Configure services ──────────────────────────────────────────────────
var services = new ServiceCollection();

services.AddLogging(b => b
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    })
    .SetMinimumLevel(LogLevel.Debug));

// Register ToolGate with built-in policies
services.AddToolGate(
    configureDenylist: deny =>
    {
        // Block destructive tools by exact name and pattern
        deny.DeniedTools.Add("delete_database");
        deny.DeniedTools.Add("drop_table");
        deny.DeniedPatterns.Add("rm_*");
    },
    configureAllowlist: allow =>
    {
        // Explicitly allow safe, read-only tools
        allow.AllowedTools.Add("get_weather");
        allow.AllowedTools.Add("get_time");
    });

// Register a custom policy that requires approval for email-related tools.
// This demonstrates ToolGate's extensibility — any IToolPolicy can participate
// in the pipeline alongside built-in policies.
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IToolPolicy, SendEmailApprovalPolicy>());

// Register a mock chat client wrapped with the ToolGate adapter.
// AddChatClient registers the inner client, then UseToolGate() inserts
// the ToolGate DelegatingChatClient into the pipeline.
services.AddChatClient(new MockChatClient())  // The "LLM" that returns tool calls
    .UseToolGate();                            // ToolGate intercepts tool calls here

var sp = services.BuildServiceProvider();
var chatClient = sp.GetRequiredService<IChatClient>();
var approvalProvider = sp.GetRequiredService<IApprovalProvider>();

// ── 2. Demonstrate all three decision types ─────────────────────────────────

Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║          ToolGate.NET v0.1 — Console Sample                 ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── Scenario A: Allowed tool call ───────────────────────────────────────────
Console.WriteLine("━━━ Scenario A: Allowed tool call (get_weather) ━━━");
var response = await chatClient.GetResponseAsync(
    [new(ChatRole.User, "What's the weather?")],
    new ChatOptions { ModelId = "mock-model", AdditionalProperties = new() { ["scenario"] = "allowed" } });

PrintResponse(response);

// ── Scenario B: Denied tool call ────────────────────────────────────────────
Console.WriteLine("━━━ Scenario B: Denied tool call (delete_database) ━━━");
response = await chatClient.GetResponseAsync(
    [new(ChatRole.User, "Delete the database please")],
    new ChatOptions { ModelId = "mock-model", AdditionalProperties = new() { ["scenario"] = "denied" } });

PrintResponse(response);

// ── Scenario C: Tool requiring approval ─────────────────────────────────────
Console.WriteLine("━━━ Scenario C: Tool requiring approval (send_email) ━━━");
response = await chatClient.GetResponseAsync(
    [new(ChatRole.User, "Send an email to alice@example.com")],
    new ChatOptions { ModelId = "mock-model", AdditionalProperties = new() { ["scenario"] = "approval" } });

PrintResponse(response);

// Extract the approval ID from the response
var approvalContent = response.Messages
    .SelectMany(m => m.Contents)
    .OfType<FunctionResultContent>()
    .FirstOrDefault(r => r.Result?.ToString()?.Contains("ApprovalRequired") == true);

if (approvalContent is not null)
{
    var resultText = approvalContent.Result!.ToString()!;
    var approvalId = resultText[(resultText.LastIndexOf("ApprovalId: ") + 12)..];

    Console.WriteLine($"  → Approval ID: {approvalId}");
    Console.WriteLine("  → Simulating approval by an admin...");

    var approvalResult = await approvalProvider.ApproveAsync(approvalId, actor: "admin@example.com");
    Console.WriteLine($"  → Approval result: Success={approvalResult.Success}, State={approvalResult.CurrentState}");
    Console.WriteLine();

    // ── Scenario D: Resume after approval ───────────────────────────────────
    Console.WriteLine("━━━ Scenario D: Resume after approval (same invocation) ━━━");
    response = await chatClient.GetResponseAsync(
        [new(ChatRole.User, "Retry send_email after approval")],
        new ChatOptions { ModelId = "mock-model", AdditionalProperties = new() { ["scenario"] = "resume" } });

    PrintResponse(response);
}

Console.WriteLine("━━━ Sample complete ━━━");

// ── Helpers ─────────────────────────────────────────────────────────────────

static void PrintResponse(ChatResponse response)
{
    foreach (var message in response.Messages)
    {
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent fc:
                    Console.WriteLine($"  [FunctionCall] {fc.Name}({FormatArgs(fc.Arguments)})");
                    break;
                case FunctionResultContent fr:
                    Console.WriteLine($"  [FunctionResult] {fr.Result}");
                    break;
                case TextContent tc:
                    Console.WriteLine($"  [Text] {tc.Text}");
                    break;
            }
        }
    }
    Console.WriteLine();
}

static string FormatArgs(IDictionary<string, object?>? args) =>
    args is null ? "" : string.Join(", ", args.Select(kv => $"{kv.Key}: {kv.Value}"));

// ═══════════════════════════════════════════════════════════════════════════
// Custom policy: requires approval for send_email tool.
// Demonstrates how to author a custom IToolPolicy that integrates seamlessly
// with the built-in policies in the evaluation pipeline.
// ═══════════════════════════════════════════════════════════════════════════
sealed class SendEmailApprovalPolicy : IToolPolicy
{
    public string Name => "SendEmailApproval";
    public int Order => 100; // After allowlist (-500) but before high-risk (500)

    public Task<PolicyOutcome> EvaluateAsync(
        ToolCallContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.ToolName.Equals("send_email", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PolicyOutcome.RequireApprovalResult(
                "send_email requires human approval before execution"));
        }

        return Task.FromResult(PolicyOutcome.AbstainResult());
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Mock chat client: simulates an LLM returning tool calls.
// In production, replace with an actual provider (OpenAI, Azure, Anthropic).
// ═══════════════════════════════════════════════════════════════════════════
sealed class MockChatClient : IChatClient
{
    // Stable invocation ID so the resume-after-approval scenario uses the same ID
    private const string SendEmailCallId = "call_send_email_001";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var scenario = options?.AdditionalProperties?["scenario"]?.ToString() ?? "allowed";

        var assistantMessage = scenario switch
        {
            "allowed" => new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call_weather_001", "get_weather",
                    new Dictionary<string, object?> { ["city"] = "Seattle" })
            ]),

            "denied" => new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call_delete_001", "delete_database",
                    new Dictionary<string, object?> { ["database"] = "production", ["confirm"] = true })
            ]),

            "approval" => new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent(SendEmailCallId, "send_email",
                    new Dictionary<string, object?>
                    {
                        ["to"] = "alice@example.com",
                        ["subject"] = "Q3 Report",
                        ["api_key"] = "sk-secret-key-12345"   // sensitive — will be redacted in audit
                    })
            ]),

            // Resume: same call ID so the approval handler finds the prior approval
            "resume" => new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent(SendEmailCallId, "send_email",
                    new Dictionary<string, object?>
                    {
                        ["to"] = "alice@example.com",
                        ["subject"] = "Q3 Report"
                    })
            ]),

            _ => new ChatMessage(ChatRole.Assistant, [new TextContent("I don't understand.")])
        };

        return Task.FromResult(new ChatResponse([assistantMessage]));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming not supported in this sample.");

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}
