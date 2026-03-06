namespace ToolGate.Core.Abstractions;

/// <summary>
/// Redacts sensitive argument values from a tool call's arguments dictionary.
/// The redacted copy is used for all output channels (logs, audit, traces).
/// </summary>
public interface IRedactionProvider
{
    /// <summary>
    /// Creates a redacted copy of the arguments dictionary, replacing sensitive
    /// values with "[REDACTED]".
    /// </summary>
    /// <param name="arguments">The original arguments. May be null or empty.</param>
    /// <returns>
    /// A new dictionary with sensitive values replaced, or null if the input is null.
    /// </returns>
    IDictionary<string, object?>? Redact(IDictionary<string, object?>? arguments);
}
