using System;

namespace Server.Andraxia;

public readonly record struct EventInstanceId
{
    public EventInstanceId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An event-instance identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static EventInstanceId New() => new(Guid.NewGuid());

    public static bool TryParse(string value, out EventInstanceId id)
    {
        if (Guid.TryParseExact(value, "N", out var guid) && guid != Guid.Empty)
        {
            id = new EventInstanceId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString("N");
}
