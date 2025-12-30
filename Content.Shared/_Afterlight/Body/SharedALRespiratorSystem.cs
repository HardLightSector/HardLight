namespace Content.Shared._Afterlight.Body;

public abstract class SharedALRespiratorSystem : EntitySystem
{
    public virtual bool IsSuffocating(EntityUid ent)
    {
        return false;
    }

    public virtual void MaximizeSaturation(EntityUid ent)
    {
    }
}
