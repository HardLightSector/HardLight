// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Content.Shared._Common.Consent;

namespace Content.Client._Common.Consent;

public interface IClientConsentManager
{
    event Action OnServerDataLoaded;
    bool HasLoaded { get; }

    void Initialize();
    void UpdateConsent(PlayerConsentSettings consentSettings);
    PlayerConsentSettings GetConsentSettings();
}
