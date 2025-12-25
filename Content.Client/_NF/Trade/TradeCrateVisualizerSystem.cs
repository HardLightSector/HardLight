using Content.Shared._NF.Trade;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._NF.Trade;

/// <summary>
/// Visualizer for trade crates, largely based on Nyano's mail visualizer (thank you)
/// </summary>
public sealed class TradeCrateVisualizerSystem : VisualizerSystem<TradeCrateComponent>
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private const string FallbackIconID = "CargoOther";
    private const string CargoPriorityActiveState = "cargo_priority_active";
    private const string CargoPriorityInactiveState = "cargo_priority_inactive";

    protected override void OnAppearanceChange(EntityUid uid, TradeCrateComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        _appearance.TryGetData(uid, TradeCrateVisuals.DestinationIcon, out string job, args.Component);

        if (string.IsNullOrEmpty(job))
            job = FallbackIconID;

        if (!_proto.TryIndex<TradeCrateDestinationPrototype>(job, out var icon))
            icon = _proto.Index<TradeCrateDestinationPrototype>(FallbackIconID);

        _sprite.LayerSetTexture((uid, args.Sprite), TradeCrateVisualLayers.Icon, _sprite.Frame0(icon.Icon));
        _sprite.LayerSetVisible((uid, args.Sprite), TradeCrateVisualLayers.Icon, true);
        if (_appearance.TryGetData(uid, TradeCrateVisuals.IsPriority, out bool isPriority) && isPriority)
        {
            _sprite.LayerSetVisible((uid, args.Sprite), TradeCrateVisualLayers.Priority, true);
            if (_appearance.TryGetData(uid, TradeCrateVisuals.IsPriorityInactive, out bool inactive) && inactive)
                _sprite.LayerSetRsiState((uid, args.Sprite), TradeCrateVisualLayers.Priority, CargoPriorityInactiveState);
            else
                _sprite.LayerSetRsiState((uid, args.Sprite), TradeCrateVisualLayers.Priority, CargoPriorityActiveState);
        }
        else
            _sprite.LayerSetVisible((uid, args.Sprite), TradeCrateVisualLayers.Priority, false);
    }
}

public enum TradeCrateVisualLayers : byte
{
    Icon,
    Priority
}
