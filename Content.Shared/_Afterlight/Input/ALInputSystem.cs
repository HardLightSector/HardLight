using Content.Shared._Afterlight.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Shared._Afterlight.Input;

// Taken from https://github.com/RMC-14/RMC-14
public sealed class ALInputSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;

    private bool _activeInputMoverEnabled;

    private EntityQuery<ActiveNPCComponent> _activeNpcQuery;
    private EntityQuery<ActorComponent> _actorQuery;

    public override void Initialize()
    {
        _activeNpcQuery = GetEntityQuery<ActiveNPCComponent>();
        _actorQuery = GetEntityQuery<ActorComponent>();

        SubscribeLocalEvent<ActiveInputMoverComponent, MapInitEvent>(OnActiveChanged);
        SubscribeLocalEvent<ActiveInputMoverComponent, PlayerAttachedEvent>(OnActiveChanged);
        SubscribeLocalEvent<ActiveInputMoverComponent, PlayerDetachedEvent>(OnActiveChanged);

        SubscribeLocalEvent<ActiveNPCComponent, MapInitEvent>(OnActiveChanged);
        SubscribeLocalEvent<ActiveNPCComponent, ComponentRemove>(OnActiveChanged);

        Subs.CVar(_config, ALCVars.ALActiveInputMoverEnabled, v => _activeInputMoverEnabled = v, true);
    }


    private void OnActiveChanged<TComp, TEvent>(Entity<TComp> ent, ref TEvent args) where TComp : IComponent?
    {
        if (!_activeInputMoverEnabled)
            return;

        if (ShouldBeActive(ent))
            EnsureComp<InputMoverComponent>(ent);
        else
            RemCompDeferred<InputMoverComponent>(ent);
    }

    private bool ShouldBeActive(EntityUid ent)
    {
        return _actorQuery.HasComp(ent) || _activeNpcQuery.HasComp(ent);
    }
}
