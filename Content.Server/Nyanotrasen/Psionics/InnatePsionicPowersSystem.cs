using Content.Server.Abilities.Psionics;
using Content.Shared.Nyanotrasen.Abilities.Psionics.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Nyanotrasen.Psionics
{
    public sealed class InnatePsionicPowersSystem : EntitySystem
    {
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly PsionicAbilitiesSystem _psionics = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<InnatePsionicPowersComponent, ComponentStartup>(OnStartup);
        }

        private void OnStartup(EntityUid uid, InnatePsionicPowersComponent component, ComponentStartup args)
        {
            foreach (var powerId in component.PowersToAdd)
            {
                // Treat entries as PsionicPowerPrototype IDs and initialize via PsionicAbilitiesSystem
                if (_prototypeManager.TryIndex<Content.Shared.Abilities.Psionics.PsionicPowerPrototype>(powerId, out var proto))
                {
                    _psionics.InitializePsionicPower(uid, proto, playFeedback: false);
                    continue;
                }

                Logger.Error($"Failed to add innate psionic power {powerId} to entity {uid}: Unknown psionic power prototype");
            }

            // Remove this component after adding powers to prevent re-adding on respawn
            RemComp<InnatePsionicPowersComponent>(uid);
        }
    }
}
