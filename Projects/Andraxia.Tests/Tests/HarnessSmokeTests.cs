using System;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public class HarnessSmokeTests
{
    [Fact]
    public void BootstrapLoadsAndraxiaAsSeparateExtensionAssembly()
    {
        var assembly = typeof(AndraxiaAssembly).Assembly;

        Assert.Equal("Andraxia", assembly.GetName().Name);
        Assert.Contains(AssemblyHandler.Assemblies, loaded => loaded == assembly);
        Assert.DoesNotContain(AssemblyHandler.Assemblies, loaded => loaded == typeof(Core).Assembly);
    }

    [Fact]
    public void SimulationClockAdvancesCoreTimeAndProcessesTimers()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        using var clock = new SimulationClock(start);
        var fired = false;

        Timer.StartTimer(TimeSpan.FromMilliseconds(16), () => fired = true, out var timer);

        try
        {
            clock.Advance(TimeSpan.FromMilliseconds(8));

            Assert.Equal(start.AddMilliseconds(8), clock.Now);
            Assert.False(fired);

            clock.Advance(TimeSpan.FromMilliseconds(8));

            Assert.Equal(start.AddMilliseconds(16), clock.Now);
            Assert.True(fired);
            Assert.False(timer.Running);
        }
        finally
        {
            timer.Cancel();
        }
    }

    [Fact]
    public void BootstrapDoesNotConfigureWorldOrNetworkListeners()
    {
        Assert.Equal(WorldState.Initial, World.WorldState);
        Assert.Empty(ServerConfiguration.Listeners);
        Assert.Empty(ServerConfiguration.DataDirectories);
    }
}
