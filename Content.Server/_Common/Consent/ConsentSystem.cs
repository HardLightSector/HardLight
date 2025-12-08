// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Content.Shared._Common.Consent;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using System.Linq;

namespace Content.Server._Common.Consent;

public sealed class ConsentSystem : SharedConsentSystem
{
    [Dependency] private readonly IServerConsentManager _consentManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MindComponent, MindGotAddedEvent>(OnMindGotAdded);
        SubscribeLocalEvent<ConsentComponent, MindRemovedMessage>(OnMindRemoved);
        _consentManager.OnConsentUpdated += OnConsentUpdated;
    }

    private void UpdateConsent(Entity<ConsentComponent> ent, PlayerConsentSettings consentSettings)
    {
        foreach (var protoId in ent.Comp.ConsentSettings.Toggles.Keys.Union(consentSettings.Toggles.Keys))
        {
            string? oldState = ent.Comp.ConsentSettings.Toggles.GetValueOrDefault(protoId);
            string? newState = consentSettings.Toggles.GetValueOrDefault(protoId);

            if (oldState == newState)
            {
                continue;
            }

            var ev = new EntityConsentToggleUpdatedEvent
            {
                Ent = ent,
                ConsentToggleProtoId = protoId,
                OldState = oldState,
                NewState = newState,
            };

            RaiseLocalEvent(ent, ref ev);
        }

        ent.Comp.ConsentSettings = consentSettings;
        Dirty(ent);
    }

    private void OnMindGotAdded(Entity<MindComponent> ent, ref MindGotAddedEvent args)
    {
        var consentComp = EnsureComp<ConsentComponent>(args.Container);

        if (args.Mind.Comp.UserId is not NetUserId userId)
        {
            // I can't think of a situation where a mind would be added to an ent without a player,
            // but if it happens they will have the default consent settings.
            return;
        }

        consentComp.ConsentSettings = _consentManager.GetPlayerConsentSettings(userId);
    }

    private void OnMindRemoved(Entity<ConsentComponent> ent, ref MindRemovedMessage args)
    {
        // Clear consent data when the mind leaves the body.
        UpdateConsent(ent, new());
    }

    private void OnConsentUpdated(ICommonSession session, PlayerConsentSettings consentSettings)
    {
        if (session.AttachedEntity is not EntityUid uid)
        {
            // Player isn't in the game, so there's no ConsentComponent to update.
            return;
        }

        Log.Debug("Consent settings updated by entity with uid: " + uid);

        if (!TryComp<ConsentComponent>(uid, out var consentComponent))
        {
            // Consent comp got removed, or never got added in OnMindGotAdded ???
            // This should never happen. Could be sign of a critical bug, or an admin deleting ConsentComponent.
            // Fix it and log.

            Log.Warning($"Missing ConsentComponent on player-controlled entity {uid}. Adding it.");
            consentComponent = EnsureComp<ConsentComponent>(uid);
        }

        UpdateConsent((uid, consentComponent), consentSettings);
    }

    protected override bool ConsentTextUpdatedSinceLastRead(Entity<ConsentComponent> targetEnt, EntityUid readerUid)
    {
        if (!_mindSystem.TryGetMind(readerUid, out _, out var readerMind)
            || readerMind.UserId is not NetUserId readerUserId
            || !_mindSystem.TryGetMind(targetEnt, out _, out var entMind)
            || entMind.UserId is not NetUserId targetUserId)
        {
            return false;
        }

        return _consentManager.ConsentTextUpdatedSinceLastRead(readerUserId, targetUserId);
    }

    protected override void UpdateReadReceipt(Entity<ConsentComponent> targetEnt, EntityUid readerUid)
    {
        if (!_mindSystem.TryGetMind(readerUid, out _, out var readerMind)
            || readerMind.UserId is not NetUserId readerUserId
            || !_mindSystem.TryGetMind(targetEnt, out _, out var entMind)
            || entMind.UserId is not NetUserId targetUserId)
        {
            return;
        }

        _consentManager.UpdateReadReceipt(readerUserId, targetUserId);
    }
}
