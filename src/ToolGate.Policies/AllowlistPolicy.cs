using Microsoft.Extensions.Options;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Models;
using ToolGate.Policies.Configuration;

namespace ToolGate.Policies;

/// <summary>
/// Allows tool calls that match the configured allowlist.
/// Order: -500 (early evaluation, after denylist).
/// </summary>
public class AllowlistPolicy : IToolPolicy
{
    private readonly AllowlistOptions _options;
    private readonly HashSet<string> _allowedTools;

    /// <summary>Initializes a new instance with the configured allowlist options.</summary>
    public AllowlistPolicy(IOptions<AllowlistOptions> options)
    {
        _options = options.Value;
        _allowedTools = new HashSet<string>(_options.AllowedTools, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string Name => "Allowlist";

    /// <inheritdoc />
    public int Order => -500;

    /// <inheritdoc />
    public Task<PolicyOutcome> EvaluateAsync(
        ToolCallContext context,
        CancellationToken cancellationToken = default)
    {
        if (_allowedTools.Contains(context.ToolName))
        {
            return Task.FromResult(PolicyOutcome.AllowResult(
                $"Tool '{context.ToolName}' is allowlisted"));
        }

        if (_options.DenyUnlisted)
        {
            return Task.FromResult(PolicyOutcome.DenyResult(
                "NotAllowlisted",
                $"Tool '{context.ToolName}' is not in the allowlist"));
        }

        return Task.FromResult(PolicyOutcome.AbstainResult());
    }
}
