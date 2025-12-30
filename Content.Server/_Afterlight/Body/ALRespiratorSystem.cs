using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Afterlight.Body;

namespace Content.Server._Afterlight.Body;

public sealed class ALRespiratorSystem : SharedALRespiratorSystem
{
    [Dependency] private readonly RespiratorSystem _respirator = default!;

    public override bool IsSuffocating(EntityUid ent)
    {
        return TryComp(ent, out RespiratorComponent? respirator) && respirator.SuffocationCycles > 0;
    }

    public override void MaximizeSaturation(EntityUid ent)
    {
        if (!TryComp(ent, out RespiratorComponent? respirator))
            return;

        _respirator.UpdateSaturation(ent, respirator.MaxSaturation + Math.Abs(respirator.MinSaturation));
    }
}
