// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Content.Shared.Examine;
using Content.Shared.Mind;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Common.Consent;

public abstract partial class SharedConsentSystem : EntitySystem
{
    [Dependency] protected readonly SharedMindSystem _mindSystem = default!;
    [Dependency] private readonly ExamineSystemShared _examineSystem = default!;

    protected virtual bool ConsentTextUpdatedSinceLastRead(Entity<ConsentComponent> targetEnt, EntityUid readerUid)
    {
        // Overridden in server ConsentSystem.
        // Predicting this would be a pain.
        // You could probably do it by putting a bool on ConsentComponent and write custom logic for networking the bool so it's correct for each player.
        // But I don't think having to wait a few milis for the red dot to appear is a big deal.
        return false;
    }

    protected virtual void UpdateReadReceipt(Entity<ConsentComponent> targetEnt, EntityUid readerUid)
    {
        // Overridden in server ConsentSystem.
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<ConsentComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
    }

    public bool HasConsent(Entity<ConsentComponent?> ent, ProtoId<ConsentTogglePrototype> consentId)
    {
        if (!Resolve(ent, ref ent.Comp))
        {
            // Entities that have never been controlled by a player consent to all mechanics.
            return true;
        }

        return ent.Comp.ConsentSettings.Toggles.TryGetValue(consentId, out var val) && val == "on";
    }

    private void OnGetExamineVerbs(Entity<ConsentComponent> ent, ref GetVerbsEvent<ExamineVerb> args)
    {
        var user = args.User;
        bool updatedSinceLastRead = ConsentTextUpdatedSinceLastRead(ent, user);
        ResPath iconPath = updatedSinceLastRead
            ? new ("/Textures/_Common/Interface/VerbIcons/consent_examine_with_red_dot.svg.192dpi.png")
            : new ("/Textures/Interface/VerbIcons/settings.svg.192dpi.png");

        args.Verbs.Add(new()
        {
            Text = Loc.GetString("consent-examine-verb"),
            Icon = new SpriteSpecifier.Texture(iconPath),
            Act = () =>
            {
                var message = GetConsentText(ent.Comp.ConsentSettings);
                _examineSystem.SendExamineTooltip(user, ent, message, getVerbs: false, centerAtCursor: false);
                UpdateReadReceipt(ent, user);
            },
            Category = VerbCategory.Examine,
            CloseMenu = true,
        });
    }

    private FormattedMessage GetConsentText(PlayerConsentSettings consentSettings)
    {
        var text = consentSettings.Freetext;
        if (text == string.Empty)
        {
            text = Loc.GetString("consent-examine-not-set");
        }

        var messageUnsanitized = new FormattedMessage();
        var message = new FormattedMessage();
        messageUnsanitized.AddMarkupPermissive(text);
        foreach (var node in messageUnsanitized)
        {
            if (node.Name is null || node.Name == "bold" || node.Name == "italic" || node.Name == "bullet")
            {
                message.PushTag(node);
            }
        }

        return message;
    }
}
