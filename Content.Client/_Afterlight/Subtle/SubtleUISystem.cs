using System.Linq;
using Content.Client.Popups;
using Content.Shared._Afterlight.CCVar;
using Content.Shared._Afterlight.Subtle;
using Content.Shared.Popups;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._Afterlight.Subtle;

public sealed class SubtleUISystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SubtleSystem _subtle = default!;

    private int _maxCharacters;
    private readonly HashSet<SubtleWindow> _windows = new();

    public override void Initialize()
    {
        Subs.CVar(_config, ALCVars.ALSubtleMaxCharacters, v => _maxCharacters = v, true);
    }

    public override void Shutdown()
    {
        foreach (var window in _windows.ToArray())
        {
            window.Close();
        }
    }

    public void OpenWindow()
    {
        var window = new SubtleWindow();
        window.OnClose += () => _windows.Remove(window);
        _windows.Add(window);

        window.TextEdit.OnTextChanged += args =>
        {
            var current = Rope.CalcTotalLength(args.TextRope);
            var msg = Loc.GetString("al-subtle-character-count", ("current", current), ("max", _maxCharacters));
            window.CharacterCountLabel.Text = msg;
            window.SubmitButton.Disabled = Rope.IsNullOrEmpty(args.TextRope);
        };

        window.SubmitButton.OnPressed += _ => Submit(window);
        window.CancelButton.OnPressed += _ => window.Close();

        var msg = Loc.GetString("al-subtle-character-count", ("current", 0), ("max", _maxCharacters));
        window.CharacterCountLabel.Text = msg;
        window.SubmitButton.Disabled = true;

        window.OpenCentered();
    }

    private void Submit(SubtleWindow window)
    {
        if (_player.LocalEntity is not { } ent ||
            !_subtle.CanSubtle(ent))
        {
            _popup.PopupCursor(Loc.GetString("al-subtle-cant-send"), PopupType.LargeCaution);
            return;
        }

        var msg = Rope.Collapse(window.TextEdit.TextRope);
        if (string.IsNullOrWhiteSpace(msg))
            return;

        if (msg.Length > _maxCharacters)
            msg = msg[.._maxCharacters];

        var ev = new SubtleClientEvent(msg, window.AntiGhostCheckbox.Pressed);
        RaiseNetworkEvent(ev);
        window.Close();
    }
}