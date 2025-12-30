namespace Content.Shared._Afterlight.Atmos;

[ByRefEvent]
public record struct ALPressureDamageAttemptEvent(bool Cancelled = false);
