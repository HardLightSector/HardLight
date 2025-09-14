using Content.Shared.Actions;
using Content.Shared.Mobs.Components;

namespace Content.Shared.Mobs.Systems;

/// <summary>
///     Adds and removes defined actions when a mob's <see cref="MobState"/> changes.
/// </summary>
public sealed class MobStateActionsSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MobStateActionsComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MobStateComponent, ComponentInit>(OnMobStateComponentInit);
    }

    private void OnMobStateChanged(EntityUid uid, MobStateActionsComponent component, MobStateChangedEvent args)
    {
        if (TerminatingOrDeleted(uid))
            return;
        ComposeActions(uid, component, args.NewMobState);
    }

    private void OnMobStateComponentInit(EntityUid uid, MobStateComponent component, ComponentInit args)
    {
        if (!TryComp<MobStateActionsComponent>(uid, out var mobStateActionsComp))
            return;

        if (TerminatingOrDeleted(uid))
            return;
        ComposeActions(uid, mobStateActionsComp, component.CurrentState);
    }

    /// <summary>
    /// Adds or removes actions from a mob based on mobstate.
    /// </summary>
    private void ComposeActions(EntityUid uid, MobStateActionsComponent component, MobState newMobState)
    {
        // Don't modify actions on entities that are terminating or deleted.
        if (TerminatingOrDeleted(uid))
            return;

        if (!TryComp<ActionsComponent>(uid, out var action))
            return;

        foreach (var act in component.GrantedActions)
        {
            Del(act);
        }
        component.GrantedActions.Clear();

        if (!component.Actions.TryGetValue(newMobState, out var toGrant) || toGrant.Count == 0)
            return;

        foreach (var id in toGrant)
        {
            EntityUid? act = null;
            // Skip granting if entity is terminating between loop iterations.
            if (TerminatingOrDeleted(uid))
                break;

            if (_actions.AddAction(uid, ref act, id, uid, action))
                component.GrantedActions.Add(act.Value);
        }
    }
}
