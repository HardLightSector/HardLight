using Content.Shared._Afterlight.MobInteraction;
using Robust.Client.UserInterface;

namespace Content.Client._Afterlight.MobInteraction;

public sealed class ALMobInteractionBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private ALMobInteractionWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ALMobInteractionWindow>();

        var control = _window.Control;

        // TODO AFTERLIGHT
        // control.InteractingWithLabel.Text = "Interacting with yourself...";

        var mobInteraction = EntMan.System<ALMobInteractionSystem>();
        mobInteraction.AddButtons(this, control.ContentPreferencesTab, c => c.MobInteraction);
    }
}
