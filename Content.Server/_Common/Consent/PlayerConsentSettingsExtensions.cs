// SPDX-FileCopyrightText: Copyright (c) 2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Content.Shared._Common.Consent;
using Content.Server.Database;
using Robust.Shared.Prototypes;
using System.Linq;

public static class PlayerConsentSettinsExtensions
{
    public static PlayerConsentSettings ToPlayerConsentSettings(this ConsentSettings dbConsentSettings)
    {
        return new(dbConsentSettings.ConsentFreetext ?? "", (dbConsentSettings.ConsentToggles ?? new()).ToDictionary(
            keySelector: t => new ProtoId<ConsentTogglePrototype>(t.ToggleProtoId),
            elementSelector: t => t.ToggleProtoState
        ));
    }

    public static ConsentSettings ToDbObject(this PlayerConsentSettings playerConsentSettings)
    {
        return new()
        {
            ConsentFreetext = playerConsentSettings.Freetext,
            ConsentToggles = playerConsentSettings.Toggles
                .Select(x => new ConsentToggle {
                    ToggleProtoId = x.Key,
                    ToggleProtoState = x.Value,
                })
                .ToList()
        };
    }
}
