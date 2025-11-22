namespace Content.Shared._Afterlight.Vore;

public readonly record struct VorePrompt(
    List<EntityUid> Waiting,
    EntityUid Predator,
    EntityUid Prey,
    EntityUid User
);
