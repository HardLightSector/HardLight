using Content.Shared.Database._Afterlight;
using Robust.Shared.Prototypes;

namespace Content.Shared._Afterlight.Kinks;

public readonly record struct KinkSetting(EntProtoId<KinkDefinitionComponent> Kink, KinkPreference Preference);
