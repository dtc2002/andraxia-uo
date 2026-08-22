using System;

namespace Server.Andraxia;

public readonly record struct EventDefinitionId
{
    public EventDefinitionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An event-definition identifier is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
