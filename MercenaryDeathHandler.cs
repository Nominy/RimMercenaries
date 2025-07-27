using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;
using System.Collections.Generic;

namespace RimMercenaries
{
    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class MercenaryDeathHandler
    {
        public static void Postfix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            if (__instance?.Faction != Faction.OfPlayer || !__instance.Dead)
                return;
            var mercenaryStatusDef = DefDatabase<HediffDef>.GetNamed("RimMercenaries_MercenaryStatus", false);
            if (mercenaryStatusDef == null)
            {
                Log.Warning("[RimMercenaries] RimMercenaries_MercenaryStatus hediff def not found");
                return;
            }
            
            var mercenaryHediff = __instance.health?.hediffSet?.GetFirstHediffOfDef(mercenaryStatusDef);
            if (mercenaryHediff == null)
                return;
                
            var thoughtDef = DefDatabase<ThoughtDef>.GetNamed("RimMercenaries_MercenaryDied", false);
            if (thoughtDef == null)
            {
                Log.Warning("[RimMercenaries] RimMercenaries_MercenaryDied thought def not found");
                return;
            }
            
            var targetMap = __instance.MapHeld ?? __instance.Map ?? __instance.prevMap;
            if (targetMap == null)
            {
                Log.Warning("[RimMercenaries] Could not determine map for dead mercenary");
                return;
            }
            
            var livingColonists = targetMap.mapPawns?.AllPawnsSpawned
                ?.Where(p => p.Faction == Faction.OfPlayer && 
                            p != __instance && 
                            p.RaceProps.Humanlike &&
                            !p.Dead &&
                            p.needs?.mood?.thoughts?.memories != null)
                ?.ToList();
                
            if (livingColonists == null || !livingColonists.Any())
                return;
                
            foreach (var colonist in livingColonists)
            {
                try
                {
                    var thought = (Thought_Memory)ThoughtMaker.MakeThought(thoughtDef);
                    thought.otherPawn = __instance;
                    colonist.needs.mood.thoughts.memories.TryGainMemory(thought);
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[RimMercenaries] Failed to apply mercenary death thought to {colonist.LabelShortCap}: {ex.Message}");
                }
            }
        }
    }
    
    [HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility), "AppendThoughts_ForHumanlike")]
    static class Patch_AppendThoughts_ForHumanlike
    {
        private static readonly HediffDef Merc =
            DefDatabase<HediffDef>.GetNamedSilentFail("RimMercenaries_MercenaryStatus");

        public static void Postfix(
            Pawn victim,
            List<IndividualThoughtToAdd> outIndividualThoughts)
        {
            if (Merc == null) return;
            if (victim?.health?.hediffSet?.HasHediff(Merc) != true) return;

            outIndividualThoughts.RemoveAll(t =>
                t.thought.def == ThoughtDefOf.WitnessedDeathAlly ||
                t.thought.def == ThoughtDefOf.KnowColonistDied);
        }
    }

    [HarmonyPatch(typeof(Alert_ColonistLeftUnburied), "IsCorpseOfColonist")]
    static class Patch_Alert_ColonistLeftUnburied_IsCorpseOfColonist
    {
        private static readonly HediffDef Merc =
            DefDatabase<HediffDef>.GetNamedSilentFail("RimMercenaries_MercenaryStatus");    
        public static bool Prefix(Corpse corpse, ref bool __result)
        {   
            if (corpse != null && corpse.InnerPawn?.health?.hediffSet?.HasHediff(Merc) == true)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}