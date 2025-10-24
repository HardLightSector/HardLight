// SPDX-FileCopyrightText: Copyright (c) 2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Robust.Shared.Prototypes;

namespace Content.Shared._Common.Consent;

/// <summary>
/// Raised after an entity's consent toggle changes state.
/// If multiple toggles changed, it is raised once for each toggle.
/// </summary>
[ByRefEvent]
public readonly record struct EntityConsentToggleUpdatedEvent
{
    public Entity<ConsentComponent> Ent { get; init; }
    public ProtoId<ConsentTogglePrototype> ConsentToggleProtoId { get; init; }
    public string? OldState { get; init; }
    public string? NewState { get; init; }
}
