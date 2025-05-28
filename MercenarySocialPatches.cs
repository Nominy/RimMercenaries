using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace RimMercenaries
{
    /// <summary>
    /// Harmony patch to hook into social interactions
    /// </summary>
    [HarmonyPatch(typeof(InteractionWorker), "Interacted")]
    public static class InteractionWorker_Interacted_Patch
    {
        public static void Postfix(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks, out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;

            try
            {
                // Only process if both pawns are colonists
                if (initiator?.Faction == Faction.OfPlayer && recipient?.Faction == Faction.OfPlayer)
                {
                    MercenarySocialEventHandler.OnSocialInteraction(initiator, recipient);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMercenaries] Error in social interaction handler: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch to hook into social jobs completion
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_SocialRelax), "MakeNewToils")]
    public static class JobDriver_SocialRelax_Patch
    {
        public static void Postfix(JobDriver_SocialRelax __instance)
        {
            try
            {
                var pawn = __instance.pawn;
                if (pawn?.Faction == Faction.OfPlayer)
                {
                    // Find nearby colonists for social interaction
                    var nearbyColonists = pawn.Map?.mapPawns?.FreeColonistsSpawned
                        ?.Where(p => p != pawn && p.Position.DistanceTo(pawn.Position) <= 5.0f)
                        ?.ToList();

                    if (nearbyColonists != null && nearbyColonists.Any())
                    {
                        // Trigger social interaction with a random nearby colonist
                        var target = nearbyColonists.RandomElement();
                        MercenarySocialEventHandler.OnSocialInteraction(pawn, target);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMercenaries] Error in social relax handler: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Game component to handle cleanup and initialization
    /// </summary>
    public class MercenarySocialGameComponent : GameComponent
    {
        private int lastCleanupTick = 0;
        private const int CleanupInterval = 60000; // Clean up every day

        public MercenarySocialGameComponent(Game game) : base()
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            // Periodic cleanup of invalid mercenaries
            if (Find.TickManager.TicksGame - lastCleanupTick > CleanupInterval)
            {
                MercenarySocialEventHandler.CleanupInvalidMercenaries();
                lastCleanupTick = Find.TickManager.TicksGame;
            }
        }
    }
} 