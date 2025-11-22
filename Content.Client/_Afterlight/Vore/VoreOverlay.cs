using Content.Client._Afterlight.MobInteraction;
using Content.Shared._Afterlight.MobInteraction;
using Content.Shared.Database._Afterlight;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._Afterlight.Vore;

public sealed class VoreOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private readonly VoreSystem _vore;
    private readonly ALMobInteractionSystem _alMobInteraction;
    private readonly SpriteSystem _sprite;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public VoreOverlay()
    {
        _vore = _entity.System<VoreSystem>();
        _alMobInteraction = _entity.System<ALMobInteractionSystem>();
        _sprite = _entity.System<SpriteSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.DrawingHandle is not DrawingHandleWorld handle)
            return;

        return;
        if (//!_alMobInteraction.LocalPreferences.Contains(ALContentPref.VoreOverlays) ||
            _player.LocalEntity is not { } ent ||
            !_vore.IsVored(ent, out _, out var space) ||
            space.Overlay is not { } overlayId ||
            !_vore.Overlays.TryGetValue(overlayId, out var overlay) ||
            overlay.Overlay is not { } overlayTexture)
        {
            return;
        }

        var texture = _sprite.Frame0(overlayTexture);
        var rect = args.WorldAABB;
        handle.DrawTextureRectRegion(texture, rect, Color.White);
    }
}
