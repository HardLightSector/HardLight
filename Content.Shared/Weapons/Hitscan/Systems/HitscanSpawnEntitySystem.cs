// SPDX-FileCopyrightText: 2025 Avalon
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Damage;
using Content.Shared.Explosion.EntitySystems;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Network;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed class HitscanSpawnEntitySystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanSpawnEntityComponent, HitscanRaycastFiredEvent>(
            OnHitscanHit,
            after: new[] { typeof(ReflectSystem) });
    }

    private void OnHitscanHit(Entity<HitscanSpawnEntityComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || args.HitEntity == null)
            return;

        if (_net.IsClient)
            return;

        var entity = Spawn(ent.Comp.SpawnedEntity, Transform(args.HitEntity.Value).Coordinates);

        // TODO: maybe split up the effects component or something - this wont play sounds and stuff (maybe that's ok?)
    }
}
