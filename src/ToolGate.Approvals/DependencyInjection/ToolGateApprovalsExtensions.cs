using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ToolGate.Approvals.Abstractions;
using ToolGate.Core.Abstractions;

namespace ToolGate.Approvals.DependencyInjection;

/// <summary>
/// Extension methods for registering ToolGate.Approvals services.
/// </summary>
public static class ToolGateApprovalsExtensions
{
    /// <summary>
    /// Registers the in-memory approval store, default approval provider,
    /// and the <see cref="IApprovalHandler"/> bridge for pipeline integration.
    /// </summary>
    public static IServiceCollection AddToolGateApprovals(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IApprovalStore, InMemoryApprovalStore>();
        services.TryAddSingleton<IApprovalProvider, DefaultApprovalProvider>();
        services.TryAddSingleton<IApprovalHandler, ApprovalHandler>();

        return services;
    }
}
