// SPDX-FileCopyrightText: Copyright (c) 2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

namespace Content.Shared._Common.CCVar;

using Robust.Shared;
using Robust.Shared.Configuration;

[CVarDefs]
public sealed partial class ConsentSystemCCVars : CVars
{
    /// <summary>
    /// How many characters the consent text can be.
    /// </summary>
    public static readonly CVarDef<int> ConsentFreetextMaxLength =
        CVarDef.Create("consent.freetext_max_length", 500, CVar.REPLICATED | CVar.SERVER);
}
