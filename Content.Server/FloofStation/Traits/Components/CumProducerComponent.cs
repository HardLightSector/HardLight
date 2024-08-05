using Content.Shared.FixedPoint;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FloofStation.Traits;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.GameStates;

namespace Content.Server.FloofStation.Traits;

[RegisterComponent, Access(typeof(LewdTraitSystem))]
public sealed partial class CumProducerComponent : Component
{
    [DataField("solutionname"), ViewVariables(VVAccess.ReadWrite)]
    public string SolutionName;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<ReagentPrototype> ReagentId = "Cum";

    [DataField]
    public FixedPoint2 MaxVolume = FixedPoint2.New(25);

    [DataField]
    public Entity<SolutionComponent>? Solution = null;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public FixedPoint2 QuantityPerUpdate = 25;

    [DataField]
    public float HungerUsage = 10f;

    [DataField]
    public TimeSpan GrowthDelay = TimeSpan.FromMinutes(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextGrowth = TimeSpan.FromSeconds(0);
}
