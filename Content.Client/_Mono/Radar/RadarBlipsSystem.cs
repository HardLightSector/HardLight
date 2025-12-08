// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 Ilya246
// SPDX-FileCopyrightText: 2025 ark1368
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._Mono.Radar;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._Mono.Radar;

public sealed partial class RadarBlipsSystem : EntitySystem
{
    private const double BlipStaleSeconds = 3.0;
    private static readonly List<(Vector2, float, Color, RadarBlipShape)> EmptyBlipList = new();
    private static readonly List<(NetCoordinates Position, Vector2 Vel, float Scale, Color Color, RadarBlipShape Shape)> EmptyRawBlipList = new();
    private static readonly List<(Vector2 Start, Vector2 End, float Thickness, Color Color)> EmptyHitscanList = new();
    private TimeSpan _lastRequestTime = TimeSpan.Zero;
    private static readonly TimeSpan RequestThrottle = TimeSpan.FromMilliseconds(250);

    // Maximum distance for blips to be considered visible
    private const float MaxBlipRenderDistance = 1000f;

    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _lastUpdatedTime;
    private List<(NetCoordinates Position, Vector2 Vel, float Scale, Color Color, RadarBlipShape Shape)> _blips = new();
    private List<(Vector2 Start, Vector2 End, float Thickness, Color Color)> _hitscans = new();
    private Vector2 _radarWorldPosition;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GiveBlipsEvent>(HandleReceiveBlips);
    }

    private void HandleReceiveBlips(GiveBlipsEvent ev, EntitySessionEventArgs args)
    {
        _blips = ev.Blips;
        _lastUpdatedTime = _timing.CurTime;
    }

    public void RequestBlips(EntityUid console)
    {
        // Only request if we have a valid console
        if (!Exists(console))
            return;

        var netConsole = GetNetEntity(console);

        var ev = new RequestBlipsEvent(netConsole);
        RaiseNetworkEvent(ev);
    }

    /// <summary>
    /// Gets the current blips as world positions with their scale, color and shape.
    /// </summary>
    public List<(EntityCoordinates Position, float Scale, Color Color, RadarBlipShape Shape)> GetCurrentBlips()
    {
        // If it's been more than a second since our last update,
        // the data is considered stale - return an empty list
        if (_timing.CurTime.TotalSeconds - _lastUpdatedTime.TotalSeconds > BlipStaleSeconds)
            return new List<(EntityCoordinates, float, Color, RadarBlipShape)>();

        var result = new List<(EntityCoordinates, float, Color, RadarBlipShape)>(_blips.Count);

        foreach (var blip in _blips)
        {
            var coord = GetCoordinates(blip.Position);

            if (!coord.IsValid(EntityManager))
                continue;

            var predictedPos = new EntityCoordinates(coord.EntityId, coord.Position + blip.Vel * (float)(_timing.CurTime - _lastUpdatedTime).TotalSeconds);

            // Distance culling for world position blips
            if (Vector2.DistanceSquared(predictedPos.Position, _radarWorldPosition) > MaxBlipRenderDistance * MaxBlipRenderDistance)
                continue;

            result.Add((predictedPos, blip.Scale, blip.Color, blip.Shape));
        }

        return result;
    }

    /// <summary>
    /// Gets the hitscan lines to be rendered on the radar
    /// </summary>
    public List<(Vector2 Start, Vector2 End, float Thickness, Color Color)> GetHitscanLines()
    {
        if (_timing.CurTime.TotalSeconds - _lastUpdatedTime.TotalSeconds > BlipStaleSeconds)
            return new List<(Vector2, Vector2, float, Color)>();

        var result = new List<(Vector2, Vector2, float, Color)>(_hitscans.Count);

        foreach (var hitscan in _hitscans)
        {
            var worldStart = hitscan.Start;
            var worldEnd = hitscan.End;

            // Distance culling - check if either end of the line is in range
            var startDist = Vector2.DistanceSquared(worldStart, _radarWorldPosition);
            var endDist = Vector2.DistanceSquared(worldEnd, _radarWorldPosition);

            if (startDist > MaxBlipRenderDistance * MaxBlipRenderDistance &&
                endDist > MaxBlipRenderDistance * MaxBlipRenderDistance)
                continue;

            result.Add((worldStart, worldEnd, hitscan.Thickness, hitscan.Color));
        }

        return result;
    }
}
