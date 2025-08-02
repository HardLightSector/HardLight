using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Afterlight.CCVar;

[CVarDefs]
public sealed class ALCVars : CVars
{
    // Taken from https://github.com/RMC-14/RMC-14
    public static readonly CVarDef<float> VolumeGainCassettes =
        CVarDef.Create("al.volume_gain_cassettes", 0.33f, CVar.REPLICATED | CVar.CLIENT | CVar.ARCHIVE);
}
