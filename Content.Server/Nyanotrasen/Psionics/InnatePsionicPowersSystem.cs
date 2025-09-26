using Content.Shared.Nyanotrasen.Abilities.Psionics.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Nyanotrasen.Psionics
{
    public sealed class InnatePsionicPowersSystem : EntitySystem
    {
        [Dependency] private readonly IComponentFactory _componentFactory = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<InnatePsionicPowersComponent, ComponentStartup>(OnStartup);
        }

        private void OnStartup(EntityUid uid, InnatePsionicPowersComponent component, ComponentStartup args)
        {
            foreach (var powerName in component.PowersToAdd)
            {
                // Convert power name to component name (e.g., "TelepathyPower" becomes "TelepathyPowerComponent")
                var componentName = powerName.EndsWith("Component") ? powerName : powerName + "Component";

                try
                {
                    var powerComponent = _componentFactory.GetComponent(componentName);
                    powerComponent.Owner = uid;
                    EntityManager.AddComponent(uid, powerComponent);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to add innate psionic power {powerName} to entity {uid}: {ex.Message}");
                }
            }

            // Remove this component after adding powers to prevent re-adding on respawn
            RemComp<InnatePsionicPowersComponent>(uid);
        }
    }
}
