using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Coyote.ShuttleCrewStatus;

/// <summary>
/// System that periodically checks player-owned shuttles for active crew and updates IFF label colors accordingly.
/// Only applies to shuttles with the PlayerShuttle flag set (excludes asteroids, wrecks, and other non-player grids).
/// Shuttles with no crew or only disconnected crew show a gray label, while shuttles with active crew show normal white labels.
/// </summary>
public sealed class ShuttleCrewStatusSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedShuttleSystem _shuttle = default!;

    /// <summary>
    /// How often to check crew status on shuttles. Default: 3 minutes.
    /// Easily adjustable here for different update frequencies.
    /// </summary>
    // private readonly TimeSpan _checkInterval = TimeSpawan.FromMinutes(3);
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The color to use when a shuttle has no active crew.
    /// </summary>
    private readonly Color _inactiveCrewColor = Color.Gray;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IFFComponent, MapInitEvent>(OnIFFMapInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnIFFMapInit(EntityUid uid, IFFComponent component, MapInitEvent args)
    {
        // Only track player-owned shuttles with IFF components
        if (!TryComp<ShuttleComponent>(uid, out var shuttle))
            return;

        // Skip non-player shuttles (asteroids, wrecks, etc.)
        if ((component.Flags & IFFFlags.IsPlayerShuttle) == 0)
            return;

        // Add the crew status component to track this shuttle
        var crewStatus = EnsureComp<ShuttleCrewStatusComponent>(uid);
        crewStatus.NextCheck = _timing.CurTime + _checkInterval;
        crewStatus.OriginalColor = component.Color;
        crewStatus.HasActiveCrew = true; // Start assuming crew is active
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        // Clean up all crew status components on round restart
        var query = EntityQueryEnumerator<ShuttleCrewStatusComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemComp<ShuttleCrewStatusComponent>(uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ShuttleCrewStatusComponent, ShuttleComponent, IFFComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var crewStatus, out var shuttle, out var iff, out var xform))
        {
            // Skip if it's not time to check yet
            if (currentTime < crewStatus.NextCheck)
                continue;

            // Schedule next check
            crewStatus.NextCheck = currentTime + _checkInterval;

            // Check if there are any active players on this grid
            var hasActiveCrew = HasActivePlayersOnGrid(uid, xform);

            // Only update IFF if the crew status changed
            if (hasActiveCrew != crewStatus.HasActiveCrew)
            {
                crewStatus.HasActiveCrew = hasActiveCrew;

                if (hasActiveCrew)
                {
                    // Restore original color
                    if (crewStatus.OriginalColor.HasValue)
                    {
                        _shuttle.SetIFFColor(uid, crewStatus.OriginalColor.Value, iff);
                    }
                }
                else
                {
                    // Store current color if we haven't already
                    if (!crewStatus.OriginalColor.HasValue)
                    {
                        crewStatus.OriginalColor = iff.Color;
                    }

                    // Set to gray to indicate no active crew
                    _shuttle.SetIFFColor(uid, _inactiveCrewColor, iff);
                }
            }
        }
    }

    /// <summary>
    /// Checks if there are any active (connected) players on the specified grid.
    /// </summary>
    /// <param name="gridUid">The grid entity to check</param>
    /// <param name="gridXform">The transform component of the grid</param>
    /// <returns>True if there are active players on the grid, false otherwise</returns>
    private bool HasActivePlayersOnGrid(EntityUid gridUid, TransformComponent gridXform)
    {
        // Iterate through all player sessions
        foreach (var session in _playerManager.Sessions)
        {
            // Skip disconnected or zombie sessions (SSD players)
            if (session.Status is SessionStatus.Disconnected or SessionStatus.Zombie)
                continue;

            // Check if the player has an attached entity
            if (session.AttachedEntity is not { } playerEntity)
                continue;

            // Check if the player entity still exists
            if (!TryComp<TransformComponent>(playerEntity, out var playerXform))
                continue;

            // Check if the player is on this grid
            if (playerXform.GridUid == gridUid)
                return true;
        }

        return false;
    }
}
