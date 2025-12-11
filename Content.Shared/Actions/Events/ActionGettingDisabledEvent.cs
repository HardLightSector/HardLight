namespace Content.Shared.Actions.Events;

/// <summary>
///     Raised on the action entity when it is about to be disabled due to having no charges remaining and DisableWhenEmpty being true.
/// </summary>
/// <param name="Performer">The entity that performed this action.</param>
[ByRefEvent]
public readonly record struct ActionGettingDisabledEvent(EntityUid Performer);
