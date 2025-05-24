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
            
            // Go to the console
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            
            // Use the console
            Toil useConsole = new Toil();
            useConsole.initAction = () =>
            {
                // Check if console has power
                var powerComp = Console.GetComp<CompPowerTrader>();
                if (powerComp != null && !powerComp.PowerOn)
                {
                    Messages.Message("RimMercenaries_NoPower".Translate(), Console, MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Open the mercenary hire window with this pawn as negotiator
                RimMercenaries.OpenMercenaryHireWindow(Console, pawn);
            };
            useConsole.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return useConsole;
        }
    }
} 