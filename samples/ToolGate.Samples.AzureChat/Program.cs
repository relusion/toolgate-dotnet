using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ToolGate.Adapters.ExtensionsAI.DependencyInjection;
using ToolGate.Approvals.Abstractions;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Models;
using ToolGate.Samples.AzureChat.Tools;

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
    Console.Error.WriteLine("  cd samples/ToolGate.Samples.AzureChat");
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

// Register ToolGate with built-in policies (same as ConsoleChat sample)
services.AddToolGate(
    configureDenylist: deny =>
    {
        deny.DeniedTools.Add("delete_database");
        deny.DeniedTools.Add("drop_table");
        deny.DeniedPatterns.Add("rm_*");
    },
    configureAllowlist: allow =>
    {
        allow.AllowedTools.Add("get_weather");
        allow.AllowedTools.Add("get_time");
    });

// Custom policy: require approval for send_email
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IToolPolicy, SendEmailApprovalPolicy>());

// Build the MEAI pipeline.
// ChatClientBuilder applies Use() in reverse: last added = innermost.
// We need: FunctionInvoking (outer) → ToolGate (inner) → AzureOpenAI (base)
// So ToolGate intercepts tool calls BEFORE FunctionInvokingChatClient executes them.
services.AddChatClient(innerClient)
    .UseFunctionInvocation()  // outermost — executes allowed tool calls
    .UseToolGate();           // innermost — blocks denied calls before execution

var sp = services.BuildServiceProvider();
var chatClient = sp.GetRequiredService<IChatClient>();
var approvalProvider = sp.GetRequiredService<IApprovalProvider>();

// ── 4. Define tools ──────────────────────────────────────────────────────────
var tools = new List<AITool>
{
    AIFunctionFactory.Create(WeatherTool.GetWeather, name: "get_weather"),
    AIFunctionFactory.Create(EmailTool.SendEmail, name: "send_email"),
    AIFunctionFactory.Create(DatabaseTool.DeleteDatabase, name: "delete_database"),
};

// ── 5. Interactive chat loop ─────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║     ToolGate.NET v0.1 — Azure OpenAI Sample                ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine("║  Try:                                                       ║");
Console.WriteLine("║    • \"What's the weather in Seattle?\"  → allowed            ║");
Console.WriteLine("║    • \"Send an email to alice\"          → requires approval  ║");
Console.WriteLine("║    • \"Delete the production database\"  → denied             ║");
Console.WriteLine("║                                                             ║");
Console.WriteLine("║  Commands: !approve <id>  |  quit                           ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var messages = new List<ChatMessage>
{
    new(ChatRole.System, """
        You are a helpful assistant with access to these tools:
        - get_weather: Check weather for a city
        - send_email: Send emails to people
        - delete_database: Delete a database

        When the user asks about weather, use get_weather.
        When asked to send emails, use send_email.
        When asked to delete databases, use delete_database.
        Always try to use the appropriate tool when the user's request matches.
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

    // Handle !approve command
    if (input.StartsWith("!approve ", StringComparison.OrdinalIgnoreCase))
    {
        var approvalId = input["!approve ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(approvalId))
        {
            Console.WriteLine("  Usage: !approve <approval-id>");
            Console.WriteLine();
            continue;
        }

        var result = await approvalProvider.ApproveAsync(approvalId, actor: "console-user");
        Console.WriteLine($"  Approval result: Success={result.Success}, State={result.CurrentState}");
        Console.WriteLine();
        continue;
    }

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

return 0;

// ═══════════════════════════════════════════════════════════════════════════════
// Custom policy: requires approval for send_email tool.
// ═══════════════════════════════════════════════════════════════════════════════
sealed class SendEmailApprovalPolicy : IToolPolicy
{
    public string Name => "SendEmailApproval";
    public int Order => 100;

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
