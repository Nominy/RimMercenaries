using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimMercenaries
{
    public class JobDriver_UseMercenaryConsole : JobDriver
    {
        private Building_CommsConsole Console => (Building_CommsConsole)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Console, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !Console.CanUseCommsNow);
            
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            var useConsole = new Toil
            {
                initAction = () =>
                {
                    var powerComp = Console.GetComp<CompPowerTrader>();
                    if (powerComp != null && !powerComp.PowerOn)
                    {
                        Messages.Message("RimMercenaries_NoPower".Translate(), Console, MessageTypeDefOf.RejectInput, false);
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    RimMercenaries.OpenMercenaryHireWindow(Console, pawn);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return useConsole;
        }
    }
} 