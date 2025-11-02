using System.Numerics;
using Content.Server.Implants;
using Content.Shared._NF.Implants.Components;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mobs;
using Content.Shared.Salvage.Fulton;
using Robust.Shared.Timing;
using Robust.Shared.Map;
using Robust.Shared.Containers;
using Content.Shared._HL.Rescue.Rescue;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Shared.Implants.Components;
using Content.Server.Radio.EntitySystems;
using System.Collections.Generic;
using System.Threading;

namespace Content.Server._NF.Implants;

/// <summary>
/// Handles delayed teleportation for entities implanted with <see cref="MedicalTeleportImplantComponent"/>.
/// When the implanted owner dies, a fulton-like extraction is scheduled for 10 seconds later. If the owner
/// is revived before the timer elapses, the extraction is canceled.
/// </summary>
public sealed class MedicalTeleportImplantSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly RadioSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Listen for mob state changes relayed from the implanted owner to the implant entity.
        SubscribeLocalEvent<MedicalTeleportImplantComponent, ImplantRelayEvent<MobStateChangedEvent>>(OnMobStateChanged);
        // Cleanup: if the implant gets removed, ensure any pending extraction is canceled.
        SubscribeLocalEvent<MedicalTeleportImplantComponent, EntGotRemovedFromContainerMessage>(OnImplantRemoved, before: [typeof(SubdermalImplantSystem)]);

        // Do not subscribe to FultonedComponent shutdown here; FultonSystem already does and directed
        // subscriptions must be unique per component/event. We'll schedule our own timer on fulton.
    }

    // Track pending arrival announcements so they can be canceled on revival/removal.
    private readonly Dictionary<EntityUid, CancellationTokenSource> _pendingArrivals = new();

    private void OnImplantRemoved(EntityUid uid, MedicalTeleportImplantComponent comp, EntGotRemovedFromContainerMessage args)
    {
        // If the implant was removed from someone, cancel pending fulton on that person.
        if (!TryComp<SubdermalImplantComponent>(uid, out var implanted) || implanted.ImplantedEntity == null)
            return;

        var owner = implanted.ImplantedEntity.Value;
        // Cancel any pending arrival announcement for this owner
        if (_pendingArrivals.Remove(owner, out var removedCts))
        {
            removedCts.Cancel();
            removedCts.Dispose();
        }
        // stop flatline if playing
        if (comp.FlatlineStream != null)
        {
            _audio.Stop(comp.FlatlineStream);
            comp.FlatlineStream = null;
        }
        if (HasComp<FultonedComponent>(owner))
            RemComp<FultonedComponent>(owner);
    }

    private void OnMobStateChanged(EntityUid uid, MedicalTeleportImplantComponent comp, ImplantRelayEvent<MobStateChangedEvent> ev)
    {
        // Identify implanted owner
        if (!TryComp<SubdermalImplantComponent>(uid, out var implanted) || implanted.ImplantedEntity == null)
            return;

        var owner = implanted.ImplantedEntity.Value;
        var (_, _, oldState, newState, _) = ev.Event;

        // If revived (no longer Dead), cancel pending teleport and stop flatline
        if (oldState == MobState.Dead && newState != MobState.Dead)
        {
            if (comp.FlatlineStream != null)
            {
                _audio.Stop(comp.FlatlineStream);
                comp.FlatlineStream = null;
            }
            if (HasComp<FultonedComponent>(owner))
                RemComp<FultonedComponent>(owner);

            // Cancel pending arrival announcement if any
            if (_pendingArrivals.Remove(owner, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            // Clear cooldown since teleport did not occur
            comp.NextAllowedTeleport = _timing.CurTime;
            return;
        }

        // Only start when entering Dead state
        if (newState != MobState.Dead)
            return;

        // Respect teleport cooldown; if still cooling down, do nothing (no sound, no schedule)
        if (_timing.CurTime < comp.NextAllowedTeleport)
            return;

        // Play flatline immediately if not already playing
        if (comp.FlatlineSound != null)
        {
            if (comp.FlatlineStream == null || !_audio.IsPlaying(comp.FlatlineStream))
            {
                var stream = _audio.PlayPvs(comp.FlatlineSound, owner, AudioParams.Default.WithLoop(false));
                comp.FlatlineStream = stream?.Entity;
            }
        }

        // Already scheduled
        if (HasComp<FultonedComponent>(owner))
            return;

        // Find the nearest active fulton beacon to use as the destination
        var beacon = FindNearestBeacon(owner);
        if (beacon == null)
            return; // No beacon available; do nothing.

        var f = AddComp<FultonedComponent>(owner);
        f.Beacon = beacon;
        f.FultonDuration = comp.TeleportDelay; // purely for examining text consistency
        f.NextFulton = _timing.CurTime + comp.TeleportDelay;
        f.Removeable = true; // allow cancellation by systems/admins
        f.Sound = comp.TeleportSound; // sound to play when teleport occurs
        Dirty(owner, f);

        // Set cooldown at scheduling time; if teleport is canceled by revival, we reset it above
        comp.NextAllowedTeleport = _timing.CurTime + comp.TeleportCooldown;

        // Schedule a post-arrival announcement slightly after the expected teleport time.
        if (_pendingArrivals.Remove(owner, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        var newCts = new CancellationTokenSource();
        _pendingArrivals[owner] = newCts;
        var scheduledBeacon = beacon.Value;
        Robust.Shared.Timing.Timer.Spawn(comp.TeleportDelay + TimeSpan.FromMilliseconds(50), () =>
        {
            if (newCts.IsCancellationRequested || Deleted(owner))
                return;

            // Only announce for rescue beacons.
            if (!HasComp<RescueBeaconComponent>(scheduledBeacon))
                return;

            // Stop flatline sound if an implant is present on the owner.
            if (_containers.TryGetContainer(owner, ImplanterComponent.ImplantSlotId, out var implantContainer))
            {
                foreach (var implantEnt in implantContainer.ContainedEntities)
                {
                    if (!HasComp<MedicalTeleportImplantComponent>(implantEnt))
                        continue;

                    var med = Comp<MedicalTeleportImplantComponent>(implantEnt);
                    if (med.FlatlineStream != null)
                    {
                        _audio.Stop(med.FlatlineStream);
                        med.FlatlineStream = null;
                    }
                    break;
                }
            }

            _radio.SendRadioMessage(owner, "Vfib signal recieved, patient unresponsive, rescue extraction en route, Medical personel requested in trauma bay immediately", "Medical", owner);

            if (_pendingArrivals.Remove(owner, out var usedCts))
                usedCts.Dispose();
        }, newCts.Token);
    }

    private EntityUid? FindNearestBeacon(EntityUid owner)
    {
        var ownerXf = Transform(owner);
        var ownerWorld = _xform.GetWorldPosition(ownerXf);

        EntityUid? best = null;
        var bestDist2 = float.MaxValue;

        var enumerator = EntityQueryEnumerator<RescueBeaconComponent, TransformComponent>();
        while (enumerator.MoveNext(out var beaconUid, out _, out var beaconXf))
        {
            // Skip beacons inside containers
            if (_containers.IsEntityOrParentInContainer(beaconUid, xform: beaconXf))
                continue;

            var pos = _xform.GetWorldPosition(beaconXf);
            var dist2 = Vector2.DistanceSquared(ownerWorld, pos);
            if (dist2 < bestDist2)
            {
                bestDist2 = dist2;
                best = beaconUid;
            }
        }

        return best;
    }
}
