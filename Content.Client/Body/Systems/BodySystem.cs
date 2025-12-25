using Content.Shared.Body.Systems;
// Shitmed Change Start
using Content.Shared._Shitmed.Body.Part;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;
using Content.Shared.Body.Components;
// Shitmed Change End

namespace Content.Client.Body.Systems;

public sealed class BodySystem : SharedBodySystem
{
    // Shitmed Change Start
    [Dependency] private readonly MarkingManager _markingManager = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private void ApplyMarkingToPart(EntityUid uid, MarkingPrototype markingPrototype,
        IReadOnlyList<Color>? colors,
        bool visible,
        SpriteComponent sprite)
    {
        for (var j = 0; j < markingPrototype.Sprites.Count; j++)
        {
            var markingSprite = markingPrototype.Sprites[j];

            if (markingSprite is not SpriteSpecifier.Rsi rsi)
                continue;

            var layerId = $"{markingPrototype.ID}-{rsi.RsiState}";

            if (!_sprite.LayerMapTryGet((uid, sprite), layerId, out _ , false))
            {
                var layer = _sprite.AddLayer((uid, sprite), markingSprite, j + 1);
                _sprite.LayerMapSet((uid, sprite), layerId, layer);
                _sprite.LayerSetSprite((uid, sprite), layerId, rsi);
            }

            _sprite.LayerSetVisible((uid, sprite), layerId, visible);

            if (!visible)
                continue;

            // Okay so if the marking prototype is modified but we load old marking data this may no longer be valid
            // and we need to check the index is correct. So if that happens just default to white?
            if (colors != null && j < colors.Count)
                _sprite.LayerSetColor((uid, sprite), layerId, colors[j]);
            else
                _sprite.LayerSetColor((uid, sprite), layerId, Color.White);
        }
    }

    protected override void ApplyPartMarkings(EntityUid target, BodyPartAppearanceComponent component)
    {
        if (!TryComp(target, out SpriteComponent? sprite))
            return;

        if (component.Color != null)
            _sprite.SetColor((target, sprite), component.Color.Value);

        foreach (var (visualLayer, markingList) in component.Markings)
            foreach (var marking in markingList)
            {
                if (!_markingManager.TryGetMarking(marking, out var markingPrototype))
                    continue;

                ApplyMarkingToPart(target, markingPrototype, marking.MarkingColors, marking.Visible, sprite);
            }
    }

    protected override void RemoveBodyMarkings(EntityUid target, BodyPartAppearanceComponent partAppearance, HumanoidAppearanceComponent bodyAppearance)
    {
        return;
    }
    // Shitmed Change End
}
