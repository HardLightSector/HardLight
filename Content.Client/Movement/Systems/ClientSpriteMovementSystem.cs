using Content.Shared.Movement.Components;
using Content.Shared.Sprite;
using Content.Shared.Movement.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client.Movement.Systems;

/// <summary>
/// Controls the switching of motion and standing still animation
/// </summary>
public sealed class ClientSpriteMovementSystem : SharedSpriteMovementSystem
{
    private EntityQuery<SpriteComponent> _spriteQuery;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        _spriteQuery = GetEntityQuery<SpriteComponent>();

        SubscribeLocalEvent<SpriteMovementComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
    }

    private void OnAfterAutoHandleState(Entity<SpriteMovementComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!_spriteQuery.TryGetComponent(ent, out var sprite))
            return;

        if (ent.Comp.IsMoving)
        {
            foreach (var (layer, state) in ent.Comp.MovementLayers)
            {
                sprite.LayerSetData(layer, state);
            }
        }
        else
        {
            foreach (var (layer, state) in ent.Comp.NoMovementLayers)
            {
                sprite.LayerSetData(layer, state);
            }
        }

        // If the entity has a SpriteStateToggle, re-apply its desired state to the configured layer so it persists.
        if (!TryComp<SpriteStateToggleComponent>(ent, out var toggle))
            return;
        if (string.IsNullOrEmpty(toggle.SpriteLayer) || !sprite.LayerMapTryGet(toggle.SpriteLayer!, out var layerIndex))
            return;

        // Read toggle from appearance; if not available yet, don't override the layer to avoid brief reversion.
        if (!_appearance.TryGetData<bool>(ent, SpriteStateToggleVisuals.Toggled, out var value))
            return;
        var enabled = value;

        var moving = ent.Comp.IsMoving;
        string? desiredState = null;
        if (moving)
            desiredState = enabled ? toggle.MovementStateOn ?? toggle.StateOn : toggle.MovementStateOff ?? toggle.StateOff;
        else
            desiredState = enabled ? toggle.StateOn : toggle.StateOff;

        if (!string.IsNullOrEmpty(desiredState))
            sprite.LayerSetState(layerIndex, desiredState!);
    }
}
