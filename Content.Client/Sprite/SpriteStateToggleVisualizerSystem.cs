using Content.Shared.Sprite;
using Content.Shared.Toggleable;
using Content.Shared.Movement.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client.Sprite;

public sealed class SpriteStateToggleVisualizerSystem : VisualizerSystem<SpriteStateToggleComponent>
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    public override void Initialize()
    {
        base.Initialize();
    }

    protected override void OnAppearanceChange(EntityUid uid, SpriteStateToggleComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || string.IsNullOrEmpty(component.SpriteLayer))
            return;

        if (!args.Sprite.LayerMapTryGet(component.SpriteLayer!, out var layerIndex))
            return;

    var enabled = _appearance.TryGetData<bool>(uid, SpriteStateToggleVisuals.Toggled, out var value, args.Component) && value;

        // If there's a movement component, prefer the moving or idle variant based on IsMoving.
        var moving = TryComp<SpriteMovementComponent>(uid, out var move) && move.IsMoving;

        string? desiredState = null;
        if (moving)
            desiredState = enabled ? component.MovementStateOn ?? component.StateOn : component.MovementStateOff ?? component.StateOff;
        else
            desiredState = enabled ? component.StateOn : component.StateOff;

        if (!string.IsNullOrEmpty(desiredState))
            args.Sprite.LayerSetState(layerIndex, desiredState!);
    }

}
