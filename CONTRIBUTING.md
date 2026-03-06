# Contributing to ToolGate.NET

## Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (for full multi-target builds)

### Building

```shell
dotnet restore
dotnet build
```

### Running Tests

```shell
dotnet test
```

### Running Benchmarks

```shell
dotnet run --project benchmarks/ToolGate.Benchmarks -c Release
```

## How to Contribute

### Reporting Bugs

- Use the [bug report template](https://github.com/relusion/toolgate-dotnet/issues/new?template=bug_report.yml) to file issues
- Include steps to reproduce, expected behavior, and actual behavior
- Include your .NET SDK version (`dotnet --info`)

### Suggesting Features

- Use the [feature request template](https://github.com/relusion/toolgate-dotnet/issues/new?template=feature_request.yml)
- Describe the use case and why the feature would be valuable

### Submitting Pull Requests

1. Fork the repository
2. Create a feature branch from `main`
3. Make your changes
4. Ensure all tests pass and code builds without warnings
5. Submit a pull request using the PR template

### Code Guidelines

- Follow the existing code style (enforced by `.editorconfig`)
- All warnings are treated as errors (`TreatWarningsAsErrors`)
- Add XML documentation for public APIs
- Add tests for new functionality
- Target 80% code coverage minimum

### Commit Messages

Use clear, descriptive commit messages:

```
feat: Add rate limiting policy
fix: Handle null arguments in denylist evaluation
docs: Update quick start example
test: Add integration tests for approval workflow
```

## Project Structure

```
src/
  ToolGate.Core/              # Pipeline, models, abstractions
  ToolGate.Policies/          # Built-in policies (denylist, allowlist, high-risk)
  ToolGate.Approvals/         # Approval store and state machine
  ToolGate.Adapters.ExtensionsAI/  # Microsoft.Extensions.AI adapter
tests/
  ToolGate.Core.Tests/
  ToolGate.Policies.Tests/
  ToolGate.Approvals.Tests/
  ToolGate.Adapters.ExtensionsAI.Tests/
samples/
  ToolGate.Samples.ConsoleChat/
  ToolGate.Samples.AzureChat/
  ToolGate.Samples.ExpenseAgent/
benchmarks/
  ToolGate.Benchmarks/
```

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
