using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ToolGate.Approvals.DependencyInjection;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Configuration;
using ToolGate.Core.DependencyInjection;
using ToolGate.Policies.Configuration;
using ToolGate.Policies.DependencyInjection;

namespace ToolGate.Adapters.ExtensionsAI.DependencyInjection;

/// <summary>
/// Extension methods for integrating ToolGate with Microsoft.Extensions.AI.
/// </summary>
public static class ToolGateExtensionsAIExtensions
{
    /// <summary>
    /// Inserts a <see cref="ToolGateChatClient"/> into the MEAI chat client pipeline.
    /// Requires ToolGate services to be registered via <see cref="AddToolGate"/> or individual Add methods.
    /// </summary>
    /// <param name="builder">The chat client builder.</param>
    /// <param name="configure">Optional callback to configure the <see cref="ToolGateChatClient"/> instance
    /// (e.g. set <see cref="ToolGateChatClient.LlmFeedbackEnabled"/>).</param>
    public static ChatClientBuilder UseToolGate(
        this ChatClientBuilder builder,
        Action<ToolGateChatClient>? configure = null)
    {
        return builder.Use((innerClient, sp) =>
        {
            var toolGate = sp.GetRequiredService<IToolGate>();
            var logger = sp.GetRequiredService<ILogger<ToolGateChatClient>>();
            var client = new ToolGateChatClient(innerClient, toolGate, logger);
            configure?.Invoke(client);
            return client;
        });
    }

    /// <summary>
    /// Convenience method that registers all ToolGate services (Core + Policies + Approvals)
    /// in a single call. Equivalent to calling AddToolGateCore(), AddToolGatePolicies(),
    /// and AddToolGateApprovals() individually.
    /// </summary>
    public static IServiceCollection AddToolGate(
        this IServiceCollection services,
        Action<ToolGateOptions>? configure = null,
        Action<DenylistOptions>? configureDenylist = null,
        Action<AllowlistOptions>? configureAllowlist = null,
        Action<HighRiskOptions>? configureHighRisk = null)
    {
        services.AddToolGateCore(configure);
        services.AddToolGatePolicies(configureDenylist, configureAllowlist, configureHighRisk);
        services.AddToolGateApprovals();

        return services;
    }
}
