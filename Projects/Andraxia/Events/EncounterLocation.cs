namespace Server.Andraxia;

public sealed record EncounterLocation(
    EncounterLocationId Id,
    Map Map,
    int X,
    int Y,
    int Z,
    string DisplayName = null
)
{
    public Point3D Anchor => new(X, Y, Z);

    public string DisplayName { get; } = string.IsNullOrWhiteSpace(DisplayName) ? Id.Value : DisplayName;
}
