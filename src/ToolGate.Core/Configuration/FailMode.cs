namespace ToolGate.Core.Configuration;

/// <summary>
/// Controls how the policy pipeline handles internal errors (e.g., policy exceptions).
/// </summary>
public enum FailMode
{
    /// <summary>
    /// Errors cause tool calls to be denied. This is the secure default —
    /// no tool call slips through due to an internal bug.
    /// </summary>
    FailClosed,

    /// <summary>
    /// Errors cause the failing policy to be skipped. Use only in development or
    /// when explicitly accepting the risk of a faulty policy allowing tool calls.
    /// </summary>
    FailOpen
}
