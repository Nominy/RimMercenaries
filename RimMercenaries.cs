using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using HarmonyLib;
using System.Reflection;
using Verse.AI;

namespace RimMercenaries
{
    [StaticConstructorOnStartup]
    public static class RimMercenaries
    {
        public static XenotypeDef selectedXenotypeDef = null;
        public static JobDef UseMercenaryConsoleJobDef;

        static RimMercenaries()
        {
            var harmony = new Harmony("rimmercenaries.commsconsolepatch");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // Cache the job def for better performance
            UseMercenaryConsoleJobDef = DefDatabase<JobDef>.GetNamed("UseMercenaryConsole");

            Log.Message("[RimMercenaries] Mod Initialized. Added Mercenary functionality to Comms Console.");
        }

        /// <summary>
        /// Safely sets the selected xenotype, with validation to ensure it exists and is valid
        /// </summary>
        public static void SetSelectedXenotype(XenotypeDef xenotype)
        {
            if (xenotype != null && !DefDatabase<XenotypeDef>.AllDefsListForReading.Contains(xenotype))
            {
                Log.Warning($"[RimMercenaries] Attempted to select invalid xenotype {xenotype.defName}, resetting to null");
                selectedXenotypeDef = null;
                return;
            }
            
            selectedXenotypeDef = xenotype;
        }

        public static void OpenMercenaryHireWindow(Building commsConsole, Pawn negotiator)
        {
            if (negotiator == null)
            {
                Messages.Message("RimMercenaries_NoNegotiator".Translate(), commsConsole, MessageTypeDefOf.RejectInput, false);
                return;
            }

            var powerComp = commsConsole.GetComp<CompPowerTrader>();
            if (powerComp != null && !powerComp.PowerOn)
            {
                Messages.Message("RimMercenaries_NoPower".Translate(), commsConsole, MessageTypeDefOf.RejectInput, false);
                return;
            }

            // This will only refresh if needed
            MercenaryManager.TryRefreshMercenaries(commsConsole.Map);
            Find.WindowStack.Add(new Window_MercenaryHire(commsConsole, negotiator));
        }
    }

    [HarmonyPatch(typeof(Building_CommsConsole), "GetCommTargets")]
    public static class CommsConsole_GetCommTargets_Patch
    {
        public static void Postfix(ref IEnumerable<ICommunicable> __result)
        {
            var resultList = __result.ToList();
            resultList.Add(new MercenaryCommTarget());
            __result = resultList;
        }
    }

    // GameComponent now handles initialization, so this patch is no longer needed
    


    [StaticConstructorOnStartup]
    public class MercenaryCommTarget : ICommunicable
    {
        private static readonly Texture2D mercIcon = FactionDefOf.Pirate.FactionIcon;

        public string CommTargetTags => "";

        public string LabelCap => "RimMercenaries_MercenaryNetwork".Translate();

        public string TargetWorldObjectLabel => "";

        public Building TargetBuilding => null;

        public string Label => "RimMercenaries_MercenaryNetwork".Translate();

        public Faction TargetFaction => null;

        public bool CanCommunicateWith => true;

        public string GetCallLabel() => "RimMercenaries_CallMercenaryNetwork".Translate();

        public string GetInfoText()
        {
            int nextRefreshIn = MercenaryManager.LastRefreshTick + MercenaryManager.RefreshIntervalTicks - Find.TickManager.TicksGame;
            
            string baseText = "RimMercenaries_MercenaryNetworkInfo".Translate() + "\n\n";
            
            return nextRefreshIn <= 0
                ? baseText + "RimMercenaries_RefreshReady".Translate()
                : baseText + "RimMercenaries_NextRefresh".Translate(nextRefreshIn.ToStringTicksToPeriod());
        }

        public void TryOpenComms(Pawn negotiator)
        {
            Building_CommsConsole console = negotiator.Map.listerBuildings.AllBuildingsColonistOfClass<Building_CommsConsole>().FirstOrDefault();
            if (console != null)
            {
                // Create a job for the pawn to go to the console
                Job job = JobMaker.MakeJob(RimMercenaries.UseMercenaryConsoleJobDef, console);
                negotiator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }

        public Faction GetFaction() => null;

        public bool ValidateTarget(out string reason)
        {
            reason = null;
            return true;
        }

        public FloatMenuOption CommFloatMenuOption(Building_CommsConsole console, Pawn negotiator)
        {
            int nextRefreshIn = MercenaryManager.LastRefreshTick + MercenaryManager.RefreshIntervalTicks - Find.TickManager.TicksGame;
            string optionLabel = "RimMercenaries_ContactMercenaryNetwork".Translate();
            Color iconColor = Color.white;
            
            if (nextRefreshIn <= 0)
            {
                optionLabel += " ✓";
                iconColor = Color.green;
            }
            
            FloatMenuOption option = new FloatMenuOption(
                optionLabel,
                () => {
                    // Create a job for the pawn to go to the console
                    Job job = JobMaker.MakeJob(RimMercenaries.UseMercenaryConsoleJobDef, console);
                    negotiator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                },
                mercIcon,
                iconColor,
                MenuOptionPriority.Default
            );
            
            return FloatMenuUtility.DecoratePrioritizedTask(option, negotiator, console);
        }
    }
}