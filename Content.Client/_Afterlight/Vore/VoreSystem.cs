using Content.Client._Afterlight.Vore.UI;
using Content.Shared._Afterlight.UserInterface;
using Content.Shared._Afterlight.Vore;
using Robust.Client.Player;

namespace Content.Client._Afterlight.Vore;

public sealed class VoreSystem : SharedVoreSystem
{
    [Dependency] private readonly ALUserInterfaceSystem _alUI = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VorePredatorComponent, AfterAutoHandleStateEvent>(OnState);

        SubscribeNetworkEvent<VoreErrorSavingEvent>(OnErrorSavingMsg);
        SubscribeNetworkEvent<VorePromptEvent>(OnPrompt);
    }

    private void OnState(Entity<VorePredatorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _alUI.RefreshUIs<VoreBui>(ent.Owner);
    }

    private void OnErrorSavingMsg(VoreErrorSavingEvent args)
    {
        var window = new VoreErrorWindow();
        window.OpenCentered();
        window.Retry.OnPressed += _ =>
        {
            var retry = new VoreRetrySavingEvent(args.Id);
            RaiseNetworkEvent(retry);
            window.Close();
        };
    }

    private void OnPrompt(VorePromptEvent ev)
    {
        if (GetEntity(ev.Predator) is not { Valid: true } predator ||
            GetEntity(ev.Predator) is not { Valid: true } prey ||
            GetEntity(ev.User) is not { Valid: true } user)
        {
            return;
        }

        var window = new VorePromptWindow();
        window.Label.Text = _player.LocalEntity == predator
            ? $"{Name(user)} is trying to feed {Name(prey)} to you, are you okay with this?"
            : $"{Name(user)} is trying to feed you to {Name(predator)}, are you okay with this?";

        window.OkButton.OnPressed += _ =>
        {
            RaiseNetworkEvent(new VorePromptAcceptEvent(ev.Prompt));
            window.Close();
        };
        window.CancelButton.OnPressed += _ =>
        {
            RaiseNetworkEvent(new VorePromptDeclineEvent(ev.Prompt));
            window.Close();
        };

        window.OpenCentered();
    }
}
