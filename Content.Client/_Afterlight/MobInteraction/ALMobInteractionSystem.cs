using System.Collections.Immutable;
using Content.Shared._Afterlight.MobInteraction;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._Afterlight.MobInteraction;

public sealed class ALMobInteractionSystem : SharedALMobInteractionSystem
{
    [Dependency] private readonly IPlayerManager _player = default!;

    public ImmutableHashSet<EntProtoId<ALContentPreferenceComponent>> LocalPreferences { get; private set; } =
        ImmutableHashSet<EntProtoId<ALContentPreferenceComponent>>.Empty;

    private ALMobInteractionWindow? _window;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ALContentPreferencesChangedEvent>(OnContentPreferences);
    }

    private void OnContentPreferences(ALContentPreferencesChangedEvent ev)
    {
        LocalPreferences = ev.Preferences.ToImmutableHashSet();

        if (_player.LocalEntity is not { } ent)
            return;

        var changedEv = new ALContentPreferencesChangedEvent(ev.Preferences);
        RaiseLocalEvent(ent, changedEv);
    }

    public void AddButtons(
        BoundUserInterface? ui,
        Control control,
        Predicate<ALContentPreferenceComponent> filter,
        Action<ButtonEventArgs, EntityPrototype>? onPressed = null)
    {
        foreach (var (entity, comp) in ContentPreferencePrototypes)
        {
            if (!filter(comp))
                continue;

            var button = new ALMobInteractionPreferenceButton
            {
                Text = entity.Name,
                ToolTip = entity.Description,
                TooltipDelay = 0.1f,
                Pressed = LocalPreferences.Contains(entity.ID),
            };

            if (onPressed != null)
            {
                button.OnPressed += args => onPressed(args, entity);
            }
            else
            {
                button.OnPressed += args =>
                    ui?.SendPredictedMessage(
                        new ALMobInteractionSetContentPreferenceBuiMsg(entity.ID, args.Button.Pressed));
            }

            control.AddChild(button);
        }
    }

    public void OpenWindow()
    {
        if (_window is { IsOpen: true })
        {
            _window.OpenCentered();
            return;
        }

        _window = new ALMobInteractionWindow();
        var control = _window.Control;
        AddButtons(
            null,
            control.ContentPreferencesTab,
            c => c.MobInteraction,
            (args, entity) => RaiseNetworkEvent(new ALMobInteractionSetContentPreferenceBuiMsg(entity.ID, args.Button.Pressed))
        );

        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }
}
