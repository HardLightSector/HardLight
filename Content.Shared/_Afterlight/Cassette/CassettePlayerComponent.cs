using Content.Shared.Inventory;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Afterlight.Cassette;

// Taken from https://github.com/RMC-14/RMC-14
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedCassetteSystem))]
public sealed partial class CassettePlayerComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId PlayPauseActionId = "ALActionCassettePlayPause";

    [DataField, AutoNetworkedField]
    public EntityUid? PlayPauseAction;

    [DataField, AutoNetworkedField]
    public EntProtoId NextActionId = "ALActionCassetteNext";

    [DataField, AutoNetworkedField]
    public EntityUid? NextAction;

    [DataField, AutoNetworkedField]
    public EntProtoId RestartActionId = "ALActionCassetteRestart";

    [DataField, AutoNetworkedField]
    public EntityUid? RestartAction;

    [DataField, AutoNetworkedField]
    public SlotFlags Slots = SlotFlags.NECK;

    [DataField, AutoNetworkedField]
    public string ContainerId = "al_cassette_player";

    [DataField, AutoNetworkedField]
    public EntityUid? AudioStream;

    [DataField]
    public EntityUid? CustomAudioStream;

    [DataField, AutoNetworkedField]
    public AudioState State;

    [DataField, AutoNetworkedField]
    public AudioParams AudioParams = AudioParams.Default.WithVolume(-6f);

    [DataField, AutoNetworkedField]
    public int Tape;

    [DataField, AutoNetworkedField]
    public SoundSpecifier PlayPauseSound = new SoundPathSpecifier("/Audio/_Afterlight/Machines/click.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier InsertEjectSound = new SoundPathSpecifier("/Audio/_Afterlight/Weapons/handcuffs.ogg");

    [DataField, AutoNetworkedField]
    public SpriteSpecifier.Rsi WornSprite = new(new ResPath("_Afterlight/Objects/Devices/cassette_player.rsi"), "mob_overlay");

    [DataField, AutoNetworkedField]
    public SpriteSpecifier.Rsi MusicSprite = new(new ResPath("_Afterlight/Objects/Devices/cassette_player.rsi"), "music");
}
