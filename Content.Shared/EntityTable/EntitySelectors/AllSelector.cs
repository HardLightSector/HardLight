using Robust.Shared.Prototypes;
using Robust.Shared.Log;

namespace Content.Shared.EntityTable.EntitySelectors;

/// <summary>
/// Gets spawns from all of the child selectors
/// </summary>
public sealed partial class AllSelector : EntityTableSelector
{
    [DataField(required: true)]
    public List<EntityTableSelector> Children = new();

    protected override IEnumerable<EntProtoId> GetSpawnsImplementation(System.Random rand,
        IEntityManager entMan,
        IPrototypeManager proto)
    {
        // Defensive: malformed YAML may omit required children. Don't crash the server/tests.
        if (Children == null)
        {
            Logger.Error($"{nameof(AllSelector)} has null Children. Returning no spawns. Check your entityTable YAML for a missing 'children:' under 'table: !type:AllSelector'.");
            yield break;
        }
        var idx = 0;
        foreach (var child in Children)
        {
            if (child == null)
            {
                Logger.Error($"{nameof(AllSelector)} contains a null child selector at index {idx}. Check your YAML under 'children:' for stray dashes or malformed entries.");
                idx++;
                continue;
            }

            IEnumerable<EntProtoId>? results = null;
            try
            {
                results = child.GetSpawns(rand, entMan, proto);
            }
            catch (Exception ex)
            {
                Logger.Error($"{nameof(AllSelector)} child at index {idx} ({child.GetType().Name}) threw during GetSpawns: {ex}");
                idx++;
                continue;
            }

            if (results == null)
            {
                Logger.Error($"{nameof(AllSelector)} child at index {idx} returned null results. Skipping.");
                idx++;
                continue;
            }

            foreach (var spawn in results)
            {
                yield return spawn;
            }

            idx++;
        }
    }
}
