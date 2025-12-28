using Content.Shared._NF.CrateMachine;
using Content.Shared._NF.CrateMachine.Components;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Audio.Systems;

namespace Content.Client._NF.CrateMachine;

public sealed class CrateMachineSystem : SharedCrateMachineSystem
{
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly AnimationPlayerSystem _animationSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private const string AnimationKey = "crate_machine_animation";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrateMachineComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<CrateMachineComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnComponentInit(EntityUid uid, CrateMachineComponent crateMachine, ComponentInit args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) || !TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        UpdateState(uid, crateMachine, sprite, appearance);
    }

    /// <summary>
    /// Update visuals and tick animation
    /// </summary>
    private void UpdateState(EntityUid uid, CrateMachineComponent component, SpriteComponent sprite, AppearanceComponent appearance)
    {
        if (!_appearanceSystem.TryGetData<CrateMachineVisualState>(uid, CrateMachineVisuals.VisualState, out var state, appearance))
        {
            return;
        }

        _sprite.LayerSetVisible((uid, sprite), CrateMachineVisualLayers.Base, true);
        _sprite.LayerSetVisible((uid, sprite), CrateMachineVisualLayers.Closed, state == CrateMachineVisualState.Closed);
        _sprite.LayerSetVisible((uid, sprite), CrateMachineVisualLayers.Opening, state == CrateMachineVisualState.Opening);
        _sprite.LayerSetVisible((uid, sprite), CrateMachineVisualLayers.Closing, state == CrateMachineVisualState.Closing);
        _sprite.LayerSetVisible((uid, sprite), CrateMachineVisualLayers.Open, state == CrateMachineVisualState.Open);
        _sprite.LayerSetVisible((uid, sprite), CrateMachineVisualLayers.Crate, state == CrateMachineVisualState.Opening);

        if (state == CrateMachineVisualState.Opening && !_animationSystem.HasRunningAnimation(uid, AnimationKey))
        {
            var openingState = _sprite.LayerMapTryGet((uid, sprite), CrateMachineVisualLayers.Opening, out var flushLayer, false)
                ? _sprite.LayerGetRsiState((uid, sprite), flushLayer)
                : new RSI.StateId(component.OpeningSpriteState);
            var crateState = _sprite.LayerMapTryGet((uid, sprite), CrateMachineVisualLayers.Crate, out var crateFlushLayer, false)
                ? _sprite.LayerGetRsiState((uid, sprite), crateFlushLayer)
                : new RSI.StateId(component.CrateSpriteState);

            // Setup the opening animation to play
            var anim = new Animation
            {
                Length = TimeSpan.FromSeconds(component.OpeningTime),
                AnimationTracks =
                {
                    new AnimationTrackSpriteFlick
                    {
                        LayerKey = CrateMachineVisualLayers.Opening,
                        KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(openingState, 0) },
                    },
                    new AnimationTrackSpriteFlick
                    {
                        LayerKey = CrateMachineVisualLayers.Crate,
                        KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(crateState, 0) },
                    },
                }
            };

            if (component.OpeningSound != null)
            {
                anim.AnimationTracks.Add(
                    new AnimationTrackPlaySound
                    {
                        KeyFrames =
                        {
                            new AnimationTrackPlaySound.KeyFrame(_audioSystem.ResolveSound(component.OpeningSound), 0),
                        }
                    }
                );
            }

            _animationSystem.Play(uid, anim, AnimationKey);
        }
        else if (state == CrateMachineVisualState.Closing && !_animationSystem.HasRunningAnimation(uid, AnimationKey))
        {
            var closingState = _sprite.LayerMapTryGet((uid, sprite), CrateMachineVisualLayers.Closing, out var flushLayer, false)
                ? _sprite.LayerGetRsiState((uid, sprite), flushLayer)
                : new RSI.StateId(component.ClosingSpriteState);
            // Setup the opening animation to play
            var anim = new Animation
            {
                Length = TimeSpan.FromSeconds(component.ClosingTime),
                AnimationTracks =
                {
                    new AnimationTrackSpriteFlick
                    {
                        LayerKey = CrateMachineVisualLayers.Closing,
                        KeyFrames =
                        {
                            // Play the flush animation
                            new AnimationTrackSpriteFlick.KeyFrame(closingState, 0),
                        }
                    },
                }
            };

            if (component.ClosingSound != null)
            {
                anim.AnimationTracks.Add(
                    new AnimationTrackPlaySound
                    {
                        KeyFrames =
                        {
                            new AnimationTrackPlaySound.KeyFrame(_audioSystem.ResolveSound(component.ClosingSound), 0.5f),
                        }
                    }
                );
            }

            _animationSystem.Play(uid, anim, AnimationKey);
        }
    }

    private void OnAppearanceChange(EntityUid uid, CrateMachineComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        UpdateState(uid, component, args.Sprite, args.Component);
    }
}

public enum CrateMachineVisualLayers : byte
{
    Base,
    Opening,
    Open,
    Closing,
    Closed,
    Crate
}
