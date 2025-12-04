using Content.Server.Botany.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server.Botany.Components;

/// <summary>
/// Component that tracks the actor/player who extracted this seed using a seed extractor.
/// Seeds with this component can only be used by the player who extracted them.
/// </summary>
[RegisterComponent, Access(typeof(SeedExtractorSystem), typeof(PlantHolderSystem))]
public sealed partial class ExtractedSeedOwnerComponent : Component
{
    /// <summary>
    /// The NetUserId of the player who extracted this seed.
    /// This tracks the player's session, not their entity, so it persists across body changes.
    /// </summary>
    [DataField]
    public NetUserId Owner;
}
