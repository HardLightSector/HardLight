using Content.Shared.Lizards.Components;
using Robust.Shared.Map;

namespace Content.Server.Lizards.Systems;

public sealed class LizardLinkerSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<TrailFollowerComponent, ComponentStartup>(OnFollowerStartup);
    }

    private void OnFollowerStartup(Entity<TrailFollowerComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.Leader != default)
            return;

        // Try to find an entity with TrailLeader in same coordinates to follow
        var enumerator = EntityQueryEnumerator<TrailLeaderComponent, TransformComponent>();
        while (enumerator.MoveNext(out var leaderUid, out var leaderComp, out var leaderXform))
        {
            var xform = Transform(ent.Owner);
            if (leaderXform.MapID == xform.MapID && leaderXform.Coordinates.TryDistance(EntityManager, xform.Coordinates, out var dist) && dist < 1.0f)
            {
                ent.Comp.Leader = leaderUid;
                Dirty(ent);
                break;
            }
        }
    }
}
