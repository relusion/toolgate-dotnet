# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - Unreleased

### Added

- Policy pipeline with sequential evaluator and first-deny-wins aggregation
- Built-in policies: denylist, allowlist, and high-risk approval
- Custom policy support via `IToolPolicy` interface
- Approval workflow with state machine (Pending, Approved, Denied, Expired)
- In-memory approval store with first-writer-wins concurrency
- Content-based approval matching and query API
- Argument redaction for sensitive fields (passwords, keys, tokens)
- OpenTelemetry-compatible tracing (`ActivitySource`) and metrics (`Meter`)
- Structured logging with stable event IDs
- Fail-closed by default (configurable)
- Microsoft.Extensions.AI `DelegatingChatClient` adapter
- Convenience `AddToolGate()` DI registration
- Sample applications: ConsoleChat, AzureChat, ExpenseAgent
- Multi-target: `net8.0` and `net9.0`
