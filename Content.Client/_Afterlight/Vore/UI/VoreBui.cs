using System.Linq;
using System.Numerics;
using Content.Client._Afterlight.MobInteraction;
using Content.Client._Afterlight.UserInterface;
using Content.Shared._Afterlight.CCVar;
using Content.Shared._Afterlight.UserInterface;
using Content.Shared._Afterlight.Vore;
using Content.Shared.Database._Afterlight;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.OptionButton;

namespace Content.Client._Afterlight.Vore.UI;

[UsedImplicitly]
public sealed class VoreBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey), IRefreshableBui
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private VoreWindow? _window;
    private int? _index;
    private bool _updating;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<VoreWindow>();

        var control = _window.Control;
        control.AddSpaceButton.OnPressed += _ => SendPredictedMessage(new VoreAddSpaceBuiMsg());

        foreach (var mode in Enum.GetValues<VoreSpaceMode>())
        {
            control.SpaceMode.AddItem(mode.ToString());
            control.SpaceMode.SetItemMetadata(control.SpaceMode.ItemCount - 1, mode);
        }

        var system = EntMan.System<VoreSystem>();
        foreach (var (name, sound) in system.InsertionSounds)
        {
            control.SpaceInsertionSoundButton.AddItem(name);

            if (sound != null)
                control.SpaceInsertionSoundButton.SetItemMetadata(control.SpaceInsertionSoundButton.ItemCount - 1, sound);
        }

        foreach (var (name, sound) in system.ReleaseSounds)
        {
            control.SpaceReleaseSoundButton.AddItem(name);

            if (sound != null)
                control.SpaceReleaseSoundButton.SetItemMetadata(control.SpaceReleaseSoundButton.ItemCount - 1, sound);
        }

        var sprite = EntMan.System<SpriteSystem>();
        var overlayButtonGroup = new ButtonGroup();
        control.NoOverlayButton.Group = overlayButtonGroup;
        foreach (var overlay in system.Overlays)
        {
            var button = new VoreOverlayButton
            {
                Id = overlay.Prototype.ID,
                ToolTip = overlay.Prototype.Name,
                TooltipDelay = 0.1f,
            };

            button.Button.Scale = new Vector2(0.5f, 0.5f);
            button.Button.ToggleMode = true;
            button.Button.Group = overlayButtonGroup;

            if (overlay.Component.Overlay is { } overlayTex)
                button.Button.TextureNormal = sprite.Frame0(overlayTex);

            control.OverlaysGrid.AddChild(button);

            button.Button.OnPressed += _ =>
            {
                if (GetIndex() is not { } index)
                    return;

                SendPredictedMessage(new VoreSetOverlayBuiMsg(index, overlay.Prototype.ID));
            };
        }

        control.NoOverlayButton.OnPressed += _ =>
        {
            if (GetIndex() is not { } index)
                return;

            SendPredictedMessage(new VoreSetOverlayBuiMsg(index, null));
        };

        control.OverlayColor.OnColorChanged += args =>
        {
            if (GetIndex() is not { } index)
                return;

            SendPredictedMessage(new VoreSetOverlayColorBuiMsg(index, args));
        };

        control.TestOverlayButton.OnPressed += _ =>
        {
            if (GetIndex() is { } index &&
                system.TryGetSpace(Owner, index, out var space))
            {
                system.StartTestOverlay(space);
            }
        };

        UpdateSelectedSpace(null);

        control.DeleteButton.OnPressed += _ =>
        {
            if (GetIndex() is not { } index)
                return;

            SendPredictedMessage(new VoreDeleteSpaceBuiMsg(index));
        };
        control.SpaceNameEdit.OnFocusExit += _ => SendSettings();
        control.SpaceDescription.OnTextChanged += _ => SendSettings();
        control.SpaceMode.OnItemSelected += args => SendSettingsOptions(control.SpaceMode, args);
        control.SpaceBurnDamage.OnValueChanged += _ => SendSettings();
        control.SpaceBruteDamage.OnValueChanged += _ => SendSettings();
        control.SpaceMuffleRadio.OnPressed += _ => SendSettings();
        control.SpaceChanceToEscape.ValueChanged += _ => SendSettings();
        control.SpaceTimeToEscape.ValueChanged += _ => SendSettings();
        control.SpaceCanTaste.OnPressed += _ => SendSettings();
        control.SpaceInsertionVerb.OnTextEntered += _ => SendSettings();
        control.SpaceInsertionVerb.OnFocusExit += _ => SendSettings();
        control.SpaceReleaseVerb.OnTextEntered += _ => SendSettings();
        control.SpaceReleaseVerb.OnFocusExit += _ => SendSettings();

        // control.SpaceFancySounds.OnPressed += _ => SendSettings();
        control.SpaceFleshy.OnPressed += _ => SendSettings();
        control.SpaceInternalSoundLoop.OnPressed += _ => SendSettings();
        control.SpaceInsertionSoundButton.OnItemSelected += args => SendSettingsOptions(control.SpaceInsertionSoundButton, args);
        control.SpaceInsertionSoundPlay.OnPressed += _ => PlaySound(control.SpaceInsertionSoundButton.SelectedMetadata);
        control.SpaceReleaseSoundButton.OnItemSelected += args => SendSettingsOptions(control.SpaceReleaseSoundButton, args);
        control.SpaceReleaseSoundPlay.OnPressed += _ => PlaySound(control.SpaceReleaseSoundButton.SelectedMetadata);

        var mobInteraction = EntMan.System<ALMobInteractionSystem>();
        mobInteraction.AddButtons(this, control.PreferencesTab, c => c.Vore);

        Refresh();
    }

    public void Refresh()
    {
        UpdatePredatorView(Owner);
        UpdatePreyView(Owner);
    }

    private void UpdatePredatorView(EntityUid predator)
    {
        if (_window?.Control is not { } control)
            return;

        if (!EntMan.TryGetComponent(Owner, out VorePredatorComponent? predatorComp))
            return;

        // TODO AFTERLIGHT
        control.SlotNameLabel.Text = Loc.GetString("al-vore-ui-slot-name", ("name", "New Slot (0)"));

        var spaceCheckboxGroup = new ButtonGroup();
        var spaceButtonGroup = new ButtonGroup();
        for (var i = 0; i < predatorComp.Spaces.Count; i++)
        {
            var space = predatorComp.Spaces[i];
            var row = control.SpacesContainer.ChildCount > i
                ? (VoreSpaceRow) control.SpacesContainer.GetChild(i)
                : new VoreSpaceRow { Id = space.Id };

            row.Checkbox.Group = spaceCheckboxGroup;
            row.Checkbox.Pressed = i == predatorComp.ActiveSpace;

            var j = i;
            row.Checkbox.OnPressed += _ => SendPredictedMessage(new VoreSetActiveSpaceBuiMsg(j));

            row.Button.Text = space.Name;
            row.Button.Group = spaceButtonGroup;
            row.Button.Pressed = i == _index;

            row.Button.OnPressed += _ => UpdateSelectedSpace(j);

            if (row.Parent != control.SpacesContainer)
                control.SpacesContainer.AddChild(row);
        }

        for (var i = control.SpacesContainer.ChildCount - 1; i >= 0; i--)
        {
            var space = (VoreSpaceRow) control.SpacesContainer.GetChild(i);
            if (predatorComp.Spaces.Any(s => s.Id == space.Id))
                continue;

            control.SpacesContainer.RemoveChild(i);
            if (_index != i)
                continue;

            _index = null;
            UpdateSelectedSpace(null);
        }

        control.AddSpaceButton.Disabled = predatorComp.Spaces.Count >= _config.GetCVar(ALCVars.ALVoreSpacesLimit);

        control.SpaceContentsEmptyLabel.Visible = true;
        // control.SpaceMessagesContainer.DisposeAllChildren(); // TODO AFTERLIGHT
        control.SpaceContentsContainer.DisposeAllChildren();

        var system = EntMan.System<VoreSystem>();
        if (!system.TryGetActiveSpace(predator, out var selectedSpace))
        {
            control.SpaceTabs.Visible = false;
            return;
        }

        control.SpaceTabs.Visible = true;
        foreach (var (proto, comp) in system.Messages)
        {
            var container = new VoreMessageContainer();
            container.Label.Text = proto.Name;
            // control.SpaceMessagesContainer.AddChild(container);

            if (!selectedSpace.Messages.TryGetValue(comp.MessageType, out var messages))
                continue;

            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                var row = new VoreMessageRow();
                row.MessageEdit.Text = message;

                var j = i;
                row.MessageEdit.OnTextEntered += args => OnMessageChanged(comp.MessageType, j, args.Text);
                row.MessageEdit.OnFocusExit += args => OnMessageChanged(comp.MessageType, j, args.Text);

                container.AddChild(row);
            }
        }

        foreach (var vored in system.GetVoredActive(predator))
        {
            control.SpaceContentsEmptyLabel.Visible = false;
            var view = new VorePreyView();
            view.SetEntity(EntMan, vored, _player.LocalEntity);
            control.SpaceContentsContainer.AddChild(view);
        }

        control.NoOverlayButton.Pressed = selectedSpace.Overlay == null;
        foreach (var button in control.OverlaysGrid.ChildrenOfType<VoreOverlayButton>())
        {
            button.Button.Pressed = button.Id == selectedSpace.Overlay;
            if (button.Panel.PanelOverride is StyleBoxFlat box)
                box.BorderThickness = button.Id == selectedSpace.Overlay ? new Thickness(3) : new Thickness(0);
        }
    }

    private void UpdatePreyView(EntityUid prey)
    {
        if (_window?.Control is not { } control)
            return;

        var system = EntMan.System<VoreSystem>();
        if (!system.IsVored(prey, out var container, out var space))
        {
            control.InsideLabel.Text = Loc.GetString("al-vore-ui-not-inside-anyone");
            return;
        }

        control.InsideLabel.Text = Loc.GetString("al-vore-ui-inside-predator",
            ("predator", container.Owner),
            ("space", space.Name),
            ("description", system.GetReplacedString(container.Owner, space, space.Description))
        );

        foreach (var vored in container.ContainedEntities)
        {
            var view = new VorePreyView();
            view.SetEntity(EntMan, vored, _player.LocalEntity);
            control.InsideContainer.AddChild(view);
        }
    }

    private void OnMessageChanged(VoreMessageType type, int index, string text)
    {
        SendPredictedMessage(new VoreChangeMessageBuiMsg(type, index, text));
    }

    private void SendSettings()
    {
        if (_window?.Control is not { } control)
            return;

        if (_updating || GetIndex() is not { } index)
            return;

        SendPredictedMessage(new VoreSetSpaceSettingsBuiMsg(
            index,
            new VoreSpace(
                Guid.Empty,
                control.SpaceNameEdit.Text,
                Rope.Collapse(control.SpaceDescription.TextRope),
                null, // TODO AFTERLIGHT
                control.OverlayColor.Color,
                (VoreSpaceMode)(control.SpaceMode.SelectedMetadata ?? VoreSpaceMode.Safe),
                FixedPoint2.New(control.SpaceBurnDamage.Value),
                FixedPoint2.New(control.SpaceBruteDamage.Value),
                control.SpaceMuffleRadio.Pressed,
                control.SpaceChanceToEscape.Value,
                TimeSpan.FromSeconds(control.SpaceTimeToEscape.Value),
                control.SpaceCanTaste.Pressed,
                control.SpaceInsertionVerb.Text,
                control.SpaceReleaseVerb.Text,
                // control.SpaceFancySounds.Pressed,
                control.SpaceFleshy.Pressed,
                control.SpaceInternalSoundLoop.Pressed,
                control.SpaceInsertionSoundButton.SelectedMetadata as SoundPathSpecifier,
                control.SpaceReleaseSoundButton.SelectedMetadata as SoundPathSpecifier,
                new Dictionary<VoreMessageType, List<string>>()
            )
        ));
    }

    private void SendSettingsOptions(OptionButton button, ItemSelectedEventArgs args)
    {
        button.SelectId(args.Id);
        SendSettings();
    }

    private void PlaySound(object? selectedMetadata)
    {
        if (selectedMetadata is not SoundSpecifier sound)
            return;

        var audio = EntMan.System<AudioSystem>();
        audio.PlayGlobal(sound, Filter.Local(), true);
    }

    private void UpdateSelectedSpace(int? index)
    {
        if (_window?.Control is not { } control)
            return;

        if (index != null)
            _index = index.Value;

        var system = EntMan.System<SharedVoreSystem>();
        VoreSpace space;
        if (index == null)
        {
            if (!system.TryGetActiveSpace(Owner, out space))
            {
                control.SpaceTabs.Visible = false;
                return;
            }
        }
        else if (!system.TryGetSpace(Owner, index.Value, out space))
        {
            return;
        }

        control.SpaceTabs.Visible = true;

        _updating = true;
        try
        {
            control.SpaceNameEdit.Text = space.Name;
            control.SpaceDescription.TextRope = new Rope.Leaf(space.Description);
            control.SpaceMode.TrySelectId((int) space.Mode);
            control.SpaceBurnDamage.Value = space.BurnDamage.Float();
            control.SpaceBruteDamage.Value = space.BruteDamage.Float();
            control.SpaceMuffleRadio.Pressed = space.MuffleRadio;
            control.SpaceChanceToEscape.Value = space.ChanceToEscape;
            control.SpaceTimeToEscape.Value = (int) space.TimeToEscape.TotalSeconds;
            control.SpaceCanTaste.Pressed = space.CanTaste;
            control.SpaceInsertionVerb.Text = space.InsertionVerb ?? string.Empty;
            control.SpaceReleaseVerb.Text = space.ReleaseVerb ?? string.Empty;
            // control.SpaceFancySounds.Pressed = space.FancySounds;
            control.SpaceFleshy.Pressed = space.Fleshy;
            control.SpaceInternalSoundLoop.Pressed = space.InternalSoundLoop;

            for (var i = 0; i < control.SpaceInsertionSoundButton.ItemCount; i++)
            {
                if (control.SpaceInsertionSoundButton.GetItemMetadata(i) is not SoundPathSpecifier selectedSound ||
                    selectedSound.Path != space.InsertionSound?.Path)
                {
                    continue;
                }

                control.SpaceInsertionSoundButton.TrySelectId(i);
                break;
            }

            for (var i = 0; i < control.SpaceReleaseSoundButton.ItemCount; i++)
            {
                if (control.SpaceReleaseSoundButton.GetItemMetadata(i) is not SoundPathSpecifier selectedSound ||
                    selectedSound.Path != space.ReleaseSound?.Path)
                {
                    continue;
                }

                control.SpaceReleaseSoundButton.TrySelectId(i);
                break;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    public int? GetIndex()
    {
        return _index ?? EntMan.GetComponentOrNull<VorePredatorComponent>(Owner)?.ActiveSpace;
    }
}
