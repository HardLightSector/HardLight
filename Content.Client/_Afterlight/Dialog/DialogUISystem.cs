using Content.Shared._Afterlight.Dialog;
using Content.Shared._Afterlight.UserInterface;

namespace Content.Client._Afterlight.Dialog;

// Taken from https://github.com/RMC-14/RMC-14
public sealed class DialogUISystem : EntitySystem
{
    [Dependency] private readonly ALUserInterfaceSystem _alUI = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DialogComponent, AfterAutoHandleStateEvent>(OnDialogState);
    }

    private void OnDialogState(Entity<DialogComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _alUI.TryBui<DialogBui>(ent.Owner, static bui => bui.Refresh());
    }
}
