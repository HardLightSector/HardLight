using JetBrains.Annotations;

using JetBrains.Annotations;

namespace Content.Shared._Afterlight.UserInterface;

// Taken from https://github.com/RMC-14/RMC-14
public sealed class ALUserInterfaceSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    private readonly List<(Entity<UserInterfaceComponent?> Ent, Action<Entity<UserInterfaceComponent?>, ALUserInterfaceSystem> Act)> _toRefresh = new();

    public void EnsureUI(Entity<UserInterfaceComponent?> ent, Enum key, string bui, float interactionRange = 2f, bool requireInputValidation = true)
    {
        if (_ui.HasUi(ent, key))
            return;

        var data = new InterfaceData(bui, interactionRange, requireInputValidation);
        _ui.SetUi(ent.AsNullable(), key, data);
    }

    public void RefreshUIs<T>(Entity<UserInterfaceComponent?> uiEnt) where T : BoundUserInterface, IRefreshableBui
    {
        _toRefresh.Add((uiEnt, static (uiEnt, system) =>
        {
            try
            {
                if (system.TerminatingOrDeleted(uiEnt))
                    return;

                if (!system.Resolve(uiEnt, ref uiEnt.Comp))
                    return;

                foreach (var bui in uiEnt.Comp.ClientOpenInterfaces.Values)
                {
                    if (bui is T ui)
                        ui.Refresh();
                }
            }
            catch (Exception e)
            {
                system.Log.Error($"Error refreshing {nameof(T)}\n{e}");
            }
        }));
    }

    public void TryBui<T>(Entity<UserInterfaceComponent?> ent, [RequireStaticDelegate] Action<T> action) where T : BoundUserInterface
    {
        try
        {
            if (!Resolve(ent, ref ent.Comp, false))
                return;

            foreach (var bui in ent.Comp.ClientOpenInterfaces.Values)
            {
                if (bui is T dialogUi)
                    action(dialogUi);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error getting {nameof(T)}:\n{e}");
        }
    }

    public override void Update(float frameTime)
    {
        try
        {
            foreach (var refresh in _toRefresh)
            {
                refresh.Act(refresh.Ent, this);
            }
        }
        finally
        {
            _toRefresh.Clear();
        }
    }
}
