using Content.Shared._Mono.Radar;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Content.Server.Weapons.Hitscan;

/// <summary>
/// System that creates radar visualization for hitscan weapon fire.
/// Shows a line on the radar representing the hitscan beam path.
/// </summary>
public sealed class HitscanRadarVisualizationSystem : EntitySystem
{
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<(Vector2 Start, Vector2 End, float Thickness, Color Color), TimeSpan> _activeHitscans = new();

    public override void Initialize()
    {
        base.Initialize();
        
        SubscribeLocalEvent<HitscanRadarSignatureComponent, HitscanRaycastFiredEvent>(OnHitscanFired);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Clean up expired hitscans
        var currentTime = _timing.CurTime;
        var toRemove = new List<(Vector2, Vector2, float, Color)>();
        
        foreach (var (hitscan, expiryTime) in _activeHitscans)
        {
            if (currentTime >= expiryTime)
                toRemove.Add(hitscan);
        }

        foreach (var hitscan in toRemove)
        {
            _activeHitscans.Remove(hitscan);
        }

        // Only broadcast when hitscans change (added or removed), not every frame
        // The radar system maintains its own state and doesn't need constant updates
        if (toRemove.Count > 0)
        {
            var hitscanList = _activeHitscans.Keys.ToList();
            var ev = new GiveBlipsEvent(new List<(NetCoordinates Position, Vector2 Vel, float Scale, Color Color, RadarBlipShape Shape)>(), hitscanList);
            RaiseNetworkEvent(ev);
        }
    }

    private void OnHitscanFired(EntityUid uid, HitscanRadarSignatureComponent component, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || args.Gun == null)
            return;

        var gunXform = Transform(args.Gun.Value);
        if (!gunXform.MapUid.HasValue)
            return;

        var fromPos = _transform.GetMapCoordinates(args.Gun.Value);
        
        // Determine end position
        Vector2 toPos;
        if (args.HitEntity != null && TryComp<TransformComponent>(args.HitEntity.Value, out var hitXform))
        {
            toPos = _transform.GetMapCoordinates(args.HitEntity.Value).Position;
        }
        else
        {
            // If no hit, extend along gun's world rotation direction
            var worldRot = _transform.GetWorldRotation(args.Gun.Value);
            var direction = worldRot.ToWorldVec();
            var maxLength = 45f; // Default hitscan max length
            toPos = fromPos.Position + direction * maxLength;
        }

        // Create a hitscan line
        var color = component.RadarColor ?? Color.Red;
        var thickness = 2f; // Line thickness on radar
        var lifetime = component.LifeTime > 0 ? component.LifeTime : 0.5f; // Default 0.5 seconds
        
        var hitscanLine = (fromPos.Position, toPos, thickness, color);
        var expiryTime = _timing.CurTime + TimeSpan.FromSeconds(lifetime);
        
        // Add or update the hitscan in our tracking dictionary
        _activeHitscans[hitscanLine] = expiryTime;
        
        // Immediately broadcast the new hitscan to clients
        var hitscanList = _activeHitscans.Keys.ToList();
        var ev = new GiveBlipsEvent(new List<(NetCoordinates Position, Vector2 Vel, float Scale, Color Color, RadarBlipShape Shape)>(), hitscanList);
        RaiseNetworkEvent(ev);
    }
}
