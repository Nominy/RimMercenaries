using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace RimMercenaries
{
    /// <summary>
    /// Handles social interaction events for mercenaries to trigger potential conversion to settlers
    /// </summary>
    public static class MercenarySocialEventHandler
    {
        private static HashSet<Hediff_MercenaryStatus> registeredMercenaries = new HashSet<Hediff_MercenaryStatus>();

        public static void RegisterMercenary(Hediff_MercenaryStatus mercenary)
        {
            if (mercenary != null)
            {
                registeredMercenaries.Add(mercenary);
            }
        }

        public static void UnregisterMercenary(Hediff_MercenaryStatus mercenary)
        {
            if (mercenary != null)
            {
                registeredMercenaries.Remove(mercenary);
            }
        }

        /// <summary>
        /// Called when a social interaction occurs. Checks if any registered mercenaries participated.
        /// </summary>
        public static void OnSocialInteraction(Pawn initiator, Pawn recipient)
        {
            // Check if initiator is a registered mercenary
            var initiatorMercenary = registeredMercenaries.FirstOrDefault(m => m.pawn == initiator);
            if (initiatorMercenary != null && initiatorMercenary.IsEligibleForConversion())
            {
                initiatorMercenary.OnSocialInteraction();
            }

            // Check if recipient is a registered mercenary
            var recipientMercenary = registeredMercenaries.FirstOrDefault(m => m.pawn == recipient);
            if (recipientMercenary != null && recipientMercenary.IsEligibleForConversion())
            {
                recipientMercenary.OnSocialInteraction();
            }
        }

        /// <summary>
        /// Clean up any null or invalid references
        /// </summary>
        public static void CleanupInvalidMercenaries()
        {
            registeredMercenaries.RemoveWhere(m => m == null || m.pawn == null || m.pawn.Dead || m.pawn.Destroyed);
        }
    }

    public class Hediff_MercenaryStatus : HediffWithComps
    {
        private int hiredTick = -1;

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            hiredTick = Find.TickManager.TicksGame;

            MercenarySocialEventHandler.RegisterMercenary(this);
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            // Unregister from social interaction events
            MercenarySocialEventHandler.UnregisterMercenary(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref hiredTick, "hiredTick", -1);
        }

        /// <summary>
        /// Called by the social event handler when this mercenary participates in a social interaction
        /// </summary>
        public void OnSocialInteraction()
        {
            // Only process if enough time has passed since hiring (1 year)
            if (Find.TickManager.TicksGame - hiredTick < RimMercenariesMod.ActiveSettings.mercenaryConversionPeriodDays * 60000)
                return;
            if (!RimMercenariesMod.ActiveSettings.mercenaryConversionEnabled)
                return;
                
            // Roll for conversion
            if (Rand.Chance(RimMercenariesMod.ActiveSettings.mercenaryConversionChance))
            {
                ConvertToSettler();
            }
        }
        private void ConvertToSettler()
        {
            // Fire the incident event before making changes
            var incidentDef = DefDatabase<IncidentDef>.GetNamed("RimMercenaries_MercenaryAccepted", false);
            if (incidentDef != null && pawn.Map != null)
            {
                // Send the letter directly using the incident definition with translated text
                Find.LetterStack.ReceiveLetter(
                    "RimMercenaries_MercenaryAcceptedEventLabel".Translate(),
                    "RimMercenaries_MercenaryAcceptedEventText".Translate(pawn.LabelShortCap),
                    LetterDefOf.PositiveEvent,
                    pawn
                );
            }
            else
            {
                Log.Warning("[RimMercenaries] Could not find RimMercenaries_MercenaryAccepted incident def or pawn has no map");
                
                // Fallback to simple message if incident def is missing
                Messages.Message(
                    "RimMercenaries_MercenaryBecameSettler".Translate(pawn.LabelShortCap),
                    (LookTargets)pawn,
                    MessageTypeDefOf.PositiveEvent,
                    false
                );
            }
            
            // Remove this hediff (which will also unregister from events via PostRemoved)
            pawn.health.RemoveHediff(this);
            
            // Remove the burden of mercenary thought
            var burdenThought = DefDatabase<ThoughtDef>.GetNamed("RimMercenaries_BurdenOfMercenary", false);
            if (burdenThought != null)
            {
                pawn.needs.mood.thoughts.memories.RemoveMemoriesOfDef(burdenThought);
            }
            
            // Add the acceptance thought
            var acceptanceThought = DefDatabase<ThoughtDef>.GetNamed("RimMercenaries_AcceptedBySettlement", false);
            if (acceptanceThought != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(acceptanceThought);
            }
            
            Log.Message($"[RimMercenaries] {pawn.LabelShortCap} has been accepted as a settler after {(Find.TickManager.TicksGame - hiredTick).ToStringTicksToPeriod()}");
        }

        public bool IsEligibleForConversion()
        {
            return Find.TickManager.TicksGame - hiredTick >= RimMercenariesMod.ActiveSettings.mercenaryConversionPeriodDays * 60000;
        }

        public override string LabelInBrackets
        {
            get
            {
                if (hiredTick == -1) return base.LabelInBrackets;
                
                int ticksSinceHired = Find.TickManager.TicksGame - hiredTick;
                if (ticksSinceHired < RimMercenariesMod.ActiveSettings.mercenaryConversionPeriodDays * 60000)
                {
                    int ticksUntilEligible = RimMercenariesMod.ActiveSettings.mercenaryConversionPeriodDays * 60000 - ticksSinceHired;
                    return "RimMercenaries_MercenaryStatusEligibleIn".Translate(ticksUntilEligible.ToStringTicksToPeriod());
                }
                else
                {
                    return "RimMercenaries_MercenaryStatusEligible".Translate();
                }
            }
        }
    }
}