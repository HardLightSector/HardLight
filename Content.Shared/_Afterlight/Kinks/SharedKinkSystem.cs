using System.Collections.Immutable;
using System.Linq;
using Content.Shared.Database._Afterlight;
using Content.Shared.GameTicking;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Afterlight.Kinks;

public abstract class SharedKinkSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public ImmutableSortedDictionary<EntityPrototype, ImmutableArray<EntityPrototype>> AllKinks { get; private set; } =
        ImmutableSortedDictionary<EntityPrototype, ImmutableArray<EntityPrototype>>.Empty;

    public ImmutableDictionary<string, EntityPrototype> FlistImports { get; private set; } =
        ImmutableDictionary<string, EntityPrototype>.Empty;

    public ImmutableDictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference>? LocalKinks { get; private set; }

    public override void Initialize()
    {
        SubscribeNetworkEvent<KinksUpdatedEvent>(OnNetworkKinksUpdated);

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);

        SubscribeLocalEvent<KinksComponent, GetVerbsEvent<ExamineVerb>>(OnExamineVerbs);
        SubscribeLocalEvent<KinksComponent, PlayerDetachedEvent>(OnPlayerDetached);

        ReloadPrototypes();
    }

    protected virtual void OnNetworkKinksUpdated(KinksUpdatedEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (_player.LocalSession?.UserId == player.UserId)
        {
            LocalKinks = msg.Kinks.ToImmutableDictionary();
            RaiseLocalEvent(msg);
        }

        if (player.AttachedEntity is { } ent)
            UpdateKinks(player, ent);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        if (ev.WasModified<EntityPrototype>())
            ReloadPrototypes();
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        UpdateKinks(ev.Player, ev.Mob);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        UpdateKinks(args.Player, args.Entity);
    }

    private void OnExamineVerbs(Entity<KinksComponent> ent, ref GetVerbsEvent<ExamineVerb> args)
    {
        args.Verbs.Add(new ExamineVerb
        {
            Text = Loc.GetString("al-kinks-show"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/examine-star.png")),
            Act = () => OpenKinksWindowFor(ent)
        });
    }

    private void OnPlayerDetached(Entity<KinksComponent> ent, ref PlayerDetachedEvent args)
    {
        RemCompDeferred<KinksComponent>(ent);
    }

    protected virtual Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference>? GetKinks(NetUserId player)
    {
        return _player.LocalSession?.UserId == player ? LocalKinks?.ToDictionary() ?? new() : null;
    }

    public bool IsEnabled(Entity<KinksComponent?> player, EntProtoId<KinkDefinitionComponent> kink)
    {
        if (!Resolve(player, ref player.Comp, false))
            return false;

        return player.Comp.Settings.TryGetValue(kink, out var preference) &&
               preference is KinkPreference.Yes or KinkPreference.Favourite;
    }

    protected void UpdateKinks(ICommonSession player, EntityUid mob)
    {
        if (GetKinks(player.UserId) is not { } settings)
            return;

        var kinks = EnsureComp<KinksComponent>(mob);
        SetSettings((mob, kinks), settings);
    }

    private void SetSettings(Entity<KinksComponent> kinks, Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> settings)
    {
        // TODO AFTERLIGHT immutable dictionary when https://github.com/space-wizards/netserializer/pull/5 is merged
        kinks.Comp.Settings = new Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference>(settings);
        Dirty(kinks);
    }

    private void ReloadPrototypes()
    {
        var categoryIds = ImmutableDictionary.CreateBuilder<EntProtoId<KinkCategoryComponent>, ImmutableArray<EntityPrototype>.Builder>();
        var flistImports = ImmutableDictionary.CreateBuilder<string, EntityPrototype>();
        foreach (var entity in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (!entity.TryGetComponent(out KinkDefinitionComponent? kink, _compFactory))
                continue;

            if (kink.Category is not { } category)
                continue;

            if (!categoryIds.TryGetValue(category, out var kinks))
            {
                kinks = ImmutableArray.CreateBuilder<EntityPrototype>();
                categoryIds[category] = kinks;
            }

            if (kink.FListImport is { } import &&
                !flistImports.TryAdd(import, entity))
            {
                Log.Error($"Error loading prototype {entity.ID}, a kink with {nameof(KinkDefinitionComponent.FListImport)} {import} already exists.");
            }

            kinks.Add(entity);
        }

        var comparer =
            Comparer<EntityPrototype>.Create((a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var categories =
            ImmutableSortedDictionary.CreateBuilder<EntityPrototype, ImmutableArray<EntityPrototype>>(comparer);
        foreach (var (categoryId, kinks) in categoryIds)
        {
            if (_prototypes.TryIndex(categoryId, out var category))
            {
                kinks.Sort(comparer);
                categories[category] = kinks.ToImmutable();
            }
        }

        AllKinks = categories.ToImmutable();
        FlistImports = flistImports.ToImmutable();
    }

    private void OpenKinksWindowFor(Entity<KinksComponent> kinks)
    {
        RaiseLocalEvent(new OpenKinksWindowEvent(GetNetEntity(kinks)));
    }
}
