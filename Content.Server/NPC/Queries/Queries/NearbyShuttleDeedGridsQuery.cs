using Content.Shared.Whitelist;

namespace Content.Server.NPC.Queries.Queries;

/// <summary>
/// Returns nearby grids that have a ShuttleDeedComponent
/// </summary>
public sealed partial class NearbyShuttleDeedGridsQuery : UtilityQuery
{
    [DataField]
    public float Range = 2000f;

    [DataField]
    public EntityWhitelist Blacklist = new();
}
