using Content.Server.Atmos.Components;
using Content.Shared._Afterlight.Atmos;

namespace Content.Server._Afterlight.Atmos;

public sealed class ALBarotraumaSystem : SharedALBarotraumaSystem
{
    public override bool IsTakingPressureDamage(EntityUid ent)
    {
        return TryComp(ent, out BarotraumaComponent? barotrauma) &&
               barotrauma.TakingDamage;
    }
}
