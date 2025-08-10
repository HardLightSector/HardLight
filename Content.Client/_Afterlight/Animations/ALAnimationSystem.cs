using Content.Shared._Afterlight.Animations;
using Robust.Client.Animations;
using Robust.Client.GameObjects;

namespace Content.Client._Afterlight.Animations;

public sealed class ALAnimationSystem : SharedALAnimationSystem
{
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private const string FlickId = "al_flick_animation";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ALPlayAnimationEvent>(OnPlayAnimation);
        SubscribeNetworkEvent<ALFlickEvent>(OnFlick);
    }

    private void OnPlayAnimation(ALPlayAnimationEvent ev)
    {
        if (GetEntity(ev.Entity) is not { Valid: true } ent)
            return;

        if (_animation.HasRunningAnimation(ent, ev.Animation.Id))
            return;

        if (!TryComp(ent, out ALAnimationComponent? animationComp) ||
            !animationComp.Animations.TryGetValue(ev.Animation, out var alAnimation))
        {
            return;
        }

        var animationTracks = new List<AnimationTrack>();
        foreach (var track in alAnimation.AnimationTracks)
        {
            var keyFrames = new List<AnimationTrackSpriteFlick.KeyFrame>();
            foreach (var keyFrame in track.KeyFrames)
            {
                keyFrames.Add(new AnimationTrackSpriteFlick.KeyFrame(keyFrame.State, keyFrame.KeyTime));
            }

            var spriteFlick = new AnimationTrackSpriteFlick { LayerKey = track.LayerKey };
            spriteFlick.KeyFrames.AddRange(keyFrames);
            animationTracks.Add(spriteFlick);
        }

        var animation = new Animation { Length = alAnimation.Length };
        animation.AnimationTracks.AddRange(animationTracks);
        _animation.Play(ent, animation, ev.Animation.Id);
    }

    private void OnFlick(ALFlickEvent ev)
    {
        if (GetEntity(ev.Entity) is not { Valid: true } ent)
            return;

        if (_animation.HasRunningAnimation(ent, FlickId))
            return;

        if (ev.Layer == null)
            _sprite.LayerMapSet(ent, FlickId, 0);

        var animationState = _sprite.GetState(ev.AnimationState);
        var length = animationState.AnimationLength;
        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new ALAnimationTrackSpriteFlick
                {
                    LayerKey = FlickId,
                    KeyFrames = new List<ALAnimationTrackSpriteFlick.KeyFrame>
                    {
                        new(ev.AnimationState, 0),
                        new(ev.DefaultState, length),
                    },
                },
            },
        };

        _animation.Play(ent, animation, FlickId);
    }
}
