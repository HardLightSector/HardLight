// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

namespace Content.Shared._Common.Consent;

using Robust.Shared.Prototypes;

/// <summary>
/// A mechanic that can be toggled on/off by the player via the consent menu,
/// defaulting to "off." Consent toggles defined in yaml are added to the menu
/// automatically.
/// </summary>
[Prototype("consentToggle")]
public sealed partial class ConsentTogglePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The name of the consent toggle as specified in yaml. This can be
    /// overridden in Fluent with "consent-ID = Consent toggle name" where ID
    /// is the prototype ID.
    /// </summary>
    [DataField]
    public string Name = default!;

    /// <summary>
    /// The description of the toggle as specified in yaml. Can be overridden
    /// similarly to Name with "consent-ID.desc".
    /// </summary>
    [DataField]
    public string Description = default!;

    /// <summary>
    /// A key used to sort the toggles by in the consent menu.
    /// If it's not set, they are sorted by the prototype ID.
    /// </summary>
    [DataField]
    public string SortKey
    {
        get => _sortKey ?? ID;
        set => _sortKey = value;
    }

    private string? _sortKey;
}
