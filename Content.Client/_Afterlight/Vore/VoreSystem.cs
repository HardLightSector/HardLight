using Content.Client._Afterlight.Vore.UI;
using Content.Shared._Afterlight.UserInterface;
using Content.Shared._Afterlight.Vore;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Timing;

namespace Content.Client._Afterlight.Vore;

public sealed class VoreSystem : SharedVoreSystem
{
    [Dependency] private readonly ALUserInterfaceSystem _alUI = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _testingOverlayUntil;
    private VoreSpace _testOverlay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VorePredatorComponent, AfterAutoHandleStateEvent>(OnState);

        SubscribeNetworkEvent<VoreErrorSavingEvent>(OnErrorSavingMsg);
        SubscribeNetworkEvent<VorePromptEvent>(OnPrompt);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _testingOverlayUntil = TimeSpan.Zero;
        _testOverlay = default;
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
            GetEntity(ev.Prey) is not { Valid: true } prey ||
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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var force = _testingOverlayUntil > TimeSpan.Zero && _timing.CurTime < _testingOverlayUntil;
        if (!force && !IsVored(_player.LocalEntity))
        {
            _overlay.RemoveOverlay<VoreOverlay>();
            return;
        }

        if (!_overlay.HasOverlay<VoreOverlay>())
            _overlay.AddOverlay(new VoreOverlay());
    }

    public void StartTestOverlay(VoreSpace space)
    {
        _testingOverlayUntil = _timing.CurTime + TimeSpan.FromSeconds(3);
        _testOverlay = space;
    }

    public bool IsTestingOverlay(out VoreSpace space)
    {
        space = default;
        return _timing.CurTime < _testingOverlayUntil && (space = _testOverlay) != default;
    }
}
