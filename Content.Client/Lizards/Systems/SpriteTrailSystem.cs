using Content.Shared.Lizards.Components;
using System.Linq;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client.Lizards.Systems;

public sealed class SpriteTrailSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TransformSystem _xformSys = default!;

    private readonly Dictionary<EntityUid, Queue<(EntityCoordinates, Angle)>> _buffers = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<TrailLeaderComponent, ComponentStartup>(OnLeaderStartup);
        SubscribeLocalEvent<TrailLeaderComponent, ComponentShutdown>(OnLeaderShutdown);
        SubscribeLocalEvent<TrailFollowerComponent, ComponentStartup>(OnFollowerStartup);
    }

    private void OnLeaderStartup(Entity<TrailLeaderComponent> ent, ref ComponentStartup args)
    {
        _buffers[ent.Owner] = new Queue<(EntityCoordinates, Angle)>(ent.Comp.BufferSize);
    }

    private void OnLeaderShutdown(Entity<TrailLeaderComponent> ent, ref ComponentShutdown args)
    {
        _buffers.Remove(ent.Owner);
    }

    private void OnFollowerStartup(Entity<TrailFollowerComponent> ent, ref ComponentStartup args)
    {
        // Ensure buffer exists for leader
        if (ent.Comp.Leader != default && !_buffers.ContainsKey(ent.Comp.Leader))
            _buffers[ent.Comp.Leader] = new Queue<(EntityCoordinates, Angle)>();
    }

    public override void Update(float frameTime)
    {
        var queryLeader = EntityQueryEnumerator<TrailLeaderComponent, TransformComponent>();
        while (queryLeader.MoveNext(out var uid, out var leader, out var xform))
        {
            if (!_buffers.TryGetValue(uid, out var buf))
                continue;

            if (buf.Count >= leader.BufferSize)
                buf.Dequeue();

            buf.Enqueue((xform.Coordinates, xform.LocalRotation));
        }

        var queryFollower = EntityQueryEnumerator<TrailFollowerComponent, SpriteComponent, TransformComponent>();
        while (queryFollower.MoveNext(out var uid, out var follower, out var sprite, out var xform))
        {
            if (follower.Leader == default || !_buffers.TryGetValue(follower.Leader, out var buf))
                continue;

            if (buf.Count < follower.Delay)
                continue;

            var arr = buf.ToArray();
            var target = arr[arr.Length - follower.Delay];
            _xformSys.SetCoordinates(uid, target.Item1);
            _xformSys.SetLocalRotation(uid, target.Item2);
        }
    }
}
