using Content.Shared.Actions;
using Content.Shared.CM14.Xenos;
using Content.Shared.CM14.Xenos.Evolution;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.CM14.Xenos.Evolution;

/// <summary>
/// Handles automatic evolution for AI-controlled (non-player) xenos when their evolution action is ready.
/// </summary>
public sealed class XenoAIAutoEvolveSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoAIAutoEvolveComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<XenoAIAutoEvolveComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<XenoAIAutoEvolveComponent> ent, ref MobStateChangedEvent args)
    {
        // Remove auto-evolve component when xeno dies
        if (args.NewMobState == MobState.Dead)
        {
            RemComp<XenoAIAutoEvolveComponent>(ent);
        }
    }

    private void OnMapInit(Entity<XenoAIAutoEvolveComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextCheckTime = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.CheckInterval);
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<XenoAIAutoEvolveComponent, XenoComponent, MetaDataComponent>();

        while (query.MoveNext(out var uid, out var autoEvolve, out var xeno, out var metaData))
        {
            // Skip player-controlled xenos - only AI xenos should auto-evolve
            if (HasComp<ActorComponent>(uid))
                continue;

            // Only check at intervals to avoid excessive processing
            if (curTime < autoEvolve.NextCheckTime)
                continue;

            autoEvolve.NextCheckTime = curTime + TimeSpan.FromSeconds(autoEvolve.CheckInterval);
            Dirty(uid, autoEvolve);

            // Skip if this xeno can't evolve
            if (xeno.EvolvesTo.Count == 0)
            {
                Log.Debug($"[XenoAI] {ToPrettyString(uid)} has no evolution targets");
                continue;
            }

            // Check if the evolve action exists
            if (xeno.EvolveAction == null)
            {
                Log.Debug($"[XenoAI] {ToPrettyString(uid)} has no evolve action");
                continue;
            }

            var cooldown = _actions.GetCooldown(xeno.EvolveAction.Value);
            if (cooldown.HasValue)
            {
                var timeRemaining = cooldown.Value.End - curTime;
                Log.Debug($"[XenoAI] {ToPrettyString(uid)} - Action cooldown remaining: {timeRemaining.TotalSeconds:F1}s");
                continue;
            }
            Log.Info($"[XenoAI] Triggering evolution for {ToPrettyString(uid)}");
            // Trigger the evolution action
            var ev = new XenoOpenEvolutionsEvent();
            RaiseLocalEvent(uid, ev);
        }
    }
}
