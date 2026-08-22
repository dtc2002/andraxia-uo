using System;

namespace Server.Andraxia;

public readonly record struct EventTargetId
{
    public EventTargetId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An event-target identifier is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
