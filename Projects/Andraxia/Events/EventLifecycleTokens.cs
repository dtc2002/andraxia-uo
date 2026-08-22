using System;

namespace Server.Andraxia;

internal static class EventLifecycleTokens
{
    public static string GetToken(EventLifecycleState state) =>
        state switch
        {
            EventLifecycleState.Active    => "active",
            EventLifecycleState.Succeeded => "succeeded",
            EventLifecycleState.Failed    => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown event lifecycle state.")
        };

    public static bool TryParse(string token, out EventLifecycleState state)
    {
        state = token switch
        {
            "active"    => EventLifecycleState.Active,
            "succeeded" => EventLifecycleState.Succeeded,
            "failed"    => EventLifecycleState.Failed,
            _           => default
        };

        return token is "active" or "succeeded" or "failed";
    }
}
