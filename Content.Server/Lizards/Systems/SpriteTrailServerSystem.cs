using Content.Shared.Lizards.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Components;
using System;
using System.Numerics;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Server.Lizards.Systems;

public sealed class SpriteTrailServerSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xformSys = default!;

    private readonly Dictionary<EntityUid, Queue<(EntityCoordinates, Angle)>> _buffers = new();
    private readonly Dictionary<EntityUid, bool> _leaderActive = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<TrailLeaderComponent, ComponentStartup>(OnLeaderStartup);
        SubscribeLocalEvent<TrailLeaderComponent, ComponentShutdown>(OnLeaderShutdown);
    }

    private void OnLeaderStartup(Entity<TrailLeaderComponent> ent, ref ComponentStartup args)
    {
        _buffers[ent.Owner] = new Queue<(EntityCoordinates, Angle)>(ent.Comp.BufferSize);
        _leaderActive[ent.Owner] = false;
    }

    private void OnLeaderShutdown(Entity<TrailLeaderComponent> ent, ref ComponentShutdown args)
    {
        _buffers.Remove(ent.Owner);
        _leaderActive.Remove(ent.Owner);
    }

    // Followers don't need a startup subscription here; linking handled elsewhere.

    public override void Update(float frameTime)
    {
        var qLead = EntityQueryEnumerator<TrailLeaderComponent, TransformComponent>();
        while (qLead.MoveNext(out var uid, out var leader, out var xform))
        {
            if (!_buffers.TryGetValue(uid, out var buf))
                continue;
            // Only enqueue if moved a small threshold to reduce stacking
            var coords = xform.Coordinates;
            var rot = xform.LocalRotation;
            var shouldEnqueue = true;
            if (buf.Count > 0)
            {
                var last = buf.Last();
                var lastMap = last.Item1.ToMap(EntityManager, _xformSys);
                var currMap = coords.ToMap(EntityManager, _xformSys);
                if (Vector2.Distance(lastMap.Position, currMap.Position) < 0.02f && Math.Abs((rot - last.Item2).Theta) < 0.01f)
                    shouldEnqueue = false;
            }

            if (shouldEnqueue)
            {
                if (buf.Count >= leader.BufferSize)
                    buf.Dequeue();
                buf.Enqueue((coords, rot));
                _leaderActive[uid] = true;
            }
            else
            {
                // Head is effectively stationary: mark inactive but preserve buffer
                // so followers keep their spaced positions instead of collapsing.
                _leaderActive[uid] = false;
            }
        }

        var qFollow = EntityQueryEnumerator<TrailFollowerComponent, TransformComponent>();
        while (qFollow.MoveNext(out var uid, out var follower, out var xform))
        {
            if (follower.Leader == default || !_buffers.TryGetValue(follower.Leader, out var buf))
                continue;
            var leaderIsActive = _leaderActive.TryGetValue(follower.Leader, out var active) && active;
            // Disable smoothing while leader is inactive; restore when active.
            var smooth = leaderIsActive ? Math.Clamp(follower.SmoothFactor, 0f, 1f) : 0f;
            // Determine target from buffer or leader
            (EntityCoordinates, Angle) target;
            if (buf.Count < follower.Delay)
            {
                var lxf = Transform(follower.Leader);
                target = (lxf.Coordinates, lxf.LocalRotation);
            }
            else
            {
                var arr = buf.ToArray();
                target = arr[arr.Length - follower.Delay];
            }
            // Use target position directly (no offset) so segments don't drift diagonally
            var targetWorld = target.Item1.ToMap(EntityManager, _xformSys);
            if (!leaderIsActive)
            {
                // If within deadzone to target, hold position; otherwise snap to target to avoid creeping overlap.
                if (Vector2.Distance(xform.WorldPosition, targetWorld.Position) <= follower.StopDeadzone)
                    continue;
                _xformSys.SetCoordinates(uid, target.Item1);
                continue;
            }
            if (Vector2.Distance(xform.WorldPosition, targetWorld.Position) < 0.001f)
            {
                // Even when stationary, align rotation toward the leader's position
                var dir = targetWorld.Position - xform.WorldPosition;
                if (dir.LengthSquared() > 0f)
                {
                    var desiredWorldRot = Angle.FromDegrees((float)(Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI) + follower.RotationOffsetDeg);
                    var parentUid = xform.ParentUid;
                    var parentWorldRot = parentUid != EntityUid.Invalid ? _xformSys.GetWorldRotation(parentUid) : Angle.Zero;
                    var desiredLocalRot = desiredWorldRot - parentWorldRot;
                    _xformSys.SetLocalRotation(uid, desiredLocalRot);
                }
                // else: keep current local rotation unchanged when perfectly aligned
                continue;
            }

            // Smoothing: interpolate current position towards target
            if (smooth > 0f)
            {
                var currWorld = xform.WorldPosition;
                var newPos = Vector2.Lerp(currWorld, targetWorld.Position, smooth);
                _xformSys.SetWorldPosition(uid, newPos);
            }
            else
            {
                _xformSys.SetCoordinates(uid, target.Item1);
            }
            // Smoothly rotate to face the leader/target direction
            var dir2 = targetWorld.Position - _xformSys.GetWorldPosition(uid);
            Angle desiredWorld = dir2.LengthSquared() > 0f
                ? Angle.FromDegrees((float)(Math.Atan2(dir2.Y, dir2.X) * 180.0 / Math.PI) + follower.RotationOffsetDeg)
                : target.Item2;

            var pUid = xform.ParentUid;
            var pWorldRot = pUid != EntityUid.Invalid ? _xformSys.GetWorldRotation(pUid) : Angle.Zero;
            var desiredLocal = desiredWorld - pWorldRot;
            if (smooth > 0f)
            {
                var curr = xform.LocalRotation;
                var newRotTheta = Robust.Shared.Maths.MathHelper.Lerp(curr.Theta, desiredLocal.Theta, smooth);
                _xformSys.SetLocalRotation(uid, Angle.FromDegrees((float)(newRotTheta * 180.0 / Math.PI)));
            }
            else
            {
                _xformSys.SetLocalRotation(uid, desiredLocal);
            }
        }
    }
}
