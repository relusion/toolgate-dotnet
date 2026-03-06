namespace ToolGate.Core.Configuration;

/// <summary>
/// Configuration for argument redaction in logs, audit events, and traces.
/// </summary>
public class RedactionOptions
{
    /// <summary>
    /// Argument key names that are considered sensitive and will be replaced with "[REDACTED]".
    /// Matching is case-insensitive. Custom keys are added to (not replacing) the defaults.
    /// </summary>
    /// <remarks>
    /// Default sensitive keys: password, secret, token, apikey, api_key,
    /// authorization, credential, connectionstring, connection_string.
    /// </remarks>
    public HashSet<string> SensitiveKeys { get; set; } =
    [
        "password",
        "secret",
        "token",
        "apikey",
        "api_key",
        "authorization",
        "credential",
        "connectionstring",
        "connection_string"
    ];

    /// <summary>
    /// Regex patterns for matching sensitive argument keys.
    /// Patterns are compiled once and cached for performance.
    /// </summary>
    public List<string> SensitivePatterns { get; set; } = [];
}
