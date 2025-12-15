// SPDX-FileCopyrightText: 2024 Aiden
// SPDX-FileCopyrightText: 2024 DrSmugleaf
// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 Toaster
// SPDX-FileCopyrightText: 2025 ark1368
//
// SPDX-License-Identifier: MIT

using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Weapons.Ranged.Prediction;

[Serializable, NetSerializable]
public sealed class PredictedProjectileHitEvent(int projectile, HashSet<(NetEntity Id, MapCoordinates Coordinates)> hit) : EntityEventArgs
{
    public readonly int Projectile = projectile;
    public readonly HashSet<(NetEntity Id, MapCoordinates Coordinates)> Hit = hit;
}

