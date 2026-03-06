using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ToolGate.Adapters.ExtensionsAI.DependencyInjection;
using ToolGate.Approvals.Abstractions;
using ToolGate.Approvals.Models;
using ToolGate.Core.Abstractions;
using ToolGate.Samples.ExpenseAgent.Policies;
using ToolGate.Samples.ExpenseAgent.Tools;

// ── 1. Load configuration ───────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var endpoint = config["AzureOpenAI:Endpoint"]
    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var apiKey = config["AzureOpenAI:ApiKey"]
    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
var deploymentName = config["AzureOpenAI:DeploymentName"]
    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
    ?? "gpt-4o";

if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("ERROR: Azure OpenAI credentials not configured.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Configure via user-secrets:");
    Console.Error.WriteLine("  cd samples/ToolGate.Samples.ExpenseAgent");
    Console.Error.WriteLine("  dotnet user-secrets set \"AzureOpenAI:Endpoint\" \"https://your-resource.openai.azure.com/\"");
    Console.Error.WriteLine("  dotnet user-secrets set \"AzureOpenAI:ApiKey\" \"your-api-key\"");
    Console.Error.WriteLine("  dotnet user-secrets set \"AzureOpenAI:DeploymentName\" \"gpt-4o\"");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Or via environment variables: AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT");
    return 1;
}

// ── 2. Build Azure OpenAI IChatClient ────────────────────────────────────────
IChatClient innerClient = new AzureOpenAIClient(
        new Uri(endpoint),
        new ApiKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .AsIChatClient();

// ── 3. Configure services ────────────────────────────────────────────────────
var services = new ServiceCollection();

services.AddLogging(b => b
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    })
    .SetMinimumLevel(LogLevel.Debug));

// Register ToolGate — no built-in policy config needed.
// All decisions come from the two custom policies below.
services.AddToolGate();

// Custom policies (registered via TryAddEnumerable to match library pattern)
services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolPolicy, ReadOnlyPolicy>());
services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolPolicy, AmountThresholdPolicy>());

// Build the MEAI pipeline.
// ChatClientBuilder applies Use() in reverse: last added = innermost.
// We need: FunctionInvoking (outer) → ToolGate (inner) → AzureOpenAI (base)
services.AddChatClient(innerClient)
    .UseFunctionInvocation()  // outermost — executes allowed tool calls
    .UseToolGate();           // innermost — blocks denied calls before execution

var sp = services.BuildServiceProvider();
var chatClient = sp.GetRequiredService<IChatClient>();
var approvalProvider = sp.GetRequiredService<IApprovalProvider>();

// ── 4. Define tools ──────────────────────────────────────────────────────────
var tools = new List<AITool>
{
    AIFunctionFactory.Create(SubmitExpenseTool.SubmitExpense, name: "submit_expense"),
    AIFunctionFactory.Create(ApproveExpenseTool.ApproveExpense, name: "approve_expense"),
    AIFunctionFactory.Create(QueryExpensesTool.QueryExpenses, name: "query_expenses"),
    AIFunctionFactory.Create(GenerateReportTool.GenerateReport, name: "generate_report"),
};

// ── 5. Background approval simulator ─────────────────────────────────────────
//
// WHAT THIS DEMONSTRATES:
//
// In production, approvals happen out-of-band: a manager clicks "Approve" in
// Slack, an admin uses a dashboard, or ServiceNow triggers a webhook. The
// approver is a DIFFERENT person in a DIFFERENT system.
//
// This background task simulates that external system. It polls for pending
// approvals every 3 seconds and auto-approves manager-level expense approvals
// ($100–$999). VP-level approvals ($1,000+) stay pending for manual !approve
// from the console, simulating a longer human review process.
//
// THE KEY FLOWS:
//
// Flow A — Manager-level ($100–$999), fully automatic:
//   1. User: "Submit a $500 hotel expense"
//   2. LLM calls submit_expense → allowed → expense stored as "Pending"
//   3. Background manager-bot approves expense in store after ~3 seconds
//   4. User: "Show my expenses" → expense is now "Approved"
//
// Flow B — VP-level ($1,000+), demonstrates content-hash matching:
//   1. User: "Submit a $2,000 equipment expense"
//   2. LLM calls submit_expense → allowed → expense stored as "Pending"
//   3. User: "Approve that expense" → LLM calls approve_expense → ToolGate blocks
//   4. User: !approve <id> from console → ToolGate authorization granted
//   5. User: "Try approving it again"
//   6. LLM calls approve_expense with NEW invocation ID → content-hash match!
//   7. Tool executes! Different invocation ID, same content = prior approval applies.
//
// Flow B shows that ToolGate prevents the AI from self-approving high-value
// expenses, while still allowing seamless retry via content-hash matching.

using var approverCts = new CancellationTokenSource();
var approverTask = Task.Run(async () =>
{
    while (!approverCts.Token.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), approverCts.Token)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (approverCts.Token.IsCancellationRequested) break;

        // ── Auto-approve manager-level expenses directly in the store ────
        // In production, this would be a manager clicking "Approve" in a dashboard
        // or Slack bot. Here we simulate it by polling pending expenses.
        var pendingExpenses = ExpenseStore.Query("Pending");
        foreach (var expense in pendingExpenses)
        {
            if (expense.Amount is >= 100m and < 1000m)
            {
                ExpenseStore.TryApprove(expense.Id, out _);
                Console.WriteLine();
                Console.WriteLine("  ┌─────────────────────────────────────────────────────────────");
                Console.WriteLine("  │ EXTERNAL APPROVAL SYSTEM (simulated manager review)");
                Console.WriteLine("  │");
                Console.WriteLine($"  │ Manager approved expense: {expense.Id} (${expense.Amount:N2})");
                Console.WriteLine($"  │ Category: {expense.Category} — {expense.Description}");
                Console.WriteLine("  │ Approved by: manager-bot (simulated Slack approval)");
                Console.WriteLine("  └─────────────────────────────────────────────────────────────");
                Console.WriteLine();
                Console.Write("You: ");
            }
        }

        // ── Auto-approve ToolGate authorization requests (manager-level) ─
        // This approves the ToolGate gate for approve_expense calls, so if the
        // LLM tried to approve and was blocked, a retry will succeed via
        // content-hash matching. VP-level ($1,000+) stays pending for manual !approve.
        var pendingApprovals = await approvalProvider.QueryAsync(
            new ApprovalQuery { State = ApprovalState.Pending });

        foreach (var request in pendingApprovals)
        {
            var isManagerLevel = request.Reason?.Contains("manager", StringComparison.OrdinalIgnoreCase) == true;
            if (!isManagerLevel)
                continue;

            await approvalProvider.ApproveAsync(
                request.ApprovalId,
                actor: "manager-bot",
                comment: "Auto-approved by external manager review system");
        }
    }
});

// ── 6. Interactive chat loop ─────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       ToolGate.NET — Expense Report Agent                        ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════════════╣");
Console.WriteLine("║  Demonstrates ASYNC approval workflow with content-hash matching  ║");
Console.WriteLine("║                                                                   ║");
Console.WriteLine("║  Submission is always allowed — ToolGate gates APPROVAL:          ║");
Console.WriteLine("║    < $100    AI can auto-approve (no human needed)                ║");
Console.WriteLine("║    $100-$999 manager authorization (auto-approved in ~3s)         ║");
Console.WriteLine("║    $1,000+   VP authorization (manual: !approve <id>)             ║");
Console.WriteLine("║                                                                   ║");
Console.WriteLine("║  Try this:                                                        ║");
Console.WriteLine("║    1. \"Submit a $500 hotel expense\"  -> stored as Pending         ║");
Console.WriteLine("║    2. Wait ~3s -> manager-bot auto-approves in the store          ║");
Console.WriteLine("║    3. \"Show my expenses\"             -> status is now Approved    ║");
Console.WriteLine("║                                                                   ║");
Console.WriteLine("║  Commands:                                                        ║");
Console.WriteLine("║    !pending       list pending approvals (QueryAsync API)         ║");
Console.WriteLine("║    !status <id>   show approval details (supports partial ID)     ║");
Console.WriteLine("║    !approve <id>  approve expense (EXP-xxxx) or ToolGate auth     ║");
Console.WriteLine("║    !deny <id>     deny a pending approval                         ║");
Console.WriteLine("║    quit           exit                                            ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var messages = new List<ChatMessage>
{
    new(ChatRole.System, """
        You are an expense report assistant. You help employees submit, query, and manage expense reports.

        Available tools:
        - submit_expense: Submit a new expense for reimbursement. Requires category, amount, currency, description, and receipt_url.
        - approve_expense: Approve a pending expense report. Requires expense_id and approver_notes.
        - query_expenses: Query submitted expenses with optional status/date filters.
        - generate_report: Generate an expense summary for a period (weekly, monthly, quarterly).

        Guidelines:
        - When submitting expenses, always use USD as currency unless specified otherwise.
        - Use a placeholder receipt URL like "https://receipts.example.com/receipt.jpg" if the user doesn't provide one.
        - Be helpful and proactive — suggest querying expenses or generating reports when appropriate.
        - Expense submission always succeeds. Expenses under $100 are auto-approved on submit.
          Expenses $100-$999 are stored as "Pending" and auto-approved by the manager within a few
          seconds. Expenses $1,000+ require manual approval via the !approve console command.
        - If the user asks you to approve an expense, use the approve_expense tool. For expenses
          $100+, ToolGate will block your approval attempt — this is expected. The external approval
          system handles it automatically for manager-level expenses.
        - VP-level expenses ($1,000+) require the user to manually authorize via !approve <id>,
          then ask you to retry the approval with the same arguments.
        """)
};

var chatOptions = new ChatOptions { Tools = tools };

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
        break;

    // ── !approve <id> — approve by expense ID or ToolGate approval ID ──
    if (input.StartsWith("!approve ", StringComparison.OrdinalIgnoreCase))
    {
        var id = input["!approve ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            Console.WriteLine("  Usage: !approve <expense-id or approval-id>");
            Console.WriteLine();
            continue;
        }

        // Try as expense ID first (e.g., EXP-1234)
        if (id.StartsWith("EXP-", StringComparison.OrdinalIgnoreCase))
        {
            if (ExpenseStore.TryApprove(id, out var expense))
                Console.WriteLine($"  Expense {id} approved! (${expense!.Amount:N2} {expense.Category})");
            else
                Console.WriteLine($"  Expense '{id}' not found or not in Pending status.");
            Console.WriteLine();
            continue;
        }

        // Otherwise try as ToolGate approval ID
        var result = await approvalProvider.ApproveAsync(id, actor: "console-user");
        if (result.Success)
            Console.WriteLine($"  ToolGate authorization approved! Ask the assistant to retry.");
        else
            Console.WriteLine($"  Failed: {result.FailureReason} (State={result.CurrentState})");
        Console.WriteLine();
        continue;
    }

    // ── !deny <id> — deny a pending request ─────────────────────────────
    if (input.StartsWith("!deny ", StringComparison.OrdinalIgnoreCase))
    {
        var approvalId = input["!deny ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(approvalId))
        {
            Console.WriteLine("  Usage: !deny <approval-id>");
            Console.WriteLine();
            continue;
        }

        var result = await approvalProvider.DenyAsync(approvalId, actor: "console-user");
        if (result.Success)
            Console.WriteLine($"  Denied. State={result.CurrentState}");
        else
            Console.WriteLine($"  Failed: {result.FailureReason} (State={result.CurrentState})");
        Console.WriteLine();
        continue;
    }

    // ── !pending — list pending approvals using QueryAsync ───────────────
    //
    // Demonstrates the IApprovalProvider.QueryAsync(ApprovalQuery) API.
    // In production, this would be an admin dashboard endpoint or a Slack
    // slash command like /toolgate-pending.
    if (input.Equals("!pending", StringComparison.OrdinalIgnoreCase))
    {
        var pending = await approvalProvider.QueryAsync(
            new ApprovalQuery { State = ApprovalState.Pending });

        if (pending.Count == 0)
        {
            Console.WriteLine("  No pending approvals.");
        }
        else
        {
            Console.WriteLine($"  Pending approvals ({pending.Count}):");
            foreach (var req in pending)
            {
                Console.WriteLine("  ──────────────────────────────────────────────────");
                Console.WriteLine($"  ID:      {req.ApprovalId}");
                Console.WriteLine($"  Tool:    {req.ToolName}");
                Console.WriteLine($"  Reason:  {req.Reason ?? "(none)"}");
                Console.WriteLine($"  Created: {req.CreatedAt:HH:mm:ss}  Expires: {req.ExpiresAt:HH:mm:ss}");
                if (req.ContentHash is not null)
                    Console.WriteLine($"  Hash:    {req.ContentHash[..16]}...");
            }
            Console.WriteLine("  ──────────────────────────────────────────────────");
        }
        Console.WriteLine();
        continue;
    }

    // ── !status <id> — show details for a specific approval ─────────────
    //
    // Supports partial ID matching for convenience (e.g., !status abc
    // matches approval ID starting with "abc").
    if (input.StartsWith("!status ", StringComparison.OrdinalIgnoreCase))
    {
        var approvalId = input["!status ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(approvalId))
        {
            Console.WriteLine("  Usage: !status <approval-id>");
            Console.WriteLine();
            continue;
        }

        var all = await approvalProvider.QueryAsync(new ApprovalQuery());
        var match = all.FirstOrDefault(r =>
            r.ApprovalId.StartsWith(approvalId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            Console.WriteLine($"  Approval '{approvalId}' not found.");
        }
        else
        {
            Console.WriteLine($"  Approval ID:  {match.ApprovalId}");
            Console.WriteLine($"  Tool:         {match.ToolName}");
            Console.WriteLine($"  State:        {match.State}");
            Console.WriteLine($"  Reason:       {match.Reason ?? "(none)"}");
            Console.WriteLine($"  Created:      {match.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Expires:      {match.ExpiresAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Content Hash: {match.ContentHash ?? "(none)"}");
            Console.WriteLine($"  Requested By: {match.RequestedBy ?? "(anonymous)"}");
        }
        Console.WriteLine();
        continue;
    }

    // ── Regular chat message → send to LLM ──────────────────────────────
    messages.Add(new ChatMessage(ChatRole.User, input));

    try
    {
        var response = await chatClient.GetResponseAsync(messages, chatOptions);

        // Add response messages to conversation history.
        // ToolGate uses InformationalOnly + proper tool-role messages,
        // so no sanitization is needed — messages are protocol-correct.
        foreach (var msg in response.Messages)
        {
            messages.Add(msg);
        }

        // Print the final text response from the LLM
        var textContent = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<TextContent>()
            .LastOrDefault();

        Console.WriteLine($"Assistant: {textContent?.Text ?? "(no text response)"}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Error: {ex.Message}");
    }

    Console.WriteLine();
}

// ── Cleanup ──────────────────────────────────────────────────────────────────
approverCts.Cancel();
try { await approverTask; } catch (OperationCanceledException) { }

return 0;
