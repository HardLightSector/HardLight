using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Construction;
using JetBrains.Annotations;
using Serilog;

namespace Content.Server.Construction.Completions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class EmptySolution : IGraphAction
    {

        [DataField("solution")]
        public string Solution { get; private set; } = string.Empty;

        /// <summary>
        ///     Whether or not the solution spills on the ground.
        /// </summary>
        [DataField("spill")]
        public bool Spill = false;

        public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
        {
            var solutionContainers = entityManager.EntitySysManager.GetEntitySystem<SharedSolutionContainerSystem>();

            if (!solutionContainers.TryGetSolution(uid, Solution, out _, out var solution))
            {
                return;
            }

            if (Spill)
            {
                var puddles = entityManager.EntitySysManager.GetEntitySystem<PuddleSystem>();

                if (puddles.TrySpillAt(uid, solution, out _))
                    return;
            }

            solution.RemoveAllSolution();
        }
    }
}
