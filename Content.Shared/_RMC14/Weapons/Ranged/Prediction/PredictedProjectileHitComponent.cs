// SPDX-FileCopyrightText: 2024 Aiden
// SPDX-FileCopyrightText: 2024 DrSmugleaf
// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 Toaster
// SPDX-FileCopyrightText: 2025 ark1368
//
// SPDX-License-Identifier: MIT

using Content.Shared.Projectiles;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._RMC14.Weapons.Ranged.Prediction;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedGunPredictionSystem), typeof(SharedProjectileSystem))]
public sealed partial class PredictedProjectileHitComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityCoordinates Origin;

    [DataField, AutoNetworkedField]
    public float Distance;
}

