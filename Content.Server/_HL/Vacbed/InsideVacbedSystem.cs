using Content.Server.Speech.Components;
using Content.Shared._HL.Vacbed;
using Robust.Shared.Containers;

namespace Content.Server._HL.Vacbed;

public sealed partial class VacbedSystem
{

    public override void InsideVacbedInit(EntityUid uid, InsideVacbedComponent insideVacbedComponent, ComponentInit args)
    {
        base.InsideVacbedInit(uid, insideVacbedComponent, args);

        if (HasComp<MumbleAccentComponent>(insideVacbedComponent.Owner))
            insideVacbedComponent.IsMuzzled = true;

        EnsureComp<MumbleAccentComponent>(insideVacbedComponent.Owner);
    }

    public override void OnEntGotRemovedFromContainer(EntityUid uid, InsideVacbedComponent component, EntGotRemovedFromContainerMessage args)
    {
        base.OnEntGotRemovedFromContainer(uid, component, args);

        if(!component.IsMuzzled)
            RemComp<MumbleAccentComponent>(uid);
    }
}
