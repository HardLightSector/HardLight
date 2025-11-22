using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Afterlight.Kinks;
using Content.Shared._Afterlight.MobInteraction;
using Content.Shared._Afterlight.Vore;
using Content.Shared.Database._Afterlight;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

// ReSharper disable CheckNamespace

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    #region Kinks

    Task<List<ALKinks>> GetKinks(Guid player, CancellationToken cancel);

    Task SetKink(Guid player,
        EntProtoId<KinkDefinitionComponent> kinkId,
        KinkPreference preference,
        CancellationToken cancel);

    Task UpdateKinks(Guid player,
        Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> kinks,
        CancellationToken cancel);

    Task UpdateKinks(Guid player,
        IEnumerable<EntProtoId<KinkDefinitionComponent>> kinks,
        KinkPreference preference,
        CancellationToken cancel);

    Task RemoveKink(Guid player, EntProtoId<KinkDefinitionComponent> kinkId, CancellationToken cancel);

    #endregion

    #region Vore

    Task<List<VoreSpace>> GetVoreSpaces(Guid player, CancellationToken cancel);

    Task UpdateVoreSpace(Guid player, VoreSpace space, CancellationToken cancel);

    Task DeleteVoreSpace(Guid player, Guid space, CancellationToken cancel);

    #endregion

    #region Interaction Preferences

    Task InitContentPreferences(Guid player, HashSet<EntProtoId<ALContentPreferenceComponent>> preferences, CancellationToken cancel);

    Task<HashSet<EntProtoId<ALContentPreferenceComponent>>> GetContentPreferences(Guid player, CancellationToken cancel);

    Task SetContentPreferences(Guid player, HashSet<EntProtoId<ALContentPreferenceComponent>> preferences, CancellationToken cancel);

    Task DisableContentPreference(Guid player, EntProtoId<ALContentPreferenceComponent> preference, CancellationToken cancel);

    Task EnableContentPreference(Guid player, EntProtoId<ALContentPreferenceComponent> preference, CancellationToken cancel);

    #endregion

    #region Generic DbEntry Handling

    Task<bool> Delete<TResult>([RequireStaticDelegate] Func<ServerDbContext, Task<TResult>> action);

    Task<bool> Delete<T1, TResult>(
        T1 arg1,
        [RequireStaticDelegate] Func<ServerDbContext, T1, Task<TResult>> action);

    Task<bool> Delete<T1, T2, TResult>(
        T1 arg1,
        T2 arg2,
        [RequireStaticDelegate] Func<ServerDbContext, T1, T2, Task<TResult>> action);

    #endregion
}
