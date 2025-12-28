using Content.Shared.CCVar;
using Content.Shared.Drugs;
using Content.Shared.StatusEffect;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Drugs;

public sealed class AbyssalOverlay : Overlay
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntitySystemManager _sysMan = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;
    private static readonly ProtoId<ShaderPrototype> AbyssalShaderId = "AbyssalWhispers";
    private readonly ShaderInstance _abyssalShader;

    public float Intoxication = 0.0f;
    public float TimeTicker = 0.0f;

    private const float VisualThreshold = 10.0f;
    private const float PowerDivisor = 500.0f; // Higher divisor = more subtle effect

    private float EffectScale => Math.Clamp((Intoxication - VisualThreshold) / PowerDivisor, 0.0f, 0.15f); // Max 15% opacity

    public AbyssalOverlay()
    {
        IoCManager.InjectDependencies(this);
        _abyssalShader = _prototypeManager.Index(AbyssalShaderId).InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var playerEntity = _playerManager.LocalEntity;

        if (playerEntity == null)
            return;

        if (!_entityManager.HasComponent<AbyssalWhispersComponent>(playerEntity)
            || !_entityManager.TryGetComponent<StatusEffectsComponent>(playerEntity, out var status))
            return;

        var statusSys = _sysMan.GetEntitySystem<StatusEffectsSystem>();
        if (!statusSys.TryGetTime(playerEntity.Value, DrugOverlaySystem.AbyssalKey, out var time, status))
            return;

        var timeLeft = (float) (time.Value.Item2 - time.Value.Item1).TotalSeconds;

        TimeTicker += args.DeltaSeconds;

        if (timeLeft - TimeTicker > timeLeft / 16f)
        {
            Intoxication += (timeLeft - Intoxication) * args.DeltaSeconds / 16f;
        }
        else
        {
            Intoxication -= Intoxication/(timeLeft - TimeTicker) * args.DeltaSeconds;
        }
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return false;

        if (args.Viewport.Eye != eyeComp.Eye)
            return false;

        return EffectScale > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Skip if reduced motion is enabled - this effect is purely atmospheric
        if (_config.GetCVar(CCVars.ReducedMotion))
            return;

        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _abyssalShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _abyssalShader.SetParameter("effectScale", EffectScale);
        handle.UseShader(_abyssalShader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
