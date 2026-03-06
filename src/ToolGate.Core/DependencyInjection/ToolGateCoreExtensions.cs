using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ToolGate.Core.Abstractions;
using ToolGate.Core.Audit;
using ToolGate.Core.Configuration;
using ToolGate.Core.Pipeline;
using ToolGate.Core.Redaction;

namespace ToolGate.Core.DependencyInjection;

/// <summary>
/// Extension methods for registering ToolGate.Core services in the DI container.
/// </summary>
public static class ToolGateCoreExtensions
{
    /// <summary>
    /// Registers ToolGate core services: policy pipeline, redaction provider, and audit sink.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration for <see cref="ToolGateOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddToolGateCore(
        this IServiceCollection services,
        Action<ToolGateOptions>? configure = null)
    {
        services.AddOptions<ToolGateOptions>();
        if (configure is not null)
            services.Configure(configure);

        services.TryAddSingleton<IToolGate, PolicyPipeline>();
        services.TryAddSingleton<IRedactionProvider, DefaultRedactionProvider>();
        services.TryAddSingleton<IAuditSink, LoggerAuditSink>();

        return services;
    }
}
