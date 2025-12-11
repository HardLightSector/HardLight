using Content.Shared.Nyanotrasen.Abilities.Psionics.Components;
using Content.Shared.Abilities.Psionics;
using Content.Server.Abilities.Psionics;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Nyanotrasen.Psionics
{
    public sealed class InnatePsionicPowersSystem : EntitySystem
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

        // Use the existing psionic abilities system to add powers properly (avoids deprecated component name heuristic).
        [Dependency] private readonly PsionicAbilitiesSystem _psionic = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<InnatePsionicPowersComponent, ComponentStartup>(OnStartup);
        }

        private void OnStartup(EntityUid uid, InnatePsionicPowersComponent component, ComponentStartup args)
        {
            // Ensure base psionic component exists.
            EnsureComp<Content.Shared.Abilities.Psionics.PsionicComponent>(uid, out var psionicComp);

            foreach (var powerId in component.PowersToAdd)
            {
                if (!_prototypeManager.TryIndex<PsionicPowerPrototype>(powerId, out var proto))
                {
                    Logger.Error($"[InnatePsionics] Unknown psionic power prototype '{powerId}' on entity {uid}");
                    continue;
                }

                // Initialize via central system (handles actions, components, stats, duplicate checks).
                _psionic.InitializePsionicPower(uid, proto, psionicComp, playFeedback: false);
            }

            // Remove marker component to prevent duplicate initialization.
            RemComp<InnatePsionicPowersComponent>(uid);
        }
    }
}
