using System;
using System.Linq;
using Server;
using Server.Andraxia;
using Xunit;

namespace Andraxia.Tests;

[Collection("Sequential Andraxia Tests")]
public sealed class EncounterLocationTests
{
    private static readonly EventInstanceId InstanceId = new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
    );

    [Fact]
    public void BritainCatalogHasUniqueStableIdsAndKnownCoordinates()
    {
        var locations = KnownEncounterLocations.BritainDisturbance;

        Assert.Equal(6, locations.Count);
        Assert.Equal(locations.Count, locations.Select(static location => location.Id).Distinct().Count());
        Assert.All(
            locations,
            location =>
            {
                Assert.StartsWith("location.britain.", location.Id.Value, StringComparison.Ordinal);
                Assert.Same(Map.Trammel, location.Map);
                Assert.True(KnownEncounterLocations.TryGet(location.Id, out var resolved));
                Assert.Same(location, resolved);
            }
        );
    }

    [Theory]
    [InlineData("location.britain.crossroads-west", 1378, 1624, 0)]
    [InlineData("location.britain.farmland-northwest", 1232, 1604, 0)]
    [InlineData("location.britain.farmland-southwest", 1199, 1823, 0)]
    [InlineData("location.britain.graveyard-east", 1402, 1510, 10)]
    [InlineData("location.britain.road-north", 1470, 1478, 0)]
    [InlineData("location.britain.road-south", 1430, 1800, 0)]
    public void BritainCatalogContainsExpectedCandidate(string id, int x, int y, int z)
    {
        Assert.True(KnownEncounterLocations.TryGet(new EncounterLocationId(id), out var location));
        Assert.Same(Map.Trammel, location.Map);
        Assert.Equal(new Point3D(x, y, z), location.Anchor);
    }

    [Fact]
    public void SameInputProducesSameApprovedLocation()
    {
        var selector = new DeterministicEncounterLocationSelector();

        var first = selector.Select(KnownEvents.BritainDisturbance, InstanceId, KnownEncounterLocations.BritainDisturbance);
        var second = selector.Select(KnownEvents.BritainDisturbance, InstanceId, KnownEncounterLocations.BritainDisturbance);

        Assert.Same(first, second);
        Assert.Contains(first, KnownEncounterLocations.BritainDisturbance);
    }

    [Fact]
    public void CandidateEnumerationOrderDoesNotAffectSelection()
    {
        var selector = new DeterministicEncounterLocationSelector();
        var candidates = KnownEncounterLocations.BritainDisturbance;

        var forward = selector.Select(KnownEvents.BritainDisturbance, InstanceId, candidates);
        var reversed = selector.Select(KnownEvents.BritainDisturbance, InstanceId, candidates.Reverse().ToArray());

        Assert.Equal(forward.Id, reversed.Id);
    }
}
