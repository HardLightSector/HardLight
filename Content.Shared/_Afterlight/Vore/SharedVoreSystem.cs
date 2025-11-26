using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Content.Shared._Afterlight.Atmos;
using Content.Shared._Afterlight.Body;
using Content.Shared._Afterlight.CCVar;
using Content.Shared._Afterlight.MobInteraction;
using Content.Shared._Afterlight.Prototypes;
using Content.Shared._Afterlight.UserInterface;
using Content.Shared.Actions;
using Content.Shared.Database._Afterlight;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Afterlight.Vore;

public abstract class SharedVoreSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedALBarotraumaSystem _alBarotrauma = default!;
    [Dependency] private readonly SharedALRespiratorSystem _alRespirator = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedALMobInteractionSystem _alMobInteraction = default!;
    [Dependency] private readonly ALPrototypeSystem _alPrototype = default!;
    [Dependency] private readonly ALUserInterfaceSystem _alUi = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    private static readonly EntProtoId<VoreSoundCollectionComponent> InsertionSoundsId = "ALVoreSoundsInsert";
    private static readonly EntProtoId<VoreSoundCollectionComponent> ReleaseSoundsId = "ALVoreSoundsRelease";
    private static readonly VerbCategory VoreCategory = new("al-vore-verb-category", null);
    private const string VoreContainerPrefix = "al_vore_space_";

    private int _spaceLimit;
    private int _nameCharacterLimit;
    private int _descriptionCharacterLimit;

    private FixedPoint2 _damageBurnMin;
    private FixedPoint2 _damageBurnMax;
    private FixedPoint2 _damageBurnDefault;

    private FixedPoint2 _damageBruteMin;
    private FixedPoint2 _damageBruteMax;
    private FixedPoint2 _damageBruteDefault;

    private bool _muffleRadioDefault;
    private int _chanceToEscapeDefault;

    private TimeSpan _timeToEscapeMin;
    private TimeSpan _timeToEscapeMax;
    private TimeSpan _timeToEscapeDefault;

    private bool _canTasteDefault;

    private string? _insertionVerbDefault;
    private int _insertionVerbCharacterLimit;

    private string? _releaseVerbDefault;
    private int _releaseVerbCharacterLimit;

    private bool _fleshySpaceDefault;
    private bool _internalSoundLoopDefault;

    private string? _insertionSoundDefault;
    private string? _releaseSoundDefault;

    private int _messageCountLimit;
    private int _messageCharacterLimit;

    public ImmutableDictionary<EntProtoId<VoreOverlayComponent>, VoreOverlayComponent> Overlays { get; private set; } =
        ImmutableDictionary<EntProtoId<VoreOverlayComponent>, VoreOverlayComponent>.Empty;

    public ImmutableArray<(EntityPrototype Entity, VoreMessageCollectionComponent Comp)> Messages { get; private set; }

    public ImmutableArray<(string Name, SoundPathSpecifier? Sound)> InsertionSounds { get; private set; }

    public ImmutableArray<(string Name, SoundPathSpecifier? Sound)> ReleaseSounds { get; private set; }

    private readonly Dictionary<Guid, VorePrompt> _prompts = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        SubscribeNetworkEvent<VorePromptAcceptEvent>(OnPromptAccept);
        SubscribeNetworkEvent<VorePromptDeclineEvent>(OnPromptDecline);

        SubscribeLocalEvent<CanBeVorePredatorComponent, MapInitEvent>(OnCanBePredatorUpdated);
        SubscribeLocalEvent<CanBeVorePredatorComponent, PlayerAttachedEvent>(OnCanBePredatorUpdated);
        SubscribeLocalEvent<CanBeVorePredatorComponent, ALContentPreferencesChangedEvent>(OnCanBePredatorUpdated);

        SubscribeLocalEvent<CanBeVorePreyComponent, MapInitEvent>(OnCanBePreyUpdated);
        SubscribeLocalEvent<CanBeVorePreyComponent, PlayerAttachedEvent>(OnCanBePreyUpdated);
        SubscribeLocalEvent<CanBeVorePreyComponent, ALContentPreferencesChangedEvent>(OnCanBePreyUpdated);

        SubscribeLocalEvent<VoreActionComponent, MapInitEvent>(OnActionMapInit);
        SubscribeLocalEvent<VoreActionComponent, ComponentRemove>(OnActionRemove);

        SubscribeLocalEvent<VorePredatorComponent, MapInitEvent>(OnPredatorMapInit);
        SubscribeLocalEvent<VorePredatorComponent, ComponentRemove>(OnPredatorRemove);
        SubscribeLocalEvent<VorePredatorComponent, VoreActionEvent>(OnPredatorActionEvent);
        SubscribeLocalEvent<VorePredatorComponent, GetVerbsEvent<Verb>>(OnPredatorVerbs);
        SubscribeLocalEvent<VorePredatorComponent, PlayerAttachedEvent>(OnPredatorAttached);
        SubscribeLocalEvent<VorePredatorComponent, PlayerDetachedEvent>(OnPredatorDetached);
        SubscribeLocalEvent<VorePredatorComponent, VoreIngestDoAfterEvent>(OnPredatorIngestDoAfter);

        SubscribeLocalEvent<VorePreyComponent, ComponentRemove>(OnPreyRemove);
        SubscribeLocalEvent<VorePreyComponent, GetVerbsEvent<Verb>>(OnPreyVerbs);
        SubscribeLocalEvent<VorePreyComponent, PlayerAttachedEvent>(OnPreyAttached);
        SubscribeLocalEvent<VorePreyComponent, PlayerDetachedEvent>(OnPreyDetached);
        SubscribeLocalEvent<VorePreyComponent, MoveInputEvent>(OnPreyMoveInput);
        SubscribeLocalEvent<VorePreyComponent, VoreEscapeDoAfterEvent>(OnPreyEscapeDoAfter);
        SubscribeLocalEvent<VorePreyComponent, EntGotInsertedIntoContainerMessage>(OnPreyGotInserted);
        SubscribeLocalEvent<VorePreyComponent, EntGotRemovedFromContainerMessage>(OnPreyGotRemoved);

        SubscribeLocalEvent<ActiveVorePreyComponent, ALPressureDamageAttemptEvent>(OnActivePreyPressureDamageAttempt);

        Subs.BuiEvents<VorePredatorComponent>(VoreUi.Key, subs =>
        {
            subs.Event<VoreAddSpaceBuiMsg>(OnAddSpaceMsg);
            subs.Event<VoreChangeMessageBuiMsg>(OnChangeMessageMsg);
            subs.Event<VoreSetSpaceSettingsBuiMsg>(OnSetSettingsMsg);
            subs.Event<VoreDeleteSpaceBuiMsg>(OnDeleteSpaceMsg);
            subs.Event<VoreSetActiveSpaceBuiMsg>(OnSetActiveSpaceMsg);
        });

        Subs.CVar(_config, ALCVars.ALVoreSpacesLimit, v => _spaceLimit = v, true);
        Subs.CVar(_config, ALCVars.ALVoreNameCharacterLimit, v => _nameCharacterLimit = v, true);
        Subs.CVar(_config, ALCVars.ALVoreDescriptionCharacterLimit, v => _descriptionCharacterLimit = v, true);

        Subs.CVar(_config, ALCVars.ALVoreDamageBurnMin, v => _damageBurnMin = v, true);
        Subs.CVar(_config, ALCVars.ALVoreDamageBurnMax, v => _damageBurnMax = v, true);
        Subs.CVar(_config, ALCVars.ALVoreDamageBurnDefault, v => _damageBurnDefault = v, true);

        Subs.CVar(_config, ALCVars.ALVoreDamageBruteMin, v => _damageBruteMin = v, true);
        Subs.CVar(_config, ALCVars.ALVoreDamageBruteMax, v => _damageBruteMax = v, true);
        Subs.CVar(_config, ALCVars.ALVoreDamageBruteDefault, v => _damageBruteDefault = v, true);

        Subs.CVar(_config, ALCVars.ALVoreMuffleRadioDefault, v => _muffleRadioDefault = v, true);
        Subs.CVar(_config, ALCVars.ALVoreChanceToEscapeDefault, v => _chanceToEscapeDefault = v, true);

        Subs.CVar(_config, ALCVars.ALVoreTimeToEscapeMin, v => _timeToEscapeMin = TimeSpan.FromSeconds(v), true);
        Subs.CVar(_config, ALCVars.ALVoreTimeToEscapeMax, v => _timeToEscapeMax = TimeSpan.FromSeconds(v), true);
        Subs.CVar(_config, ALCVars.ALVoreTimeToEscapeDefault, v => _timeToEscapeDefault = TimeSpan.FromSeconds(v), true);

        Subs.CVar(_config, ALCVars.ALVoreCanTasteDefault, v => _canTasteDefault = v, true);

        Subs.CVar(_config, ALCVars.ALVoreInsertionVerbDefault, v => _insertionVerbDefault = v, true);
        Subs.CVar(_config, ALCVars.ALVoreInsertionVerbCharacterLimit, v => _insertionVerbCharacterLimit = v, true);

        Subs.CVar(_config, ALCVars.ALVoreReleaseVerbDefault, v => _releaseVerbDefault = v, true);
        Subs.CVar(_config, ALCVars.ALVoreReleaseVerbCharacterLimit, v => _releaseVerbCharacterLimit = v, true);

        Subs.CVar(_config, ALCVars.ALVoreFleshySpaceDefault, v => _fleshySpaceDefault = v, true);
        Subs.CVar(_config, ALCVars.ALVoreInternalSoundLoopDefault, v => _internalSoundLoopDefault = v, true);

        Subs.CVar(_config, ALCVars.ALVoreInsertionSoundDefault, v => _insertionSoundDefault = v, true);
        Subs.CVar(_config, ALCVars.ALVoreReleaseSoundDefault, v => _releaseSoundDefault = v, true);

        Subs.CVar(_config, ALCVars.ALVoreMessagesCountLimit, v => _messageCountLimit = v, true);
        Subs.CVar(_config, ALCVars.ALVoreMessagesCharacterLimit, v => _messageCharacterLimit = v, true);

        ReloadPrototypes();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        if (ev.WasModified<EntityPrototype>())
            ReloadPrototypes();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _prompts.Clear();
    }

    private void OnPromptAccept(VorePromptAcceptEvent msg, EntitySessionEventArgs args)
    {
        if (!_prompts.TryGetValue(msg.Prompt, out var prompt))
            return;

        if (args.SenderSession.AttachedEntity is not { } ent)
            return;

        prompt.Waiting.Remove(ent);
        if (prompt.Waiting.Count > 0)
            return;

        Vore(prompt.Predator, prompt.Prey, prompt.User);
    }

    private void OnPromptDecline(VorePromptDeclineEvent msg, EntitySessionEventArgs args)
    {
        if (!_prompts.Remove(msg.Prompt, out var list))
            return;

        if (args.SenderSession.AttachedEntity is not { } ent)
            return;

        if (_net.IsServer)
            _popup.PopupEntity($"{Name(ent)} is not interested", ent, PopupType.MediumCaution);
    }

    private void OnCanBePredatorUpdated<T>(Entity<CanBeVorePredatorComponent> ent, ref T args)
    {
        if (!HasComp<ActorComponent>(ent) ||
            !_alMobInteraction.HasPreference(ent.Owner, ALContentPref.Vore))
        {
            RemCompDeferred<VorePredatorComponent>(ent);
            RemCompDeferred<VoreActionComponent>(ent);
            return;
        }

        EnsureComp<VorePredatorComponent>(ent);
        EnsureComp<VoreActionComponent>(ent);
    }

    private void OnCanBePreyUpdated<T>(Entity<CanBeVorePreyComponent> ent, ref T args)
    {
        if (!HasComp<ActorComponent>(ent) ||
            !_alMobInteraction.HasPreference(ent.Owner, ALContentPref.Vore))
        {
            RemCompDeferred<VorePreyComponent>(ent);
            RemCompDeferred<VoreActionComponent>(ent);
            return;
        }

        EnsureComp<VorePreyComponent>(ent);
        EnsureComp<VoreActionComponent>(ent);
    }

    private void OnActionMapInit(Entity<VoreActionComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.Action, ent.Comp.ActionId);
        _alUi.EnsureUI(ent.Owner, VoreUi.Key, "VoreBui");
    }

    private void OnActionRemove(Entity<VoreActionComponent> ent, ref ComponentRemove args)
    {
        if (Deleting(ent))
            return;

        PredictedQueueDel(ent.Comp.Action);
        _ui.CloseUi(ent.Owner, VoreUi.Key);

        foreach (var container in _container.GetAllContainers(ent).ToArray())
        {
            var id = container.ID;
            if (IsVoreContainer(container))
                _container.EmptyContainer(container);
        }
    }

    private void OnPredatorMapInit(Entity<VorePredatorComponent> ent, ref MapInitEvent args)
    {
        ReloadSpaces(ent);
    }

    private void OnPredatorRemove(Entity<VorePredatorComponent> ent, ref ComponentRemove args)
    {
        if (Deleting(ent))
            return;

        EmptyPredator(ent);
    }

    private void OnPredatorActionEvent(Entity<VorePredatorComponent> ent, ref VoreActionEvent args)
    {
        _ui.OpenUi(ent.Owner, VoreUi.Key, ent);
    }

    private void OnPredatorVerbs(Entity<VorePredatorComponent> predator, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract)
            return;

        var prey = args.User;
        if (prey == args.Target)
            return;

        if (!TryComp(prey, out VorePreyComponent? userPrey) ||
            !TryGetActiveSpace(predator.AsNullable(), out var space))
        {
            return;
        }

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("al-vore-verb-feed-yourself-to-predator", ("predator", predator), ("space", space.Name)),
            Category = VoreCategory,
            Act = () => StartVorePrompt(predator, (prey, userPrey), prey),
        });

        if (TryComp(prey, out PullerComponent? puller) &&
            puller.Pulling != predator.Owner &&
            TryComp(puller.Pulling, out VorePreyComponent? pullingPrey))
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("al-vore-verb-feed-pulled-to-predator", ("predator", predator), ("space", space.Name)),
                Category = VoreCategory,
                Act = () => StartVorePrompt(predator, (puller.Pulling.Value, pullingPrey), prey),
            });
        }
    }

    private void OnPredatorAttached(Entity<VorePredatorComponent> ent, ref PlayerAttachedEvent args)
    {
        ReloadSpaces(ent);

        ent.Comp.Disconnected = false;
        Dirty(ent);
    }

    private void OnPredatorDetached(Entity<VorePredatorComponent> ent, ref PlayerDetachedEvent args)
    {
        ent.Comp.Disconnected = true;
        Dirty(ent);
    }

    private void OnPredatorIngestDoAfter(Entity<VorePredatorComponent> predator, ref VoreIngestDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        if (args.Target is not { } target ||
            !CanVorePopup(predator.AsNullable(), target, args.User, out var space))
        {
            return;
        }

        var container = _container.EnsureContainer<Container>(predator, GetSpaceContainerId(space));
        if (_container.Insert(target, container))
            EnsureComp<ActiveVorePreyComponent>(target);

        // TODO AFTERLIGHT
        if (_net.IsClient)
            return;

        // TODO AFTERLIGHT
        _popup.PopupEntity(Loc.GetString("al-vore-predator-ate-others", ("predator", predator), ("prey", args.Target), ("space", space.Name)), predator, predator, PopupType.Medium);
        TryPlaySound(predator, space.InsertionSound, ALContentPref.VoreEatingNoises);
    }

    private void OnPreyRemove(Entity<VorePreyComponent> ent, ref ComponentRemove args)
    {
        if (Deleting(ent))
            return;

        EjectPrey(ent);
    }

    private void OnPreyVerbs(Entity<VorePreyComponent> prey, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract)
            return;

        var user = args.User;
        if (user == args.Target)
            return;

        if (!TryComp(user, out VorePredatorComponent? predator) ||
            !TryGetActiveSpace((user, predator), out var space))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("al-vore-verb-vore", ("prey", prey), ("space", space.Name)),
            Category = VoreCategory,
            Act = () => StartVorePrompt((user, predator), prey, user),
        });
    }

    private void OnPreyAttached(Entity<VorePreyComponent> ent, ref PlayerAttachedEvent args)
    {
        ent.Comp.Disconnected = false;
        Dirty(ent);
    }

    private void OnPreyDetached(Entity<VorePreyComponent> ent, ref PlayerDetachedEvent args)
    {
        ent.Comp.Disconnected = true;
        Dirty(ent);
    }

    private void OnPreyMoveInput(Entity<VorePreyComponent> prey, ref MoveInputEvent args)
    {
        if (!args.HasDirectionalMovement)
            return;

        if (!IsVored(prey.AsNullable(), out var container, out var space))
            return;

        var ev = new VoreEscapeDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, prey, space.TimeToEscape, ev, prey)
        {
            CancelDuplicate = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        if (_net.IsClient)
            return;

        _popup.PopupEntity($"{Name(prey)} is attempting to free themselves from {Name(container.Owner)}'s {space.Name}!", container.Owner, PopupType.MediumCaution);
    }

    private void OnPreyEscapeDoAfter(Entity<VorePreyComponent> prey, ref VoreEscapeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        if (!IsVored(prey.AsNullable(), out var container, out var space))
            return;

        EjectPrey(prey);

        if (_net.IsServer)
            _popup.PopupEntity($"{Name(prey)} frees themselves from {Name(container.Owner)}'s {space.Name}!", container.Owner, PopupType.MediumCaution);

        TryPlaySound(container.Owner, space.ReleaseSound, ALContentPref.VoreEatingNoises);
    }

    private void OnPreyGotInserted(Entity<VorePreyComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (_timing.ApplyingState)
            return;

        if (TerminatingOrDeleted(ent))
            return;

        EnsureComp<ActiveVorePreyComponent>(ent);
    }

    private void OnPreyGotRemoved(Entity<VorePreyComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!IsVoreContainer(args.Container))
            return;

        RemCompDeferred<ActiveVorePreyComponent>(ent);
    }

    private void OnActivePreyPressureDamageAttempt(Entity<ActiveVorePreyComponent> ent, ref ALPressureDamageAttemptEvent args)
    {
        if (!_container.TryGetContainingContainer(ent.Owner, out var container))
            return;

        if (_alBarotrauma.IsTakingPressureDamage(container.Owner))
            return;

        // TODO AFTERLIGHT pressure damage from gases inside someone when we get inflation mechanics?
        args.Cancelled = true;
    }

    private void OnAddSpaceMsg(Entity<VorePredatorComponent> ent, ref VoreAddSpaceBuiMsg args)
    {
        TryAddSpaceTo(ent);
    }

    private void OnChangeMessageMsg(Entity<VorePredatorComponent> ent, ref VoreChangeMessageBuiMsg args)
    {
        if (!ent.Comp.Spaces.TryGetValue(ent.Comp.ActiveSpace, out var space) ||
            !space.Messages.TryGetValue(args.Type, out var messages) ||
            args.Index < 0 ||
            !messages.TryGetValue(args.Index, out _))
        {
            return;
        }

        var text = args.Text;
        if (text.Length > _messageCharacterLimit)
            text = text[.._messageCharacterLimit];

        messages[args.Index] = text;
        Dirty(ent);
    }

    private void OnSetSettingsMsg(Entity<VorePredatorComponent> ent, ref VoreSetSpaceSettingsBuiMsg args)
    {
        var space = ValidateSpace(args.Space);
        if (!TryGetSpace(ent.AsNullable(), args.Index, out var selectedSpace))
            return;

        space.Id = selectedSpace.Id;
        space.Messages = selectedSpace.Messages;
        ent.Comp.Spaces[args.Index] = space;
        Dirty(ent);
        UpdateSpaceDatabase(args.Actor, space);
    }

    private void OnDeleteSpaceMsg(Entity<VorePredatorComponent> ent, ref VoreDeleteSpaceBuiMsg args)
    {
        if (args.Index < 0 || args.Index >= ent.Comp.Spaces.Count)
            return;

        var space = ent.Comp.Spaces[args.Index];
        ent.Comp.Spaces.RemoveAt(args.Index);
        Dirty(ent);

        if (_container.TryGetContainer(ent, GetSpaceContainerId(space), out var container))
            TryEmptyPredatorContainer(container);

        RemoveSpaceDatabase(ent, space.Id);
    }

    private void OnSetActiveSpaceMsg(Entity<VorePredatorComponent> ent, ref VoreSetActiveSpaceBuiMsg args)
    {
        if (!TryGetSpace(ent.AsNullable(), args.Index, out var space))
            return;

        ent.Comp.ActiveSpace = args.Index;
        Dirty(ent);

        _popup.PopupPredictedCursor(Loc.GetString("al-vore-selected-active-space", ("space", space.Name)), ent, PopupType.LargeCaution);
    }

    private void ReloadPrototypes()
    {
        var overlays = ImmutableDictionary.CreateBuilder<EntProtoId<VoreOverlayComponent>, VoreOverlayComponent>();
        foreach (var (proto, comp) in _alPrototype.EnumerateComponents<VoreOverlayComponent>())
        {
            overlays[proto.ID] = comp;
        }

        Overlays = overlays.ToImmutable();

        var messages = ImmutableArray
            .CreateBuilder<(EntityPrototype Entity, VoreMessageCollectionComponent Comp)>();
        foreach (var (proto, comp) in _alPrototype.EnumerateComponents<VoreMessageCollectionComponent>())
        {
            messages.Add((proto, comp));
        }

        Messages = messages.ToImmutable();

        InsertionSounds = _alPrototype
            .IndexOrNullComponent<VoreSoundCollectionComponent>(InsertionSoundsId)
            ?.Sounds
            .ToImmutableArray() ?? ImmutableArray<(string Name, SoundPathSpecifier? Sound)>.Empty;
        ReleaseSounds = _alPrototype
            .IndexOrNullComponent<VoreSoundCollectionComponent>(ReleaseSoundsId)
            ?.Sounds
            .ToImmutableArray() ?? ImmutableArray<(string Name, SoundPathSpecifier? Sound)>.Empty;
    }

    private VoreSpace CreateDefaultSpace()
    {
        // TODO AFTERLIGHT before merge default messages
        var id = Guid.NewGuid();
        return new VoreSpace(
            id,
            Loc.GetString("al-vore-ui-space-default-name"),
            Loc.GetString("al-vore-ui-space-default-description"),
            null,
            VoreSpaceMode.Safe,
            _damageBurnDefault,
            _damageBruteDefault,
            _muffleRadioDefault,
            _chanceToEscapeDefault,
            _timeToEscapeDefault,
            _canTasteDefault,
            _insertionVerbDefault,
            _releaseVerbDefault,
            _fleshySpaceDefault,
            _internalSoundLoopDefault,
            InsertionSounds.FirstOrDefault(kvp => kvp.Name == _insertionSoundDefault).Sound,
            ReleaseSounds.FirstOrDefault(kvp => kvp.Name == _releaseSoundDefault).Sound,
            Messages.ToDictionary(tuple => tuple.Comp.MessageType, tuple => tuple.Comp.Messages.ToList())
        );
    }

    private void TryAddSpaceTo(Entity<VorePredatorComponent, ActorComponent?> player)
    {
        if (player.Comp1.Spaces.Count >= _spaceLimit ||
            GetDbSpaces(player)?.Count >= _spaceLimit)
        {
            return;
        }

        var space = CreateDefaultSpace();
        if (_net.IsServer)
        {
            player.Comp1.Spaces.Add(space);
            Dirty(player, player.Comp1);
        }

        UpdateSpaceDatabase(player, space);
    }

    protected virtual void UpdateSpaceDatabase(EntityUid player, VoreSpace space)
    {
    }

    protected virtual void RemoveSpaceDatabase(EntityUid player, Guid space)
    {
    }

    protected virtual List<VoreSpace>? GetDbSpaces(Entity<VorePredatorComponent> predator)
    {
        return null;
    }

    private string GetSpaceContainerId(VoreSpace space)
    {
        return $"{VoreContainerPrefix}{space.Id}";
    }

    public string GetReplacedString(Entity<VorePredatorComponent?> predator, VoreSpace space, string str)
    {
        if (!Resolve(predator, ref predator.Comp, false))
            return str;

        var builder = new StringBuilder(str);

        builder
            .Replace("%pred", Name(predator))
            .Replace("%belly", space.Name)
            .Replace("%space", space.Name);

        if (_container.TryGetContainer(predator, GetSpaceContainerId(space), out var container))
        {
            var preyNames = string.Join(", ", container.ContainedEntities.Select(e => Name(e)));
            builder
                .Replace("%prey", preyNames)
                .Replace("%count", container.Count.ToString());
        }

        return builder.ToString();
    }

    private VoreSpace ValidateSpace(VoreSpace space)
    {
        if (space.Name.Length > _nameCharacterLimit)
            space.Name = space.Name[.._nameCharacterLimit];

        if (space.Description.Length > _descriptionCharacterLimit)
            space.Description = space.Description[.._descriptionCharacterLimit];

        if (space.Overlay != null && !_prototype.HasIndex(space.Overlay))
            space.Overlay = null;

        if (!Enum.IsDefined(space.Mode))
            space.Mode = VoreSpaceMode.Safe;

        space.BurnDamage = FixedPoint2.Clamp(space.BurnDamage, _damageBurnMin, _damageBurnMax);
        space.BruteDamage = FixedPoint2.Clamp(space.BruteDamage, _damageBruteMin, _damageBruteMax);

        space.ChanceToEscape = Math.Clamp(space.ChanceToEscape, 0, 100);

        if (space.TimeToEscape < _timeToEscapeMin)
            space.TimeToEscape = _timeToEscapeMin;

        if (space.TimeToEscape > _timeToEscapeMax)
            space.TimeToEscape = _timeToEscapeMax;

        if (space.InsertionVerb != null && space.InsertionVerb.Length > _insertionVerbCharacterLimit)
            space.InsertionVerb = space.InsertionVerb[.._insertionVerbCharacterLimit];

        if (space.ReleaseVerb != null && space.ReleaseVerb.Length > _releaseVerbCharacterLimit)
            space.ReleaseVerb = space.ReleaseVerb[.._releaseVerbCharacterLimit];

        if (!InsertionSounds.Any(kvp => kvp.Sound?.Path == space.InsertionSound?.Path))
            space.InsertionSound = null;

        if (!ReleaseSounds.Any(kvp => kvp.Sound?.Path == space.ReleaseSound?.Path))
            space.ReleaseSound = null;

        if (space.Messages.Count > Enum.GetValues<VoreMessageType>().Length * 5)
            space.Messages.Clear();

        foreach (var (type, messages) in space.Messages.ToArray())
        {
            if (!Enum.IsDefined(type))
                space.Messages.Remove(type);

            if (messages.Count > _messageCountLimit)
                space.Messages[type] = messages[.._messageCountLimit];

            var setMessages = space.Messages[type];
            for (var i = 0; i < setMessages.Count; i++)
            {
                var message = setMessages[i];
                if (message.Length <= _messageCharacterLimit)
                    continue;

                message = message[.._messageCharacterLimit];
                setMessages[i] = message;
            }
        }

        return space;
    }

    private void StartVorePrompt(Entity<VorePredatorComponent> predator, Entity<VorePreyComponent> prey, EntityUid user)
    {
        if (!CanVorePopup(predator.AsNullable(), prey.AsNullable(), user, out _))
            return;

        var promptId = Guid.NewGuid();
        var promptEv = new VorePromptEvent(promptId, GetNetEntity(predator), GetNetEntity(prey), GetNetEntity(user));
        var prompt = new VorePrompt(new List<EntityUid>(), predator, prey, user);
        _prompts[promptId] = prompt;

        if (user != predator.Owner && user != prey.Owner)
        {
            RaiseNetworkEvent(promptEv, predator);
            RaiseNetworkEvent(promptEv, prey);

            if (_net.IsServer)
            {
                _popup.PopupEntity($"{Name(predator)} is deciding if they want to be fed {Name(prey)}",
                    predator,
                    Filter.Entities(user, prey),
                    true,
                    PopupType.MediumCaution);
                _popup.PopupEntity($"{Name(prey)} is deciding if they want to be fed to {Name(prey)}",
                    predator,
                    Filter.Entities(user, predator),
                    true,
                    PopupType.MediumCaution);
            }

            prompt.Waiting.Add(predator);
            prompt.Waiting.Add(prey);
        }
        else if (user == predator.Owner)
        {
            if (_net.IsServer)
            {
                _popup.PopupEntity($"{Name(prey)} is deciding if they want to be fed to {Name(prey)}",
                    predator,
                    user,
                    PopupType.MediumCaution);
            }

            RaiseNetworkEvent(promptEv, prey);
            prompt.Waiting.Add(prey);
        }
        else
        {
            if (_net.IsServer)
            {
                _popup.PopupEntity($"{Name(predator)} is deciding if they want to be fed {Name(prey)}",
                    predator,
                    user,
                    PopupType.MediumCaution);
            }

            RaiseNetworkEvent(promptEv, predator);
            prompt.Waiting.Add(predator);
        }
    }

    private void Vore(Entity<VorePredatorComponent?> predator, Entity<VorePreyComponent?> prey, EntityUid user)
    {
        if (!Resolve(predator, ref predator.Comp, false) ||
            !Resolve(prey, ref prey.Comp, false))
        {
            return;
        }

        if (!CanVorePopup(predator, prey, user, out var space))
            return;

        var ev = new VoreIngestDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, user, predator.Comp.EatDelay, ev, predator, prey)
        {
            BreakOnMove = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        if (_net.IsClient)
            return;

        var msg = user == predator.Owner
            ? "al-vore-predator-start-eating-others"
            : user == prey.Owner
                ? "al-vore-prey-start-feeding-self-others"
                : "al-vore-prey-start-feeding-others";

        _popup.PopupEntity(Loc.GetString(msg,
                ("predator", predator),
                ("prey", prey),
                ("user", user),
                ("space", space.Name)),
            prey,
            PopupType.Medium
        );
    }

    private bool CanVorePopup(Entity<VorePredatorComponent?> predator, Entity<VorePreyComponent?> prey, EntityUid user, out VoreSpace space)
    {
        space = default;
        if (!Resolve(predator, ref predator.Comp, false) ||
            !Resolve(prey, ref prey.Comp, false))
        {
            // TODO AFTERLIGHT popup
            return false;
        }

        if (!predator.Comp.Running || !predator.Comp.Running)
            return false;

        // TODO AFTERLIGHT preferences
        if (TryComp(predator, out ActorComponent? predatorActor) &&
            predatorActor.PlayerSession.Status != SessionStatus.InGame)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("al-vore-player-not-online", ("name", predator)), predator, user, PopupType.MediumCaution);

            return false;
        }

        if (TryComp(prey, out ActorComponent? preyActor) &&
            preyActor.PlayerSession.Status != SessionStatus.InGame)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("al-vore-player-not-online", ("name", prey)), prey, user, PopupType.MediumCaution);

            return false;
        }

        if (user != prey.Owner &&
            TryComp(user, out PullerComponent? puller) &&
            puller.Pulling != prey)
        {
            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("al-vore-player-not-pulling", ("name", prey)), prey, user, PopupType.MediumCaution);

            return false;
        }

        if (predator.Comp.ActiveSpace < 0 ||
            !predator.Comp.Spaces.TryGetValue(predator.Comp.ActiveSpace, out space))
        {
            var msg = user == predator.Owner
                ? "al-vore-player-no-spaces-self"
                : "al-vore-player-no-spaces-other";

            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString(msg, ("predator", predator)), predator, user, PopupType.MediumCaution);

            return false;
        }

        if (!_interaction.InRangeUnobstructed(user,
                predator.Owner,
                SharedInteractionSystem.InteractionRange * 2,
                popup: true) ||
            !_interaction.InRangeUnobstructed(user,
                prey.Owner,
                SharedInteractionSystem.InteractionRange * 2,
                popup: true))
        {
            return false;
        }

        return true;
    }

    private void ReloadSpaces(Entity<VorePredatorComponent> ent)
    {
        if (_net.IsClient)
            return;

        ent.Comp.Spaces.Clear();
        ent.Comp.ActiveSpace = 0;

        if (GetDbSpaces(ent) is { } spaces)
            ent.Comp.Spaces.AddRange(spaces);

        Dirty(ent);
    }

    public bool TryGetActiveSpace(Entity<VorePredatorComponent?> predator, out VoreSpace space)
    {
        space = default;
        return Resolve(predator, ref predator.Comp, false) &&
               predator.Comp.ActiveSpace >= 0 &&
               predator.Comp.Spaces.TryGetValue(predator.Comp.ActiveSpace, out space);
    }

    public bool TryGetSpace(Entity<VorePredatorComponent?> predator, int index, out VoreSpace space)
    {
        space = default;
        return Resolve(predator, ref predator.Comp, false) &&
               index >= 0 &&
               predator.Comp.Spaces.TryGetValue(index, out space);
    }

    public bool IsVored(
        Entity<VorePreyComponent?> prey,
        [NotNullWhen(true)] out BaseContainer? container,
        out VoreSpace space)
    {
        container = null;
        space = default;

        if (!_container.TryGetContainingContainer(prey.Owner, out container))
            return false;

        if (TryComp(container.Owner, out VorePredatorComponent? predator))
        {
            foreach (var predatorSpace in predator.Spaces)
            {
                if (container.ID != GetSpaceContainerId(predatorSpace))
                    continue;

                space = predatorSpace;
                break;
            }
        }

        return IsVoreContainer(container);
    }

    public bool IsVored(Entity<VorePreyComponent?> prey)
    {
        return IsVored(prey, out _, out _);
    }

    public IEnumerable<EntityUid> GetVoredActive(Entity<VorePredatorComponent?> predator)
    {
        if (!Resolve(predator, ref predator.Comp, false) ||
            !TryGetActiveSpace(predator, out var space) ||
            !_container.TryGetContainer(predator.Owner, GetSpaceContainerId(space), out var container))
        {
            return Enumerable.Empty<EntityUid>();
        }

        return container.ContainedEntities;
    }

    private void EmptyPredator(Entity<VorePredatorComponent> ent)
    {
        foreach (var container in _container.GetAllContainers(ent).ToArray())
        {
            if (IsVoreContainer(container))
                _container.EmptyContainer(container);
        }
    }

    private void TryEmptyPredatorContainer(BaseContainer container)
    {
        if (IsVoreContainer(container))
            _container.EmptyContainer(container);
    }

    private void EjectPrey(Entity<VorePreyComponent> ent)
    {
        if (!_container.TryGetContainingContainer(ent.Owner, out var container))
            return;

        if (!IsVoreContainer(container))
            return;

        _container.Remove(ent.Owner, container);
    }

    private bool Deleting(EntityUid ent)
    {
        if (TerminatingOrDeleted(ent))
            return true;

        if (!TryComp(ent, out TransformComponent? xform) ||
            TerminatingOrDeleted(xform.GridUid) ||
            TerminatingOrDeleted(xform.MapUid))
        {
            return true;
        }

        return false;
    }

    private void TryPlaySound(EntityUid from, SoundSpecifier? sound, EntProtoId<ALContentPreferenceComponent> preference)
    {
        if (_net.IsClient || sound == null)
            return;

        var filter = Filter.Pvs(from);
        foreach (var recipient in filter.Recipients)
        {
            if (_alMobInteraction.HasPreference(recipient.AttachedEntity, preference))
            {
                _audio.PlayEntity(sound, recipient, from);
            }
        }
    }

    private bool IsVoreContainer(BaseContainer container)
    {
        // My hive for being able to assign data to containers in any meaningful way
        // (aka making them entities)
        var id = container.ID;
        return id.StartsWith(VoreContainerPrefix);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ActiveVorePreyComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_container.TryGetContainingContainer(uid, out var container))
                continue;

            if (_alRespirator.IsSuffocating(container.Owner))
                continue;

            // TODO AFTERLIGHT gas mixes inside someone when we get inflation mechanics?
            _alRespirator.MaximizeSaturation(uid);
        }
    }
}
