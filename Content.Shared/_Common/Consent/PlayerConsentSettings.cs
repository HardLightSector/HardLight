// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using System.Linq;
using Content.Shared._Common.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Common.Consent;

[Serializable, NetSerializable]
public sealed class PlayerConsentSettings
{
    [ViewVariables]
    public string Freetext;

    [ViewVariables]
    public Dictionary<ProtoId<ConsentTogglePrototype>, string> Toggles;

    public PlayerConsentSettings()
    {
        Freetext = string.Empty;
        Toggles = new Dictionary<ProtoId<ConsentTogglePrototype>, string>();
    }

    public PlayerConsentSettings(
        string freetext,
        Dictionary<ProtoId<ConsentTogglePrototype>, string> toggles)
    {
        Freetext = freetext;
        Toggles = toggles;
    }

    public void EnsureValid(IConfigurationManager configManager, IPrototypeManager prototypeManager)
    {
        var maxLength = configManager.GetCVar(ConsentSystemCCVars.ConsentFreetextMaxLength);
        Freetext = Freetext.Trim();
        if (Freetext.Length > maxLength)
            Freetext = Freetext.Substring(0, maxLength);

        Toggles = Toggles.Where(t =>
            prototypeManager.HasIndex<ConsentTogglePrototype>(t.Key)
            && t.Value == "on"
        ).ToDictionary();
    }
}
