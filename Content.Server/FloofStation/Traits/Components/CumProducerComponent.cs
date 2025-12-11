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
    [DataField("solutionname")]
    public string SolutionName = "penis";

    [DataField]
    public ProtoId<ReagentPrototype> ReagentId = "Cum";

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("maxVol")]
    public FixedPoint2 MaxVolume = FixedPoint2.New(25);

    public Entity<SolutionComponent>? Solution = null;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("reVol")]
    public FixedPoint2 QuantityPerUpdate = 5;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("reHunger")]
    public float HungerUsage = 10f;

    [DataField]
    public TimeSpan GrowthDelay = TimeSpan.FromSeconds(10);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextGrowth = TimeSpan.FromSeconds(0);
}
