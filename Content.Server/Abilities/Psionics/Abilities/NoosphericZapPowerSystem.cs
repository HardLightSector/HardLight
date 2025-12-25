using Content.Shared.Abilities.Psionics;
using Content.Shared.Nyanotrasen.Abilities.Psionics;
using Content.Server.Projectiles;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Actions.Events;
using Robust.Shared.Map;

namespace Content.Server.Abilities.Psionics
{
    public sealed class NoosphericZapPowerSystem : EntitySystem
    {
        [Dependency] private readonly SharedPsionicAbilitiesSystem _psionics = default!;
        [Dependency] private readonly ProjectileSystem _projectile = default!;
        [Dependency] private readonly GunSystem _gun = default!;


        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<NoosphericZapPowerActionEvent>(OnPowerUsed);
        }

        private void OnPowerUsed(NoosphericZapPowerActionEvent args)
        {
            if (!_psionics.OnAttemptPowerUse(args.Performer, "noospheric zap"))
                return;

            // Spawn and shoot a TeslaGunBullet projectile
            var xform = Transform(args.Performer);
            var targetXform = Transform(args.Target);
            
            var projectile = Spawn("TeslaGunBullet", xform.Coordinates);
            var proj = EnsureComp<ProjectileComponent>(projectile);
            _projectile.SetShooter(projectile, proj, args.Performer);

            // Shoot the projectile towards the target
            var targetCoords = new EntityCoordinates(args.Target, targetXform.LocalPosition);
            _gun.ShootProjectile(projectile, targetCoords.Position - xform.LocalPosition, xform.LocalPosition, args.Performer);

            _psionics.LogPowerUsed(args.Performer, "noospheric zap");
            args.Handled = true;
        }
    }
}
