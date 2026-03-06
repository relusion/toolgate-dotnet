using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Configuration;

namespace ToolGate.Core.Redaction;

/// <summary>
/// Default redaction provider that masks sensitive argument values based on
/// configured key names and regex patterns.
/// </summary>
internal sealed class DefaultRedactionProvider : IRedactionProvider
{
    private const string RedactedValue = "[REDACTED]";
    private readonly HashSet<string> _sensitiveKeys;
    private readonly Regex[]? _compiledPatterns;

    public DefaultRedactionProvider(IOptions<ToolGateOptions> options)
    {
        var redaction = options.Value.Redaction;
        _sensitiveKeys = new HashSet<string>(redaction.SensitiveKeys, StringComparer.OrdinalIgnoreCase);

        if (redaction.SensitivePatterns.Count > 0)
        {
            _compiledPatterns = redaction.SensitivePatterns
                .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                .ToArray();
        }
    }

    public IDictionary<string, object?>? Redact(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return arguments;

        if (_sensitiveKeys.Count == 0 && _compiledPatterns is null)
            return arguments;

        var redacted = new Dictionary<string, object?>(arguments.Count);

        foreach (var kvp in arguments)
        {
            redacted[kvp.Key] = IsSensitive(kvp.Key) ? RedactedValue : kvp.Value;
        }

        return redacted;
    }

    private bool IsSensitive(string key)
    {
        if (_sensitiveKeys.Contains(key))
            return true;

        if (_compiledPatterns is not null)
        {
            foreach (var pattern in _compiledPatterns)
            {
                if (pattern.IsMatch(key))
                    return true;
            }
        }

        return false;
    }
}
