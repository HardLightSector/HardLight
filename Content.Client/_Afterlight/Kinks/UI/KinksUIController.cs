using Content.Client._Afterlight.UserInterface;
using Content.Client.Lobby;
using Content.Shared._Afterlight.Collections;
using Content.Shared._Afterlight.Kinks;
using Content.Shared.Database._Afterlight;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._Afterlight.Kinks.UI;

[UsedImplicitly]
public sealed class KinksUIController : UIController, IOnStateChanged<LobbyState>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    [UISystemDependency] private readonly KinkSystem? _kinks = default;

    private KinksEditingWindow? _kinksWindow;
    private readonly Dictionary<EntityUid, KinksListWindow> _openKinkWindows = new();

    public override void Initialize()
    {
        SubscribeNetworkEvent<KinkImportedFlistServerEvent>(OnKinksImported);
        SubscribeLocalEvent<OpenKinksWindowEvent>(OnOpenKinksWindow);
    }

    private void OnKinksImported(KinkImportedFlistServerEvent msg, EntitySessionEventArgs args)
    {
        if (_kinksWindow?.Control is not { } kinks)
            return;

        kinks.ImportFlistButton.Disabled = false;
        kinks.ImportFlistButton.Text = Loc.GetString("al-kinks-import-f-list");
    }

    private void OnOpenKinksWindow(OpenKinksWindowEvent ev)
    {
        if (EntityManager.GetEntity(ev.Target) is not { Valid: true } target)
            return;

        if (_openKinkWindows.TryGetValue(target, out var window))
        {
            window.OpenCentered();
            return;
        }

        if (!EntityManager.TryGetComponent(target, out KinksComponent? kinks))
            return;

        window = new KinksListWindow();
        _openKinkWindows[target] = window;

        window.OnClose += () => _openKinkWindows.Remove(target);
        window.SearchEdit.OnTextChanged += _ => window.FilterKinks(_kinks?.LocalKinks, kinks.Settings);

        window.CompareWithYourselfButton.OnPressed += args =>
        {
            if (_kinks?.LocalKinks is not { } localKinks ||
                !args.Button.Pressed)
            {
                window.ResetKinksColor();
                return;
            }

            foreach (var label in window.GetKinks())
            {
                if (label.KinkId is not { } kinkId)
                    continue;

                var localPreference = localKinks.GetValueOrNullStruct(kinkId);
                label.Label.Modulate = localPreference.GetColor();
            }
        };

        window.OnlyShowMatchingButton.OnPressed += _ => window.FilterKinks(_kinks?.LocalKinks, kinks.Settings);

        window.Title = Loc.GetString("al-kinks-player", ("player", target));

        var kinkGroups = new Dictionary<KinkPreference, List<EntityPrototype>>();
        foreach (var (kinkId, preference) in kinks.Settings)
        {
            if (!_prototype.TryIndex(kinkId, out var kink))
                continue;

            kinkGroups.GetOrNew(preference).Add(kink);
        }

        var preferences = Enum.GetValues<KinkPreference>();
        Array.Reverse(preferences);
        for (var i = 0; i < preferences.Length; i++)
        {
            var preference = preferences[i];
            var column = new KinksListColumn();
            column.NameLabel.Text = $"[color={preference.GetColor().ToHex()}]{preference.ToString()}[/color]";
            window.Columns.AddChild(column);

            if (i + 1 < preferences.Length)
            {
                window.Columns.AddChild(new VerticalYellowSeparator());
            }

            if (!kinkGroups.TryGetValue(preference, out var kinksInGroup))
                continue;

            kinksInGroup.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var kink in kinksInGroup)
            {
                var label = new KinksListLabel();
                label.KinkId = kink.ID;
                label.Label.Text = kink.Name;

                column.KinksContainer.AddChild(label);
            }
        }
    }

    public void OnStateEntered(LobbyState state)
    {
        if (state.Lobby is not { } lobby)
            return;

        lobby.CharacterPreview.KinksButton.OnPressed -= OnLobbyKinksPressed;
        lobby.CharacterPreview.KinksButton.OnPressed += OnLobbyKinksPressed;
    }

    public void OnStateExited(LobbyState state)
    {
        if (state.Lobby is not { } lobby)
            return;

        lobby.CharacterPreview.KinksButton.OnPressed -= OnLobbyKinksPressed;
    }

    public void OpenWindow()
    {
        if (_kinks == null)
            return;

        if (_kinksWindow is { IsOpen: true })
        {
            _kinksWindow.OpenCentered();
            return;
        }

        _kinksWindow = new KinksEditingWindow();
        _kinksWindow.OnClose += () => _kinksWindow = null;

        var control = _kinksWindow.Control;
        control.ImportFlistButton.OnPressed += ShowImportFlist;
        control.ImportFlistSubmitButton.OnPressed += ImportFlist;
        control.ImportFlistCancelButton.OnPressed += HideImportFlist;
        control.BackButton.OnPressed += _ => _kinksWindow.ShowCategories();
        control.SearchEdit.OnTextChanged += args => _kinksWindow.Control.OnSearchChanged(args.Text);

        _kinksWindow.ShowCategories();

        foreach (var (category, kinks) in _kinks.AllKinks)
        {
            var categoryButton = new Button
            {
                Text = category.Name,
                StyleClasses = { "OpenBoth" }
            };

            categoryButton.OnPressed += _ =>
            {
                control.MarkAllButtons.OnPressed = null;
                control.MarkAllButtons.OnPressed += (_, preference) =>
                {
                    var categoryKinkIds = new List<EntProtoId<KinkDefinitionComponent>>();
                    foreach (var child in control.Kinks.Children)
                    {
                        if (child is not KinksRow { Visible: true } row)
                            continue;

                        row.SetPreference(preference);

                        if (row.KinkId is { } kinkId)
                            categoryKinkIds.Add(kinkId);
                    }

                    _kinks.ClientSetPreferences(categoryKinkIds, preference);

                    foreach (var button in control.MarkAllButtons.AllButtons)
                    {
                        button.Button.Pressed = false;
                    }
                };

                control.Kinks.DisposeAllChildren();
                ShowKinks(category.Name);

                foreach (var kink in kinks)
                {
                    var localPreference = _kinks.LocalKinks?.GetValueOrNullStruct(kink.ID);

                    var row = new KinksRow { KinkId = kink.ID };
                    row.SetIcon(localPreference);
                    row.SetPreference(localPreference);
                    row.NameLabel.Text = $"{kink.Name}";
                    row.OnPressed += (args, preference) =>
                    {
                        if (_kinks == null)
                            return;

                        var pressed = args.Button.Pressed;
                        KinkPreference? setPreference = pressed ? preference : null;
                        _kinks.ClientSetPreference(kink.ID, setPreference);
                        row.SetPreference(setPreference);
                    };

                    if (!string.IsNullOrWhiteSpace(kink.Description))
                        row.ToolTip = kink.Description;

                    control.Kinks.AddChild(row);
                }
            };

            control.Categories.AddChild(categoryButton);
        }

        _kinksWindow.OpenCentered();
    }

    private void OnLobbyKinksPressed(ButtonEventArgs args)
    {
        OpenWindow();
    }

    private void ShowKinks(string categoryName)
    {
        if (_kinksWindow is not { } kinks)
            return;

        kinks.Title = Loc.GetString("al-kinks-category", ("category", categoryName));
        kinks.Control.ShowKinks();
    }

    private void HideImportFlist()
    {
        if (_kinksWindow?.Control is not { } kinks)
            return;

        kinks.ImportFlistButton.Visible = true;
        kinks.ImportFlistContainer.Visible = false;
    }

    private void HideImportFlist(ButtonEventArgs args)
    {
        HideImportFlist();
    }

    private void ShowImportFlist()
    {
        if (_kinksWindow?.Control is not { } kinks)
            return;

        kinks.ImportFlistButton.Visible = false;
        kinks.ImportFlistContainer.Visible = true;
    }

    private void ShowImportFlist(ButtonEventArgs args)
    {
        ShowImportFlist();
    }

    private void ImportFlist(ButtonEventArgs args)
    {
        HideImportFlist();

        if (_kinksWindow?.Control is not { } kinks)
            return;

        _kinks?.ClientImportFlist(kinks.ImportFlistEdit.Text);
        kinks.ImportFlistEdit.SetText(string.Empty, invokeEvent: false);
        kinks.ImportFlistButton.Disabled = true;
        kinks.ImportFlistButton.Text = Loc.GetString("al-kinks-import-f-list-importing");
    }
}
