using Content.Shared.Humanoid;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._HL.Markings;

/// <summary>
/// Component that allows removing and adding markings to humanoid entities through a verb.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModifyMarkingsComponent : Component
{
    /// <summary>
    ///     The bodypart target enums for the undies.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<HumanoidVisualLayers> BodyPartTargets = new()
    {
        HumanoidVisualLayers.UndergarmentTop,
        HumanoidVisualLayers.UndergarmentBottom,
        HumanoidVisualLayers.Genital
    };

    /// <summary>
    ///     The sound played when markings is removed or added.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg")
    {
        Params = AudioParams.Default.WithVolume(0.5f).WithVariation(0.5f),
    };
}
