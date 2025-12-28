using Content.Shared.Chemistry.Components;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Nutrition;

/// <summary>
///     Raised directed at the consumer when attempting to ingest something.
/// </summary>
public sealed class IngestionAttemptEvent : CancellableEntityEventArgs
{
    /// <summary>
    ///     The equipment that is blocking consumption. Should only be non-null if the event was canceled.
    /// </summary>
    public EntityUid? Blocker = null;
}

/// <summary>
/// Raised directed at the food after finishing eating a food before it's deleted.
/// Cancel this if you want to do something special before a food is deleted.
/// </summary>
public sealed class BeforeFullyEatenEvent : CancellableEntityEventArgs
{
    /// <summary>
    /// The person that ate the food.
    /// </summary>
    public EntityUid User;
}

/// <summary>
/// Raised directed at the food after finishing eating it and before it's deleted.
/// </summary>
[ByRefEvent]
public readonly record struct AfterFullyEatenEvent(EntityUid User)
{
    /// <summary>
    /// The entity that ate the food.
    /// </summary>
    public readonly EntityUid User = User;
}

/// <summary>
/// Raised directed at the food being sliced before it's deleted.
/// Cancel this if you want to do something special before a food is deleted.
/// </summary>
public sealed class BeforeFullySlicedEvent : CancellableEntityEventArgs
{
    /// <summary>
    /// The person slicing the food.
    /// </summary>
    public EntityUid User;
}

[ByRefEvent]
public record struct IngestingEvent(EntityUid Food, Solution Split, bool ForceFed);

/// <summary>
/// Raised on an entity when it is being made to be eaten.
/// </summary>
/// <param name="User">Who is doing the action?</param>
/// <param name="Target">Who is doing the eating?</param>
/// <param name="Split">The solution we're currently eating.</param>
/// <param name="ForceFed">Whether we're being fed by someone else, checkec enough I might as well pass it.</param>
[ByRefEvent]
public record struct IngestedEvent(EntityUid User, EntityUid Target, Solution Split, bool ForceFed)
{
    // Should we refill the solution now that we've eaten it?
    // This bool basically only exists because of stackable system.
    public bool Refresh;

    // Should we destroy the ingested entity?
    public bool Destroy;

    // Has this eaten event been handled? Used to prevent duplicate flavor popups and sound effects.
    public bool Handled;

    // Should we try eating again?
    public bool Repeat;
}
