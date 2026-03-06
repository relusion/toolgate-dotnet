using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ToolGate.Core.Abstractions;
using ToolGate.Core.DependencyInjection;
using ToolGate.Core.Diagnostics;
using ToolGate.Core.Models;
using ToolGate.Policies.DependencyInjection;

namespace ToolGate.Core.Tests.Diagnostics;

public class ObservabilityTests : IDisposable
{
    private readonly MeterListener _meterListener;
    private readonly List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];

    public ObservabilityTests()
    {
        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "ToolGate")
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _measurements.Add((instrument.Name, measurement, tags.ToArray()));
        });
        _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            _measurements.Add((instrument.Name, measurement, tags.ToArray()));
        });
        _meterListener.Start();
    }

    public void Dispose()
    {
        _meterListener.Dispose();
    }

    private static ToolCallContext CreateContext(string toolName = "some_tool") => new()
    {
        ToolName = toolName,
        InvocationId = Guid.NewGuid().ToString(),
        Timestamp = DateTimeOffset.UtcNow
    };

    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        ToolGateDiagnostics.ActivitySource.Name.Should().Be("ToolGate");
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        ToolGateDiagnostics.Meter.Name.Should().Be("ToolGate");
    }

    [Fact]
    public async Task EvaluationCounter_IncrementsOnAllow()
    {
        using var sp = BuildProvider(allowAll: true);
        var gate = sp.GetRequiredService<IToolGate>();

        await gate.EvaluateAsync(CreateContext());

        _measurements.Should().Contain(m =>
            m.Name == "toolgate.evaluations.count" &&
            m.Tags.Any(t => t.Key == "decision" && (string)t.Value! == "Allow"));
    }

    [Fact]
    public async Task EvaluationCounter_IncrementsOnDeny()
    {
        using var sp = BuildProvider();
        var gate = sp.GetRequiredService<IToolGate>();

        // Default decision is Deny when no policies match
        await gate.EvaluateAsync(CreateContext());

        _measurements.Should().Contain(m =>
            m.Name == "toolgate.evaluations.count" &&
            m.Tags.Any(t => t.Key == "decision" && (string)t.Value! == "Deny"));
    }

    [Fact]
    public async Task DurationHistogram_RecordsValue()
    {
        using var sp = BuildProvider();
        var gate = sp.GetRequiredService<IToolGate>();

        await gate.EvaluateAsync(CreateContext());

        _measurements.Should().Contain(m =>
            m.Name == "toolgate.evaluations.duration" &&
            (double)m.Value >= 0);
    }

    [Fact]
    public async Task Activity_CreatedWithCorrectTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ToolGate",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        Activity? captured = null;
        listener.ActivityStopped = activity => captured = activity;

        using var sp = BuildProvider();
        var gate = sp.GetRequiredService<IToolGate>();

        await gate.EvaluateAsync(CreateContext("test_tool"));

        captured.Should().NotBeNull();
        captured!.DisplayName.Should().Be("ToolGate.Evaluate");
        captured.GetTagItem("toolgate.tool_name").Should().Be("test_tool");
        captured.GetTagItem("toolgate.decision").Should().NotBeNull();
    }

    private static ServiceProvider BuildProvider(bool allowAll = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolGateCore(o =>
        {
            if (allowAll)
                o.DefaultDecision = Decision.Allow;
        });
        services.AddToolGatePolicies();
        return services.BuildServiceProvider();
    }
}
