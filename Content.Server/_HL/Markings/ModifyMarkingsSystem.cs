using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared._HL.Markings;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Audio.Systems;
using Content.Shared.IdentityManagement;

namespace Content.Server._HL.Markings;

public sealed class ModifyMarkingsSystem : SharedModifyMarkingsSystem
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly MarkingManager _markingManager = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoid = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModifyMarkingsComponent, ModifyMarkingsDoAfterEvent>(ToggleUndies);
    }

    private void ToggleUndies(Entity<ModifyMarkingsComponent> ent, ref ModifyMarkingsDoAfterEvent args)
    {
        if (!_markingManager.TryGetMarking(args.Marking, out var mProt))
            return;

        if (!TryComp<HumanoidAppearanceComponent>(args.Target, out var humanoid))
            return;

        var isVisible = args.IsVisible;
        var localizedName = args.MarkingPrototypeName;

        var user = args.User;
        var target = args.Target.Value;
        var isMine = user == target;

        _humanoid.SetMarkingVisibility(target, humanoid, mProt.ID, !args.IsVisible);

        if (isMine)
        {
            var selfString = isVisible ? "undies-removed-self" : "undies-equipped-self";
            var selfPopup = Loc.GetString(selfString, ("undie", localizedName));
            _popupSystem.PopupClient(selfPopup, target, target, PopupType.Medium);
        }
        else
        {
            // to the user
            var userString = isVisible ? "undies-removed-user" : "undies-equipped-user";
            var userPopup = Loc.GetString(userString, ("undie", localizedName));
            _popupSystem.PopupClient(userPopup, user, user, PopupType.Medium);

            // to the target
            var targetString = isVisible ? "undies-removed-target" : "undies-equipped-target";
            var targetPopup = Loc.GetString(targetString, ("undie", localizedName), ("user", Identity.Entity(user, _entMan)));
            _popupSystem.PopupClient(targetPopup, target, target, PopupType.MediumCaution);
        }

        // and then play a sound! If they aren't genitals.
        if (mProt.BodyPart != HumanoidVisualLayers.Genital)
            _audio.PlayEntity(ent.Comp.Sound, Filter.Entities(user, target), target, false);
    }

}
