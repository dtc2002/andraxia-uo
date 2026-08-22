namespace Server.Andraxia;

public sealed record EncounterLocation(
    EncounterLocationId Id,
    Map Map,
    int X,
    int Y,
    int Z
)
{
    public Point3D Anchor => new(X, Y, Z);
}
