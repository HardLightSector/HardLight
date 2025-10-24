// SPDX-FileCopyrightText: Copyright (c) 2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Common.Consent.EffectConditions;

/// <summary>
/// Checks if the target entity has consented to a specific toggle.
/// </summary>
public sealed partial class ConsentCondition : EntityEffectCondition
{
    [DataField]
    public ProtoId<ConsentTogglePrototype> Consent = default!;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        return args.EntityManager.System<SharedConsentSystem>()
            .HasConsent(args.TargetEntity, Consent);
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
    {
        return Loc.GetString("reagent-effect-condition-guidebook-consent-condition", ("consent", Consent));
    }
}
