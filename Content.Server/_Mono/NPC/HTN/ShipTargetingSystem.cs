using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using System.Numerics;

namespace Content.Server._Mono.NPC.HTN;

public sealed partial class ShipTargetingSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedGunSystem _gunSystem = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;

    private EntityQuery<GunComponent> _gunQuery;
    private EntityQuery<PhysicsComponent> _physQuery;

    public override void Initialize()
    {
        base.Initialize();

        _gunQuery = GetEntityQuery<GunComponent>();
        _physQuery = GetEntityQuery<PhysicsComponent>();
    }

    // have to use this because RT's is broken and unusable for navigation
    // another algorithm stolen from myself from orbitfight
    public Angle ShortestAngleDistance(Angle from, Angle to)
    {
        var diff = (to - from) % Math.Tau;
        return diff + Math.Tau * (diff < -Math.PI ? 1 : diff > Math.PI ? -1 : 0);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ShipTargetingComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            var pilotXform = Transform(uid);

            var shipUid = pilotXform.GridUid;

            var target = comp.Target;
            var targetUid = target.EntityId; // if we have a target try to lead it
            var targetGrid = Transform(targetUid).GridUid;

            if (shipUid == null
                || TerminatingOrDeleted(targetUid)
                || !_physQuery.TryComp(shipUid, out var shipBody)
            )
                continue;

            var shipXform = Transform(shipUid.Value);

            var mapTarget = _transform.ToMapCoordinates(target);
            var shipPos = _transform.GetMapCoordinates(shipXform);

            // we or target might just be in FTL so don't count us as finished
            if (mapTarget.MapId != shipPos.MapId)
                continue;

            var linVel = shipBody.LinearVelocity;
            var targetVel = targetGrid == null ? Vector2.Zero : _physics.GetMapLinearVelocity(targetGrid.Value);
            var leadBy = 1f - MathF.Pow(1f - comp.LeadingAccuracy, frameTime);
            comp.CurrentLeadingVelocity = Vector2.Lerp(comp.CurrentLeadingVelocity, targetVel, leadBy);
            var relVel = comp.CurrentLeadingVelocity - linVel;

            FireWeapons(shipUid.Value, comp.Cannons, mapTarget, relVel);
        }
    }

    private void FireWeapons(EntityUid shipUid, List<EntityUid> cannons, MapCoordinates destMapPos, Vector2 leadBy)
    {
        var shipPos = _transform.GetMapCoordinates(shipUid);

        var toDestVec = destMapPos.Position - shipPos.Position;
        var toDestDir = NormalizedOrZero(toDestVec);

        foreach (var uid in cannons)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            var targetPos = destMapPos.Position;
            if (!_gunQuery.TryComp(uid, out var gun))
                continue;

            var projVel = gun.ProjectileSpeedModified;
            var normVel = toDestDir * Vector2.Dot(leadBy, toDestDir);
            var tgVel = leadBy - normVel;
            // going too fast to the side, we can't possibly hit it
            if (tgVel.Length() > projVel)
                continue;

            var normTarget = toDestDir * MathF.Sqrt(projVel * projVel - tgVel.LengthSquared());
            // going too fast away, we can't hit it
            if (Vector2.Dot(normTarget, normVel) > 0f && normVel.Length() > normTarget.Length())
                continue;

            var approachVel = (normTarget - normVel).Length();
            var hitTime = toDestVec.Length() / approachVel;

            targetPos += leadBy * hitTime;

            // Fire the gun forward (simplified - just shoot in the direction we're facing)
            var gunXform = Transform(uid);
            var forwardPos = gunXform.Coordinates.Offset(gunXform.LocalRotation.ToWorldVec() * 50f);
            _gunSystem.AttemptShoot(uid, uid, gun, forwardPos);
        }
    }

    public Vector2 NormalizedOrZero(Vector2 vec)
    {
        return vec.LengthSquared() == 0 ? Vector2.Zero : vec.Normalized();
    }

    /// <summary>
    /// Adds the AI to the steering system to move towards a specific target.
    /// Returns null on failure.
    /// </summary>
    public ShipTargetingComponent? Target(Entity<ShipTargetingComponent?> ent, EntityCoordinates coordinates, bool checkGuns = true)
    {
        var xform = Transform(ent);
        var shipUid = xform.GridUid;
        if (!TryComp<MapGridComponent>(shipUid, out var grid))
            return null;

        if (!Resolve(ent, ref ent.Comp, false))
            ent.Comp = AddComp<ShipTargetingComponent>(ent);

        ent.Comp.Target = coordinates;

        if (checkGuns)
        {   //Find all guns on the ship grid with the AIShipWeapon tag
            ent.Comp.Cannons.Clear();
            var guns = new HashSet<Entity<GunComponent>>();
            _lookup.GetGridEntities(shipUid.Value, guns);
            foreach (var gun in guns)
            {
                // Only add guns with the AIShipWeapon tag
                if (_tagSystem.HasTag(gun, "AIShipWeapon"))
                {
                    ent.Comp.Cannons.Add(gun);
                }
            }
        }

        return ent.Comp;
    }

    /// <summary>
    /// Stops the steering behavior for the AI and cleans up.
    /// </summary>
    public void Stop(Entity<ShipTargetingComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        RemComp<ShipTargetingComponent>(ent);
    }
}
