using Content.Client._Afterlight.MobInteraction;
using Content.Shared._Afterlight.MobInteraction;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Afterlight.Vore;

public sealed class VoreOverlay : Overlay
{
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private readonly ALMobInteractionSystem _alMobInteraction;
    private readonly SpriteSystem _sprite;
    private readonly VoreSystem _vore;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public VoreOverlay()
    {
        IoCManager.InjectDependencies(this);
        _vore = _entity.System<VoreSystem>();
        _alMobInteraction = _entity.System<ALMobInteractionSystem>();
        _sprite = _entity.System<SpriteSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.DrawingHandle is not DrawingHandleScreen handle)
            return;

        if (!_vore.IsTestingOverlay(out var space))
        {
            if (!_alMobInteraction.LocalPreferences.Contains(ALContentPref.VoreOverlays) ||
                _player.LocalEntity is not { } ent ||
                !_vore.IsVored(ent, out _, out space))
            {
                return;
            }
        }

        if (space.Overlay is not { } overlayId ||
            !overlayId.TryGet(out var overlay, _prototype, _compFactory) ||
            overlay.Overlay is not { } overlayTexture)
        {
            return;
        }

        var texture = _sprite.Frame0(overlayTexture);
        var rect = args.ViewportBounds;
         handle.DrawTextureRectRegion(texture, rect, modulate: space.OverlayColor);
    }
}
