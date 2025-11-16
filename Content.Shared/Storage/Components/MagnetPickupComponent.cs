// SPDX-FileCopyrightText: 2023 Stray-Pyramid
// SPDX-FileCopyrightText: 2023 crystalHex
// SPDX-FileCopyrightText: 2023 metalgearsloth
// SPDX-FileCopyrightText: 2024 Mnemotechnican
// SPDX-FileCopyrightText: 2024 Pieter-Jan Briers
// SPDX-FileCopyrightText: 2025 Dvir
// SPDX-FileCopyrightText: 2025 Redrover1760
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Inventory;
using Robust.Shared.GameStates; // Frontier

namespace Content.Shared.Storage.Components; // Frontier: Server<Shared

/// <summary>
/// Applies an ongoing pickup area around the attached entity.
/// </summary>
[RegisterComponent] // Mono - Removed AutoGenerateComponentPause
[NetworkedComponent, AutoGenerateComponentState] // Frontier
public sealed partial class MagnetPickupComponent : Component
{
    //[ViewVariables(VVAccess.ReadWrite), DataField("nextScan")] // Mono
    //[AutoPausedField]
    //public TimeSpan NextScan = TimeSpan.Zero;

    /// <summary>
    /// What container slot the magnet needs to be in to work.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("slotFlags")]
    public SlotFlags SlotFlags = SlotFlags.BELT;

    [ViewVariables(VVAccess.ReadWrite), DataField("range")]
    public float Range = 1f;

    // Frontier: togglable magnets
    /// <summary>
    /// Is the magnet currently enabled?
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadWrite), DataField]
    public bool MagnetEnabled = true;

    /// <summary>
    /// Is the magnet currently enabled?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool MagnetCanBeEnabled = true;

    /// <summary>
    /// Is the magnet currently enabled?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public int MagnetTogglePriority = 3;
    // End Frontier: togglable magnets
}
