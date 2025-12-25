using Content.Client._NF.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Rounding;
using Robust.Client.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Client.Graphics;

namespace Content.Client._NF.Charges.Systems;

// Limited charge visualizer - essentially a copy of the magazine visuals.
public sealed partial class LimitedChargesVisualizerSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LimitedChargesVisualsComponent, ComponentInit>(OnChargeVisualsInit);
        SubscribeLocalEvent<LimitedChargesVisualsComponent, AppearanceChangeEvent>(OnMagazineVisualsChange);
    }

    private void OnChargeVisualsInit(EntityUid uid, LimitedChargesVisualsComponent component, ComponentInit args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite)) return;

        if (_sprite.LayerMapTryGet((uid, sprite), LimitedChargesVisualLayers.Charges, out var chargeLayer, false))
        {
            _sprite.LayerSetRsiState((uid, sprite), chargeLayer, new RSI.StateId($"{component.ChargePrefix}-{component.ChargeSteps - 1}"));
            _sprite.LayerSetVisible((uid, sprite), chargeLayer, false);
        }
    }

    private void OnMagazineVisualsChange(EntityUid uid, LimitedChargesVisualsComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;

        if (sprite == null) return;

        if (!args.AppearanceData.TryGetValue(LimitedChargeVisuals.MaxCharges, out var capacity))
            capacity = component.ChargeSteps;

        if (!args.AppearanceData.TryGetValue(LimitedChargeVisuals.Charges, out var current))
            current = component.ChargeSteps;

        var step = ContentHelpers.RoundToLevels((int)current, (int)capacity, component.ChargeSteps);

        int chargeLayer;
        if (step == 0 && !component.ZeroVisible)
        {
            if (_sprite.LayerMapTryGet((uid, sprite), LimitedChargesVisualLayers.Charges, out chargeLayer, false))
                _sprite.LayerSetVisible((uid, sprite), chargeLayer, false);
        }
        else if (_sprite.LayerMapTryGet((uid, sprite), LimitedChargesVisualLayers.Charges, out chargeLayer, false))
        {
            _sprite.LayerSetVisible((uid, sprite), chargeLayer, true);
            _sprite.LayerSetRsiState((uid, sprite), chargeLayer, new RSI.StateId($"{component.ChargePrefix}-{step}"));
        }
    }
}
