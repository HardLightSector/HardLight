namespace Content.Shared._Afterlight.Atmos;

public abstract class SharedALBarotraumaSystem : EntitySystem
{
    public virtual bool IsTakingPressureDamage(EntityUid ent)
    {
        return false;
    }

    public bool CanTakePressureDamage(EntityUid ent)
    {
        var ev = new ALPressureDamageAttemptEvent();
        RaiseLocalEvent(ent, ref ev);
        return !ev.Cancelled;
    }
}
