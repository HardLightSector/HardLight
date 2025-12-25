using Content.Shared.CM14.Xenos.Construction;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Content.Shared.CM14.Xenos.Construction.Events;
using Content.Shared.CM14.Xenos;
using Content.Shared.UserInterface;

namespace Content.Client.CM14.Xenos.Construction;

[UsedImplicitly]
public sealed class XenoConstructionClientSystem : SharedXenoConstructionSystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        Log.Info("[XenoWeeds] (client) XenoConstructionSystem.Initialize()");
    }

    protected override void OnXenoChooseStructureAction(Entity<XenoComponent> xeno, ref XenoChooseStructureActionEvent args)
    {
        // On client, always open predictively for snappy UX; server will authoritatively open too.
        Log.Info($"[XenoChooseStructure] (client) Action received for {ToPrettyString(xeno)}; forcing predictive reopen");
        // Close any existing open state then re-open predictively so it always reopens after a manual close
        _ui.CloseUi(xeno.Owner, XenoChooseStructureUI.Key);
        _ui.OpenUi(xeno.Owner, XenoChooseStructureUI.Key, predicted: true);
        var predictedOpen = _ui.TryGetOpenUi(xeno.Owner, XenoChooseStructureUI.Key, out _);
        Log.Info($"[XenoChooseStructure] (client) Predictive Open invoked; OpenState={predictedOpen}");
        // Do not mark handled; the server will still process the action and confirm.
    }
}
