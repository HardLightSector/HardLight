using Robust.Shared.GameStates;

namespace Content.Shared._Afterlight.MobInteraction;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedALMobInteractionSystem))]
public sealed partial class ALContentPreferenceComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool MobInteraction;

    [DataField, AutoNetworkedField]
    public bool Vore;

    [DataField, AutoNetworkedField]
    public bool DefaultValue;
}
