using System.ComponentModel;

namespace ToolGate.Samples.AzureChat.Tools;

public static class EmailTool
{
    [Description("Sends an email to the specified recipient")]
    public static string SendEmail(
        [Description("Recipient email address")] string to,
        [Description("Email subject")] string subject,
        [Description("Email body")] string body)
    {
        // This tool is gated by ToolGate's approval policy.
        // If it executes, the approval was granted.
        return $"Email sent to {to} with subject '{subject}'.";
    }
}
