using Content.Shared.Actions;
using Content.Shared.Sprite;
using Robust.Shared.GameObjects;

namespace Content.Shared.Sprite.EntitySystems;

public sealed class SpriteStateToggleSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpriteStateToggleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SpriteStateToggleComponent, SpriteStateToggleActionEvent>(OnToggleAction);
    }

    private void OnMapInit(EntityUid uid, SpriteStateToggleComponent component, MapInitEvent args)
    {
        // Ensure the action entity exists in our action container and grant it directly to this entity,
        // so the action shows on the entity's action hotbar (not only when held).
        _actionContainer.EnsureAction(uid, ref component.ToggleActionEntity, component.ToggleAction);
        _actions.AddAction(uid, ref component.ToggleActionEntity, component.ToggleAction);
        _appearance.SetData(uid, SpriteStateToggleVisuals.Toggled, component.Enabled);
        _actions.SetToggled(component.ToggleActionEntity, component.Enabled);
        Dirty(uid, component);
    }

    private void OnToggleAction(EntityUid uid, SpriteStateToggleComponent component, SpriteStateToggleActionEvent args)
    {
        if (args.Handled)
            return;

        component.Enabled = !component.Enabled;
        Dirty(uid, component);

        _appearance.SetData(uid, SpriteStateToggleVisuals.Toggled, component.Enabled);
        _actions.SetToggled(component.ToggleActionEntity, component.Enabled);

        args.Handled = true;
    }
}
