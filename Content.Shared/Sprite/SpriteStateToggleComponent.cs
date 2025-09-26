using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Sprite;

/// <summary>
/// Attach to an entity to allow toggling a specific sprite layer between two RSI states via an action.
/// Visuals are driven on the client via Appearance using ToggleVisuals.Toggled.
///
/// Example usage on an entity prototype:
///
///  - type: entity
///    id: ExampleSpriteToggleItem
///    components:
///    - type: Sprite
///      sprite: Objects/Example/example_item.rsi
///      layers:
///      - map: [ "toggle" ] # layer key referenced below
///        state: off
///    - type: SpriteStateToggle
///      spriteLayer: toggle
///      stateOn: on
///      stateOff: off
///      enabled: false
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SpriteStateToggleComponent : Component
{
    /// <summary>
    /// The mapped sprite layer key to change (SpriteComponent.LayerMap).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? SpriteLayer;

    /// <summary>
    /// RSI state when enabled (toggled on).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? StateOn;

    /// <summary>
    /// RSI state when disabled (toggled off).
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? StateOff;

    /// <summary>
    /// Optional movement layer key to change when moving. Defaults to "movement" used by SpriteMovement.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string MovementLayer = "movement";

    /// <summary>
    /// RSI state when enabled and moving.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? MovementStateOn;

    /// <summary>
    /// RSI state when disabled and moving.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? MovementStateOff;

    /// <summary>
    /// Whether the component is currently enabled (state on) or not (state off).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled;

    /// <summary>
    /// Action prototype granted to toggle this component.
    /// </summary>
    [DataField]
    public EntProtoId ToggleAction = "ActionToggleSpriteState";

    /// <summary>
    /// Instantiated action entity for toggling.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ToggleActionEntity;
}
