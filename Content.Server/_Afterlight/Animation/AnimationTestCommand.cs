using Content.Server.Administration;
using Content.Shared._Afterlight.Animations;
using Content.Shared.Administration;
using Robust.Shared.Toolshed;
using Robust.Shared.Utility;

namespace Content.Server._Afterlight.Animation;

[ToolshedCommand, AdminCommand(AdminFlags.Debug)]
public sealed class AnimationTestCommand : ToolshedCommand
{
    [CommandImplementation("setlayerstate")]
    public void SetLayerState([PipedArgument] EntityUid ent, [CommandArgument] string layer, [CommandArgument] string state)
    {
        const string key = "al_toolshed_animation_test";
        var comp = EnsureComp<ALAnimationComponent>(ent);
#pragma warning disable RA0002
        comp.Animations[new ALAnimationId(key)] = new ALAnimation(TimeSpan.FromSeconds(3),
#pragma warning restore RA0002
        [
            new ALAnimationTrack(layer,
            [
                new ALKeyFrame(state, 0),
            ]),
        ]);

        EntityManager.Dirty(ent, comp);
        Sys<ALAnimationSystem>().Play((ent, comp), key);
    }

    [CommandImplementation("flick")]
    public void Flick([PipedArgument] EntityUid ent,
        [CommandArgument] string animationRsiPath,
        [CommandArgument] string animationState,
        [CommandArgument] string defaultRsiPath,
        [CommandArgument] string defaultState)
    {
        var animationRsi = new SpriteSpecifier.Rsi(new ResPath(animationRsiPath), animationState);
        var defaultRsi = new SpriteSpecifier.Rsi(new ResPath(defaultRsiPath), defaultState);
        Sys<ALAnimationSystem>().Flick(ent, animationRsi, defaultRsi);
    }
}
