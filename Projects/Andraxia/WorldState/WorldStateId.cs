using System;

namespace Server.Andraxia;

public readonly record struct WorldStateId
{
    public WorldStateId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A world-state identifier is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
