// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Content.Shared._Common.Consent;
using Robust.Shared.Network;
using Robust.Shared.Player;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._Common.Consent;

public interface IServerConsentManager
{
    event Action<ICommonSession, PlayerConsentSettings>? OnConsentUpdated;

    void Initialize();

    Task LoadData(ICommonSession session, CancellationToken cancel);

    /// <summary>
    /// Get a player's consent settings from their user id.
    /// </summary>
    PlayerConsentSettings GetPlayerConsentSettings(NetUserId userId);

    /// <summary>
    /// Return true if the target has updated their consent freetext since the reader last read it.
    /// </summary>
    bool ConsentTextUpdatedSinceLastRead(NetUserId readerUserId, NetUserId targetUserId);

    /// <summary>
    /// Update read recipe in the DB.
    /// </summary>
    Task UpdateReadReceipt(NetUserId readerUserId, NetUserId targetUserId);
}
