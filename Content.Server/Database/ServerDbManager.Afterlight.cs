using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Afterlight.Kinks;
using Content.Shared._Afterlight.MobInteraction;
using Content.Shared._Afterlight.Vore;
using Content.Shared.Database._Afterlight;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server.Database;

public sealed partial class ServerDbManager
{
    #region Vore

    public Task<List<VoreSpace>> GetVoreSpaces(Guid player, CancellationToken cancel)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetVoreSpaces(player, cancel));
    }

    public Task UpdateVoreSpace(Guid player, VoreSpace space, CancellationToken cancel)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.UpdateVoreSpace(player, space, cancel));
    }

    public Task DeleteVoreSpace(Guid player, Guid space, CancellationToken cancel)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteVoreSpace(player, space, cancel));
    }

    #endregion

    #region Interaction Preferences

    public Task InitContentPreferences(Guid player, HashSet<EntProtoId<ALContentPreferenceComponent>> preferences, CancellationToken cancel)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.InitContentPreferences(player, preferences, cancel));
    }

    public Task<HashSet<EntProtoId<ALContentPreferenceComponent>>> GetContentPreferences(Guid player, CancellationToken cancel)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetContentPreferences(player, cancel));
    }

    public Task SetContentPreferences(Guid player, HashSet<EntProtoId<ALContentPreferenceComponent>> preferences, CancellationToken cancel)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetContentPreferences(player, preferences, cancel));
    }

    public Task DisableContentPreference(Guid player, EntProtoId<ALContentPreferenceComponent> preference, CancellationToken cancel)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DisableContentPreference(player, preference, cancel));
    }

    public Task EnableContentPreference(Guid player, EntProtoId<ALContentPreferenceComponent> preference, CancellationToken cancel)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.EnableContentPreference(player, preference, cancel));
    }

    #endregion

    #region Generic DbEntry Handling

    public Task<bool> Delete<TResult>(Func<ServerDbContext, Task<TResult>> action)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.Delete(action));
    }

    public Task<bool> Delete<T1, TResult>(
        T1 arg1,
        [RequireStaticDelegate] Func<ServerDbContext, T1, Task<TResult>> action)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.Delete(arg1, action));
    }

    public Task<bool> Delete<T1, T2, TResult>(
        T1 arg1,
        T2 arg2,
        [RequireStaticDelegate] Func<ServerDbContext, T1, T2, Task<TResult>> action)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.Delete(arg1, arg2, action));
    }

    #endregion
}
