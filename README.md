# ToolGate.NET

[![CI](https://github.com/relusion/toolgate-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/relusion/toolgate-dotnet/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ToolGate.Core.svg)](https://www.nuget.org/packages/ToolGate.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Policy and approvals middleware for AI tool calls in .NET applications.

ToolGate evaluates every LLM tool call against configurable policies — **Allow**, **Deny**, or **RequireApproval** — before the tool executes. It integrates with [Microsoft.Extensions.AI](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai-preview/) as a `DelegatingChatClient` decorator.

## Features

- **Policy Pipeline** — Sequential evaluator with first-deny-wins aggregation
- **Built-in Policies** — Denylist, allowlist, and high-risk approval policies
- **Approval Workflow** — Human-in-the-loop approval with state machine (Pending → Approved/Denied/Expired)
- **Custom Policies** — Implement `IToolPolicy` to add your own evaluation logic
- **Argument Redaction** — Sensitive fields (passwords, keys, tokens) are automatically redacted in logs and audit
- **Observability** — OpenTelemetry-compatible tracing (`ActivitySource`) and metrics (`Meter`)
- **Fail-Closed by Default** — Policy errors block tool execution (configurable)

## Packages

| Package | Purpose |
|---|---|
| `ToolGate.Core` | Pipeline, models, abstractions, diagnostics |
| `ToolGate.Policies` | Built-in denylist, allowlist, and high-risk policies |
| `ToolGate.Approvals` | In-memory approval store and state machine |
| `ToolGate.Adapters.ExtensionsAI` | MEAI `DelegatingChatClient` adapter + convenience registration |

## Quick Start

### 1. Install packages

```shell
dotnet add package ToolGate.Adapters.ExtensionsAI
```

The adapter package transitively includes Core, Policies, and Approvals.

### 2. Register services

```csharp
using ToolGate.Adapters.ExtensionsAI.DependencyInjection;

services.AddToolGate(
    configureDenylist: deny =>
    {
        deny.DeniedTools.Add("delete_database");
        deny.DeniedPatterns.Add("rm_*");
    },
    configureAllowlist: allow =>
    {
        allow.AllowedTools.Add("get_weather");
        allow.AllowedTools.Add("get_time");
    });

// Wrap your chat client with ToolGate
services.AddChatClient(innerClient)
    .UseToolGate();
```

### 3. Tool calls are now evaluated

When the LLM returns a tool call, ToolGate intercepts it:
- **Allowed** tools pass through unchanged
- **Denied** tools are replaced with a `FunctionResultContent` containing the denial reason
- **RequireApproval** tools are held until a human approves or denies via `IApprovalProvider`

## Configuration

### ToolGateOptions

```csharp
services.AddToolGateCore(options =>
{
    options.FailMode = FailMode.FailClosed;        // Default: errors block execution
    options.DefaultDecision = Decision.Deny;        // Default: unknown tools are denied
    options.DefaultApprovalTtl = TimeSpan.FromMinutes(30);
    options.Redaction.SensitiveKeys.Add("my_secret_field");
});
```

### Denylist Policy

Blocks tools by exact name or glob pattern. Evaluated first (Order: -1000).

```csharp
services.AddToolGatePolicies(configureDenylist: deny =>
{
    deny.DeniedTools.Add("delete_database");
    deny.DeniedTools.Add("drop_table");
    deny.DeniedPatterns.Add("rm_*");        // Glob pattern
    deny.DeniedPatterns.Add("*.destructive");
});
```

### Allowlist Policy

Explicitly allows tools. Optional `DenyUnlisted` mode blocks everything not in the list. Evaluated second (Order: -500).

```csharp
services.AddToolGatePolicies(configureAllowlist: allow =>
{
    allow.AllowedTools.Add("get_weather");
    allow.AllowedTools.Add("search_docs");
    allow.DenyUnlisted = true;   // Deny everything not explicitly allowed
});
```

### High-Risk Approval Policy

Requires human approval for tools with high risk metadata. Evaluated late (Order: 500).

```csharp
services.AddToolGatePolicies(configureHighRisk: risk =>
{
    risk.MinimumRiskLevel = RiskLevel.High;  // Default
});
```

Triggered when `ToolCallContext.Metadata.RiskLevel >= MinimumRiskLevel` or when metadata contains the `"high-risk"` tag.

## Custom Policies

Implement `IToolPolicy` and register it in DI:

```csharp
public class RateLimitPolicy : IToolPolicy
{
    public string Name => "RateLimit";
    public int Order => 100;   // After allowlist, before high-risk

    public Task<PolicyOutcome> EvaluateAsync(
        ToolCallContext context,
        CancellationToken cancellationToken = default)
    {
        if (IsOverLimit(context.ToolName))
            return Task.FromResult(PolicyOutcome.DenyResult(
                "RateLimited", "Too many calls to this tool"));

        return Task.FromResult(PolicyOutcome.AbstainResult());
    }
}

// Register:
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IToolPolicy, RateLimitPolicy>());
```

**Policy outcomes:** `AllowResult()`, `DenyResult()`, `RequireApprovalResult()`, `AbstainResult()`

**Policy ordering:** Lower `Order` values evaluate first. Built-in: Denylist (-1000), Allowlist (-500), HighRisk (500). Custom policies default to 0.

## Approval Workflow

When a policy returns `RequireApproval`, the pipeline creates an approval request:

```csharp
var approvalProvider = sp.GetRequiredService<IApprovalProvider>();

// Approve programmatically (e.g., from an admin UI or webhook)
var result = await approvalProvider.ApproveAsync(approvalId, actor: "admin@example.com");

// On next evaluation of the same invocation, ToolGate recognizes the approval
// and allows the tool call to proceed (resume-after-approval)
```

The in-memory approval store uses first-writer-wins concurrency and lazy TTL expiration.

## Observability

ToolGate emits OpenTelemetry-compatible diagnostics:

- **Tracing**: `ActivitySource` named `"ToolGate"` with evaluation spans
- **Metrics**: `Meter` named `"ToolGate"` with:
  - `toolgate.evaluations.count` — Counter by decision
  - `toolgate.evaluations.duration` — Histogram in ms
  - `toolgate.errors.count` — Counter by error type
  - `toolgate.approvals.count` — Counter by state transition
- **Logging**: Structured log events with stable IDs (see `ToolGateEventIds`)

## Samples

| Sample | Description |
|---|---|
| [ConsoleChat](samples/ToolGate.Samples.ConsoleChat/) | Minimal console app demonstrating all decision types (Allow, Deny, RequireApproval) |
| [AzureChat](samples/ToolGate.Samples.AzureChat/) | Azure OpenAI integration with tool policies |
| [ExpenseAgent](samples/ToolGate.Samples.ExpenseAgent/) | Expense report agent with custom policies (amount thresholds, read-only enforcement) |

```shell
dotnet run --project samples/ToolGate.Samples.ConsoleChat
```

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on building, testing, and submitting pull requests.

## License

[MIT](LICENSE)
