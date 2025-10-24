// SPDX-FileCopyrightText: Copyright (c) 2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Robust.Shared.GameStates;

namespace Content.Shared._Common.Consent;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public partial class ConsentComponent : Component
{
    /// <summary>
    /// Contains the consent toggles and text.
    /// It defaults to no toggles enabled.
    /// </summary>
    [DataField, AutoNetworkedField]
    public PlayerConsentSettings ConsentSettings = new();
}
