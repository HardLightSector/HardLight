using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._HL.Shipyard;

/// <summary>
/// Entities with this component will persist themselves and their contents when the ship is saved.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HLPersistOnShipSaveComponent : Component;
