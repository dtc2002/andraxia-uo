using System;
using System.Reflection;
using Server;
using Server.Andraxia;
using Server.Mobiles;
using Server.Regions;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class AndraxiaGuardTests
{
    [Fact]
    public void GuardedRegionsUseAndraxiaTownGuard()
    {
        var region = new GuardedRegion("Andraxia guard test", Map.Internal, 0, new Rectangle3D(0, 0, 0, 1, 1, 1));

        AndraxiaGuardSystem.Install([region]);

        Assert.Equal(typeof(AndraxiaTownGuard), region.GuardType);
    }

    [Fact]
    public void TownGuardUsesOrdinaryCreatureCombatWithoutStockExecutionTimer()
    {
        Assert.True(typeof(BaseCreature).IsAssignableFrom(typeof(AndraxiaTownGuard)));
        Assert.False(typeof(BaseGuard).IsAssignableFrom(typeof(AndraxiaTownGuard)));
        Assert.Null(typeof(AndraxiaTownGuard).GetMethod("Kill", BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Null(typeof(AndraxiaTownGuard).GetMethod("Damage", BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Contains(typeof(AndraxiaTownGuard).GetConstructors(), constructor =>
            constructor.GetParameters() is [{ ParameterType: var type }] && type == typeof(Mobile));
    }

    [Fact]
    public void CaravanGuardOptsOutOfTownWitnessBehaviorAndRetainsHostileTargeting()
    {
        Assert.Equal(typeof(AndraxiaCaravanGuard),
            typeof(AndraxiaCaravanGuard).GetMethod(nameof(BaseCreature.IsHumanInTown))?.DeclaringType);
        Assert.True(typeof(AndraxiaCaravanGuard).GetMethod(nameof(Mobile.OnMovement))?.DeclaringType ==
            typeof(AndraxiaCaravanGuard));
        Assert.True(typeof(AndraxiaCaravanGuard).GetMethod(nameof(BaseCreature.IsEnemy))?.DeclaringType ==
            typeof(AndraxiaCaravanGuard));
    }

    [Fact]
    public void GuardInstallationPreservesStockCriminalEligibilityLogic()
    {
        var region = new GuardedRegion("Andraxia crime test", Map.Internal, 0, new Rectangle3D(0, 0, 0, 1, 1, 1));

        AndraxiaGuardSystem.Install([region]);

        Assert.False(region.GuardsDisabled);
        Assert.Equal(typeof(GuardedRegion), region.GetType());
        Assert.Equal(typeof(GuardedRegion), region.GetType().GetMethod(nameof(GuardedRegion.IsGuardCandidate))?.DeclaringType);
    }
}
