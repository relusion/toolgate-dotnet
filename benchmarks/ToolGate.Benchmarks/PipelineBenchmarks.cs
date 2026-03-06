using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ToolGate.Approvals.DependencyInjection;
using ToolGate.Core.Abstractions;
using ToolGate.Core.DependencyInjection;
using ToolGate.Core.Models;
using ToolGate.Policies.DependencyInjection;

namespace ToolGate.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PipelineBenchmarks
{
    private IToolGate _pipeline1 = null!;
    private IToolGate _pipeline5 = null!;
    private IToolGate _pipeline10 = null!;
    private ToolCallContext _allowedContext = null!;
    private ToolCallContext _deniedContext = null!;

    [GlobalSetup]
    public void Setup()
    {
        _allowedContext = new ToolCallContext
        {
            ToolName = "get_weather",
            InvocationId = "bench-001",
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = new Dictionary<string, object?> { ["city"] = "Seattle" }
        };

        _deniedContext = new ToolCallContext
        {
            ToolName = "delete_database",
            InvocationId = "bench-002",
            Timestamp = DateTimeOffset.UtcNow,
            Arguments = new Dictionary<string, object?> { ["db"] = "prod" }
        };

        _pipeline1 = BuildPipeline(1);
        _pipeline5 = BuildPipeline(5);
        _pipeline10 = BuildPipeline(10);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Allow")]
    public Task<ToolDecision> Allow_1Policy() =>
        _pipeline1.EvaluateAsync(_allowedContext);

    [Benchmark]
    [BenchmarkCategory("Allow")]
    public Task<ToolDecision> Allow_5Policies() =>
        _pipeline5.EvaluateAsync(_allowedContext);

    [Benchmark]
    [BenchmarkCategory("Allow")]
    public Task<ToolDecision> Allow_10Policies() =>
        _pipeline10.EvaluateAsync(_allowedContext);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deny")]
    public Task<ToolDecision> Deny_1Policy() =>
        _pipeline1.EvaluateAsync(_deniedContext);

    [Benchmark]
    [BenchmarkCategory("Deny")]
    public Task<ToolDecision> Deny_5Policies() =>
        _pipeline5.EvaluateAsync(_deniedContext);

    [Benchmark]
    [BenchmarkCategory("Deny")]
    public Task<ToolDecision> Deny_10Policies() =>
        _pipeline10.EvaluateAsync(_deniedContext);

    private static IToolGate BuildPipeline(int policyCount)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false)); // Suppress all logging

        services.AddToolGateCore();
        services.AddToolGatePolicies(
            configureDenylist: deny =>
            {
                deny.DeniedTools.Add("delete_database");
            },
            configureAllowlist: allow =>
            {
                allow.AllowedTools.Add("get_weather");
            });
        services.AddToolGateApprovals();

        // Add extra no-op policies to reach the target count.
        // Built-in count: Denylist + Allowlist + HighRisk = 3
        for (var i = 3; i < policyCount; i++)
        {
            var index = i;
            services.AddSingleton<IToolPolicy>(new NoOpPolicy($"NoOp_{index}", index));
        }

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IToolGate>();
    }

    private sealed class NoOpPolicy(string name, int order) : IToolPolicy
    {
        public string Name => name;
        public int Order => order;

        public Task<PolicyOutcome> EvaluateAsync(
            ToolCallContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PolicyOutcome.AbstainResult());
    }
}
