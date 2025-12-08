using Content.Server.Administration.Logs;
using Content.Server.Destructible;
using Content.Server.Effects;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Content.Shared.StatusEffect;
using Content.Shared.Eye.Blinding.Components; // Frontier
using Content.Shared.Eye.Blinding.Systems; // Frontier
using Robust.Shared.Random; // Frontier
using Content.Server.Chat.Systems; // Frontier
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Components;
using System.Linq;
using System.Numerics;
using Content.Shared.Physics;
using Robust.Shared.Physics;

namespace Content.Server.Projectiles;

public sealed class ProjectileSystem : SharedProjectileSystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly ColorFlashEffectSystem _color = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly DestructibleSystem _destructibleSystem = default!;
    [Dependency] private readonly GunSystem _guns = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _sharedCameraRecoil = default!;

    [Dependency] private readonly StatusEffectsSystem _statusEffectsSystem = default!; // Frontier
    [Dependency] private readonly BlindableSystem _blindingSystem = default!; // Frontier
    [Dependency] private readonly IRobustRandom _random = default!; // Frontier
    [Dependency] private readonly ChatSystem _chat = default!; // Frontier

    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;

    /// <summary>
    /// Minimum velocity for a projectile to be considered for raycast hit detection.
    /// Projectiles slower than this will rely on standard StartCollideEvent.
    /// </summary>
    private const float MinRaycastVelocity = 75f; // 100->75 Mono

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ProjectileComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(EntityUid uid, ProjectileComponent component, ref StartCollideEvent args)
    {
        // This is so entities that shouldn't get a collision are ignored.
        if (args.OurFixtureId != ProjectileFixture || !args.OtherFixture.Hard
            || component.ProjectileSpent || component is { Weapon: null, OnlyCollideWhenShot: true })
            return;

        var target = args.OtherEntity;
        // it's here so this check is only done once before possible hit
        var attemptEv = new ProjectileReflectAttemptEvent(uid, component, false);
        RaiseLocalEvent(target, ref attemptEv);
        if (attemptEv.Cancelled)
        {
            SetShooter(uid, component, target);
            return;
        }

        var ev = new ProjectileHitEvent(component.Damage * _damageableSystem.UniversalProjectileDamageModifier, target, component.Shooter);
        RaiseLocalEvent(uid, ref ev);

        var otherName = ToPrettyString(target);
        var damageRequired = _destructibleSystem.DestroyedAt(target);
        if (TryComp<DamageableComponent>(target, out var damageableComponent))
        {
            damageRequired -= damageableComponent.TotalDamage;
            damageRequired = FixedPoint2.Max(damageRequired, FixedPoint2.Zero);
        }
        var modifiedDamage = _damageableSystem.TryChangeDamage(target, ev.Damage, component.IgnoreResistances, damageable: damageableComponent, origin: component.Shooter);
        var deleted = Deleted(target);

        if (modifiedDamage is not null && EntityManager.EntityExists(component.Shooter))
        {
            if (modifiedDamage.AnyPositive() && !deleted)
            {
                _color.RaiseEffect(Color.Red, new List<EntityUid> { target }, Filter.Pvs(target, entityManager: EntityManager));
            }

            _adminLogger.Add(LogType.BulletHit,
                LogImpact.Medium,
                $"Projectile {ToPrettyString(uid):projectile} shot by {ToPrettyString(component.Shooter!.Value):user} hit {otherName:target} and dealt {modifiedDamage.GetTotal():damage} damage");
        }

        // If penetration is to be considered, we need to do some checks to see if the projectile should stop.
        if (modifiedDamage is not null && component.PenetrationThreshold != 0)
        {
            // If a damage type is required, stop the bullet if the hit entity doesn't have that type.
            if (component.PenetrationDamageTypeRequirement != null)
            {
                var stopPenetration = false;
                foreach (var requiredDamageType in component.PenetrationDamageTypeRequirement)
                {
                    if (!modifiedDamage.DamageDict.Keys.Contains(requiredDamageType))
                    {
                        stopPenetration = true;
                        break;
                    }
                }
                if (stopPenetration)
                    component.ProjectileSpent = true;
            }

            // If the object won't be destroyed, it "tanks" the penetration hit.
            if (modifiedDamage.GetTotal() < damageRequired)
            {
                component.ProjectileSpent = true;
            }

            if (!component.ProjectileSpent)
            {
                component.PenetrationAmount += damageRequired;
                // The projectile has dealt enough damage to be spent.
                if (component.PenetrationAmount >= component.PenetrationThreshold)
                {
                    component.ProjectileSpent = true;
                }
            }
        }
        else
        {
            component.ProjectileSpent = true;
        }

        if (!deleted)
        {
            _guns.PlayImpactSound(target, modifiedDamage, component.SoundHit, component.ForceSound);

            if (!args.OurBody.LinearVelocity.IsLengthZero())
                _sharedCameraRecoil.KickCamera(target, args.OurBody.LinearVelocity.Normalized());
        }

        if (component.DeleteOnCollide && component.ProjectileSpent)
            QueueDel(uid);

        if (component.ImpactEffect != null && TryComp(uid, out TransformComponent? xform))
        {
            RaiseNetworkEvent(new ImpactEffectEvent(component.ImpactEffect, GetNetCoordinates(xform.Coordinates)), Filter.Pvs(xform.Coordinates, entityMan: EntityManager));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ProjectileComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var projectileComp, out var physicsComp, out var xform))
        {
            if (projectileComp.ProjectileSpent)
                continue;

            var currentVelocity = physicsComp.LinearVelocity;
            if (currentVelocity.Length() < MinRaycastVelocity)
                continue;

            var lastPosition = _transformSystem.GetWorldPosition(xform, GetEntityQuery<TransformComponent>());
            var rayDirection = currentVelocity.Normalized();
            // Ensure rayDistance is not zero to prevent issues with IntersectRay if frametime or velocity is zero.
            var rayDistance = currentVelocity.Length() * frameTime;
            if (rayDistance <= 0f)
                continue;

            // Define a collision mask appropriate for projectiles.
            // This should generally match what projectiles are expected to collide with.
            // Using Impassable, BulletImpassable, and Opaque as a baseline. - Mono
            const int collisionMask = (int)(CollisionGroup.Impassable | CollisionGroup.BulletImpassable | CollisionGroup.Opaque); // Replaces CollisonGroup.MobMask with CollisionGroup.BulletImpassable - Mono

            var hits = _physics.IntersectRay(xform.MapID,
                new CollisionRay(lastPosition, rayDirection, collisionMask),
                rayDistance,
                uid, // Entity to ignore (self)
                false) // IncludeNonHard = false
                .ToList();

            // If IgnoreShooter is true, remove the shooter from the list of potential hits.
            if (projectileComp.IgnoreShooter && projectileComp.Shooter.HasValue)
            {
                hits.RemoveAll(hit => hit.HitEntity == projectileComp.Shooter.Value);
            }

            if (hits.Count > 0)
            {
                // Process the closest hit
                // IntersectRay results are not guaranteed to be sorted by distance, so we sort them.
                hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                var closestHit = hits.First();

                var hitEntity = closestHit.HitEntity;
                var hitDistance = closestHit.Distance;
                var hitPosition = lastPosition + rayDirection * hitDistance;

                // --- Begin Collision Logic (adapted from OnStartCollide) ---
                // Check for reflection
                var attemptEv = new ProjectileReflectAttemptEvent(uid, projectileComp, false);
                RaiseLocalEvent(hitEntity, ref attemptEv);
                if (attemptEv.Cancelled)
                {
                    // Handle reflection - e.g., change shooter, reverse velocity (simplified here)
                    // This part might need more sophisticated logic based on how reflection is meant to work.
                    SetShooter(uid, projectileComp, hitEntity);
                    _physics.SetLinearVelocity(uid, -currentVelocity, body: physicsComp);
                    // Potentially change angle if your projectile component uses it for orientation
                    if (TryComp<TransformComponent>(uid, out var projXform))
                        _transformSystem.SetLocalRotation(projXform, currentVelocity.ToAngle() + new Angle(MathF.PI));
                    continue; // Done with this projectile if reflected
                }

                var damageable = CompOrNull<DamageableComponent>(hitEntity);
                var ev = new ProjectileHitEvent(projectileComp.Damage * _damageableSystem.UniversalProjectileDamageModifier, hitEntity, projectileComp.Shooter);
                RaiseLocalEvent(uid, ref ev); // Raise on the projectile itself

                if (projectileComp.RandomBlindChance > 0.0f && _random.Prob(projectileComp.RandomBlindChance)) // Frontier
                {
                    TryBlind(hitEntity);
                }

                var otherName = ToPrettyString(hitEntity);
                var damageRequired = _destructibleSystem.DestroyedAt(hitEntity);
                if (damageable != null) // Replicating original logic from OnStartCollide
                {
                    damageRequired -= damageable.TotalDamage;
                    damageRequired = FixedPoint2.Max(damageRequired, FixedPoint2.Zero);
                }
                var modifiedDamage = _damageableSystem.TryChangeDamage(hitEntity, ev.Damage, projectileComp.IgnoreResistances, damageable: damageable, origin: projectileComp.Shooter);
                var deleted = Deleted(hitEntity);

                if (modifiedDamage != null && EntityManager.EntityExists(projectileComp.Shooter))
                {
                    if (modifiedDamage.AnyPositive() && !deleted)
                    {
                        _color.RaiseEffect(Color.Red, new List<EntityUid> { hitEntity }, Filter.Pvs(hitEntity, entityManager: EntityManager));
                    }

                    _adminLogger.Add(LogType.BulletHit,
                        HasComp<ActorComponent>(hitEntity) ? LogImpact.Extreme : LogImpact.High,
                        $"Projectile {ToPrettyString(uid):projectile} (raycast) shot by {ToPrettyString(projectileComp.Shooter!.Value):user} hit {otherName:target} and dealt {modifiedDamage.GetTotal():damage} damage");
                }

                // Penetration Logic
                if (modifiedDamage != null && projectileComp.PenetrationThreshold != FixedPoint2.Zero)
                {
                    if (projectileComp.PenetrationDamageTypeRequirement != null)
                    {
                        var stopPenetration = false;
                        foreach (var requiredDamageType in projectileComp.PenetrationDamageTypeRequirement)
                        {
                            if (!modifiedDamage.DamageDict.ContainsKey(requiredDamageType))
                            {
                                stopPenetration = true;
                                break;
                            }
                        }
                        if (stopPenetration)
                            projectileComp.ProjectileSpent = true;
                    }

                    if (modifiedDamage.GetTotal() < damageRequired) // Not enough damage to destroy part/entity
                    {
                        projectileComp.ProjectileSpent = true;
                    }

                    if (!projectileComp.ProjectileSpent)
                    {
                        projectileComp.PenetrationAmount += damageRequired;
                        if (projectileComp.PenetrationAmount >= projectileComp.PenetrationThreshold)
                        {
                            projectileComp.ProjectileSpent = true;
                        }
                    }
                }
                else
                {
                    projectileComp.ProjectileSpent = true;
                }
                Dirty(uid, projectileComp); // Mark component as dirty if ProjectileSpent or PenetrationAmount changed

                // Post-hit effects if projectile is spent
                if (projectileComp.ProjectileSpent)
                {
                    if (!deleted)
                    {
                        _guns.PlayImpactSound(hitEntity, modifiedDamage, projectileComp.SoundHit, projectileComp.ForceSound);
                        // Camera kick is tricky without the StartCollideEvent's body info directly.
                        // We can use the projectile's velocity before it's zeroed.
                        if (currentVelocity.LengthSquared() > 0f)
                           _sharedCameraRecoil.KickCamera(hitEntity, currentVelocity.Normalized());
                    }

                    // Move projectile to exact hit point
                    _transformSystem.SetWorldPosition(xform, hitPosition);
                    // Stop the projectile completely
                    _physics.SetLinearVelocity(uid, Vector2.Zero, body: physicsComp);
                    _physics.SetAngularVelocity(uid, 0, body: physicsComp);
                    // Prevent further physics interactions by making it non-hard or disabling fixtures if necessary (optional)
                    // For now, zero velocity and ProjectileSpent should be sufficient.

                    if (projectileComp.DeleteOnCollide)
                        QueueDel(uid);

                    if (projectileComp.ImpactEffect != null)
                    {
                        // Ensure the projectile is at the hit location for the effect
                        RaiseNetworkEvent(new ImpactEffectEvent(projectileComp.ImpactEffect, GetNetCoordinates(xform.Coordinates)), Filter.Pvs(xform.Coordinates, entityMan: EntityManager));
                    }
                }
            }
        }
    }

    private void TryBlind(EntityUid target) // Frontier - bb make you go blind
    {
        if (!TryComp<BlindableComponent>(target, out var blindable) || blindable.IsBlind)
            return;

        var eyeProtectionEv = new GetEyeProtectionEvent();
        RaiseLocalEvent(target, eyeProtectionEv);

        var time = (float)(TimeSpan.FromSeconds(2) - eyeProtectionEv.Protection).TotalSeconds;
        if (time <= 0)
            return;

        var emoteId = "Scream";
        _chat.TryEmoteWithoutChat(target, emoteId);

        // Add permanent eye damage if they had zero protection, also somewhat scale their temporary blindness by
        // how much damage they already accumulated.
        _blindingSystem.AdjustEyeDamage((target, blindable), 1);
        var statusTimeSpan = TimeSpan.FromSeconds(time * MathF.Sqrt(blindable.EyeDamage));
        _statusEffectsSystem.TryAddStatusEffect(target, TemporaryBlindnessSystem.BlindingStatusEffect,
            statusTimeSpan, false, TemporaryBlindnessSystem.BlindingStatusEffect);
    }
}
