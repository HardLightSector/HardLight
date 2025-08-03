using Content.Shared._Afterlight.Kinks;
using Content.Shared.Database._Afterlight;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Prototypes;

namespace Content.Client._Afterlight.Kinks;

public sealed class KinkSystem : SharedKinkSystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IResourceCache _resource = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private EntityQuery<SpriteComponent> _spriteQuery;

    public override void Initialize()
    {
        base.Initialize();

        _spriteQuery = GetEntityQuery<SpriteComponent>();

        SubscribeLocalEvent<KinksUpdatedEvent>(OnKinksUpdated);

        SubscribeLocalEvent<KinkAlternateSpriteComponent, ComponentStartup>(OnStartup);
    }

    private void OnKinksUpdated(KinksUpdatedEvent ev)
    {
        if (_player.LocalEntity is not { } playerEnt ||
            !TryComp(playerEnt, out KinksComponent? kinks))
        {
            return;
        }

        var player = new Entity<KinksComponent?>(playerEnt, kinks);
        var query = EntityQueryEnumerator<KinkAlternateSpriteComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            UpdateSprite((uid, comp), player);
        }
    }

    private void OnStartup(Entity<KinkAlternateSpriteComponent> ent, ref ComponentStartup args)
    {
        UpdateSprite(ent);
    }

    private void UpdateSprite(Entity<KinkAlternateSpriteComponent> ent, Entity<KinksComponent?>? player = null)
    {
        player ??= _player.LocalEntity;
        if (player == null)
            return;

        if (!_spriteQuery.TryComp(ent, out var sprite))
            return;

        var newSprite = ent.Comp.OffSprite;
        if (ent.Comp.Kink != null && IsEnabled(player.Value, ent.Comp.Kink.Value))
            newSprite = ent.Comp.OnSprite;

        if (newSprite == null)
            return;

        if (!_resource.TryGetResource<RSIResource>(SpriteSystem.TextureRoot / newSprite.RsiPath, out var rsi))
        {
            Log.Error($"Unable to load RSI '{newSprite.RsiPath}' for entity {ToPrettyString(ent)}. Trace:\n{Environment.StackTrace}");
            return;
        }

        var entSprite = new Entity<SpriteComponent?>(ent, sprite);
        _sprite.SetBaseRsi(entSprite, rsi.RSI);

        if (!_sprite.LayerExists(entSprite, 0))
            _sprite.AddLayer(entSprite, newSprite);

        _sprite.LayerSetRsiState(entSprite, 0, newSprite.RsiState);
        _sprite.LayerSetAnimationTime(entSprite, 0, 0);
    }

    public void ClientSetPreference(EntProtoId<KinkDefinitionComponent> kink, KinkPreference? preference)
    {
        var ev = new UpdateSingleKinkClientEvent(kink, preference);
        RaiseNetworkEvent(ev);
    }

    public void ClientSetPreferences(Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> kinks)
    {
        var ev = new UpdateKinksClientEvent(kinks);
        RaiseNetworkEvent(ev);
    }

    public void ClientSetPreferences(List<EntProtoId<KinkDefinitionComponent>> kinks, KinkPreference preference)
    {
        var ev = new UpdateKinksSinglePreferenceClientEvent(kinks, preference);
        RaiseNetworkEvent(ev);
    }

    public void ClientImportFlist(string link)
    {
        var ev = new KinkImportFlistClientEvent(link);
        RaiseNetworkEvent(ev);
    }
}
