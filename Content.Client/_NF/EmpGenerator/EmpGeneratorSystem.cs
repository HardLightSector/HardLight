using Content.Shared._NF.EmpGenerator;
using Content.Shared.Power;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client._NF.EmpGenerator;

public sealed partial class EmpGeneratorSystem : EntitySystem
{
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EmpGeneratorVisualsComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    /// <summary>
    /// Ensures that the visible state of mobile emps are synced with their sprites.
    /// </summary>
    private void OnAppearanceChange(EntityUid uid, EmpGeneratorVisualsComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (_appearanceSystem.TryGetData<PowerChargeStatus>(uid, PowerChargeVisuals.State, out var state, args.Component))
        {
            if (comp.SpriteMap.TryGetValue(state, out var spriteState))
            {
                var layer = _sprite.LayerMapGet((uid, args.Sprite), EmpGeneratorVisualLayers.Base);
                _sprite.LayerSetRsiState((uid, args.Sprite), layer, new RSI.StateId(spriteState));
            }
        }

        if (_appearanceSystem.TryGetData<float>(uid, PowerChargeVisuals.Charge, out var charge, args.Component))
        {
            var layer = _sprite.LayerMapGet((uid, args.Sprite), EmpGeneratorVisualLayers.Core);
            foreach (var threshold in comp.Thresholds)
            {
                if (charge < threshold.MaxCharge)
                {
                    _sprite.LayerSetVisible((uid, args.Sprite), layer, threshold.Visible);
                    if (threshold.State != null)
                        _sprite.LayerSetRsiState((uid, args.Sprite), layer, new RSI.StateId(threshold.State));
                    break;
                }
            }
        }
    }
}

public enum EmpGeneratorVisualLayers : byte
{
    Base,
    Core
}
