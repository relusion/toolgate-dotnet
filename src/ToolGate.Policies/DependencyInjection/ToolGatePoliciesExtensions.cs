using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ToolGate.Core.Abstractions;
using ToolGate.Policies.Configuration;

namespace ToolGate.Policies.DependencyInjection;

/// <summary>
/// Extension methods for registering ToolGate.Policies services.
/// </summary>
public static class ToolGatePoliciesExtensions
{
    /// <summary>
    /// Registers the built-in policies (Denylist, Allowlist, HighRiskApproval).
    /// </summary>
    public static IServiceCollection AddToolGatePolicies(
        this IServiceCollection services,
        Action<DenylistOptions>? configureDenylist = null,
        Action<AllowlistOptions>? configureAllowlist = null,
        Action<HighRiskOptions>? configureHighRisk = null)
    {
        services.AddOptions<DenylistOptions>();
        services.AddOptions<AllowlistOptions>();
        services.AddOptions<HighRiskOptions>();

        if (configureDenylist is not null)
            services.Configure(configureDenylist);
        if (configureAllowlist is not null)
            services.Configure(configureAllowlist);
        if (configureHighRisk is not null)
            services.Configure(configureHighRisk);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolPolicy, DenylistPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolPolicy, AllowlistPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolPolicy, HighRiskApprovalPolicy>());

        return services;
    }
}
