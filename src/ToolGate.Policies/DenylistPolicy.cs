using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Models;
using ToolGate.Policies.Configuration;

namespace ToolGate.Policies;

/// <summary>
/// Denies tool calls that match configured tool names or patterns.
/// Order: -1000 (earliest evaluation — short-circuits fast).
/// </summary>
public class DenylistPolicy : IToolPolicy
{
    private readonly DenylistOptions _options;
    private readonly HashSet<string> _deniedTools;
    private readonly Regex[]? _patterns;

    /// <summary>Initializes a new instance with the configured denylist options.</summary>
    public DenylistPolicy(IOptions<DenylistOptions> options)
    {
        _options = options.Value;
        _deniedTools = new HashSet<string>(_options.DeniedTools, StringComparer.OrdinalIgnoreCase);

        if (_options.DeniedPatterns.Count > 0)
        {
            _patterns = _options.DeniedPatterns
                .Select(p => GlobToRegex(p))
                .ToArray();
        }
    }

    /// <inheritdoc />
    public string Name => "Denylist";

    /// <inheritdoc />
    public int Order => -1000;

    /// <inheritdoc />
    public Task<PolicyOutcome> EvaluateAsync(
        ToolCallContext context,
        CancellationToken cancellationToken = default)
    {
        if (_deniedTools.Contains(context.ToolName))
        {
            return Task.FromResult(PolicyOutcome.DenyResult(
                "Denylisted",
                $"Tool '{context.ToolName}' is in the denylist"));
        }

        if (_patterns is not null)
        {
            foreach (var pattern in _patterns)
            {
                if (pattern.IsMatch(context.ToolName))
                {
                    return Task.FromResult(PolicyOutcome.DenyResult(
                        "Denylisted",
                        $"Tool '{context.ToolName}' matches denylist pattern"));
                }
            }
        }

        return Task.FromResult(PolicyOutcome.AbstainResult());
    }

    private static Regex GlobToRegex(string glob)
    {
        var pattern = "^" + Regex.Escape(glob)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }
}
