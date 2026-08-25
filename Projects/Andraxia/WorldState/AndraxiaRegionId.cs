using System;

namespace Server.Andraxia;

public readonly record struct AndraxiaRegionId
{
    public AndraxiaRegionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A regional identifier is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
