using System;

namespace Server.Andraxia;

public readonly record struct EncounterLocationId
{
    public EncounterLocationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An encounter-location identifier is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
