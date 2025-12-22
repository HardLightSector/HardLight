using Content.Server._HL.ShuttleDeedTracking.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._HL.ShuttleDeedTracking.Systems;

/// <summary>
/// System that periodically checks the shuttle deed owner's session status.
/// If the owner is offline/inactive for 6 consecutive checks, the grid is deleted.
/// </summary>
public sealed class ShuttleDeedOwnerTrackingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    /// <summary>
    /// How often to check the deed owner's status. Default: 10 minutes.
    /// </summary>
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10);

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("shuttle-deed-tracking");

        SubscribeLocalEvent<ShuttleDeedComponent, ComponentStartup>(OnDeedStartup);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnDeedStartup(EntityUid uid, ShuttleDeedComponent component, ComponentStartup args)
    {
        // Only track shuttles (grids with ShuttleComponent), not ID cards
        if (!HasComp<ShuttleComponent>(uid))
            return;

        // Only track if we have an owner's UserId
        if (string.IsNullOrEmpty(component.OwnerUserId))
            return;

        // Add the tracking component to this shuttle
        var tracking = EnsureComp<ShuttleDeedOwnerTrackingComponent>(uid);
        tracking.NextCheck = _timing.CurTime + _checkInterval;
        tracking.InactiveCheckCount = 0;
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        // Clean up all tracking components on round restart
        var query = EntityQueryEnumerator<ShuttleDeedOwnerTrackingComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemComp<ShuttleDeedOwnerTrackingComponent>(uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ShuttleDeedOwnerTrackingComponent, ShuttleDeedComponent, ShuttleComponent>();

        while (query.MoveNext(out var uid, out var tracking, out var deed, out _))
        {
            // Skip if it's not time to check yet
            if (currentTime < tracking.NextCheck)
                continue;

            // Schedule next check
            tracking.NextCheck = currentTime + _checkInterval;

            // Check if the deed owner's session is active
            var isOwnerActive = IsOwnerSessionActive(deed.OwnerUserId);

            if (isOwnerActive)
            {
                // Owner is online and active, reset the counter
                if (tracking.InactiveCheckCount > 0)
                {
                    _sawmill.Debug($"Shuttle {ToPrettyString(uid)} owner is now active, resetting inactive count from {tracking.InactiveCheckCount}");
                }
                tracking.InactiveCheckCount = 0;
            }
            else
            {
                // Owner is offline/inactive, increment the counter
                tracking.InactiveCheckCount++;
                _sawmill.Debug($"Shuttle {ToPrettyString(uid)} owner inactive check {tracking.InactiveCheckCount}/{tracking.MaxInactiveChecks}");

                // Check if we've hit the threshold
                if (tracking.InactiveCheckCount >= tracking.MaxInactiveChecks)
                {
                    _sawmill.Info($"Shuttle {ToPrettyString(uid)} owner has been inactive for {tracking.InactiveCheckCount} checks. Deleting grid.");
                    QueueDel(uid);
                }
            }
        }
    }

    /// <summary>
    /// Checks if the owner's session is currently active.
    /// Returns true if the owner is online and has an active session (not disconnected or zombie).
    /// </summary>
    private bool IsOwnerSessionActive(string? ownerUserId)
    {
        if (string.IsNullOrEmpty(ownerUserId))
            return false;

        // Try to parse the UserId from string (stored as Guid string)
        if (!Guid.TryParse(ownerUserId, out var guidValue))
            return false;

        var userId = new NetUserId(guidValue);

        // Check if there's an active session for this user
        if (!_playerManager.TryGetSessionById(userId, out var session))
            return false;

        // Check session status - only count as active if not disconnected/zombie
        if (session.Status is SessionStatus.Disconnected or SessionStatus.Zombie)
            return false;

        // Session is connected and active
        return true;
    }
}
