// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Content.Shared._Common.Consent;
using Robust.Client.Player;
using Robust.Shared.Network;

namespace Content.Client._Common.Consent;

public sealed class ClientConsentManager : IClientConsentManager
{
    [Dependency] private readonly IClientNetManager _netManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private PlayerConsentSettings? _consent;

    public bool HasLoaded => _consent is not null;

    public event Action? OnServerDataLoaded;

    public void Initialize()
    {
        _netManager.RegisterNetMessage<MsgUpdateConsent>(HandleUpdateConsent);
    }

    public void UpdateConsent(PlayerConsentSettings consentSettings)
    {
        var msg = new MsgUpdateConsent
        {
            Consent = consentSettings
        };
        _netManager.ClientSendMessage(msg);
    }

    public PlayerConsentSettings GetConsentSettings()
    {
        if (_consent is null)
        {
            throw new InvalidOperationException("Player doesn't have a session yet?");
        }

        return _consent;
    }

    private void HandleUpdateConsent(MsgUpdateConsent message)
    {
        _consent = message.Consent;

        OnServerDataLoaded?.Invoke();
    }
}
