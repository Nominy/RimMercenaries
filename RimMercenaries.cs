using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimMercenaries
{
    public class Building_MercenaryConsole : Building
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
            {
                yield return g;
            }

            yield return new Command_Action
            {
                defaultLabel = "Hire Mercenaries",
                defaultDesc = "Contact mercenaries via the trading interface",
                action = () =>
                {
                    Pawn negotiator = FindBestNegotiator();
                    if (negotiator == null)
                    {
                        Messages.Message("No negotiator available!", MessageTypeDefOf.RejectInput);
                        return;
                    }

                    // Create our merc-trader
                    var mercTrader = new MercenaryTrader();

                    // Open standard trade dialog
                    Find.WindowStack.Add(new Dialog_Trade(negotiator, mercTrader));
                }
            };
        }

        private Pawn FindBestNegotiator()
        {
            if (Map == null) return null;

            // Example: pick the colonist with the highest Social skill
            return Map.mapPawns.FreeColonistsSpawned
                       .Where(p => !p.Dead && !p.Downed)
                       .OrderByDescending(p => p.skills.GetSkill(SkillDefOf.Social).Level)
                       .FirstOrDefault();
        }

    }

    [StaticConstructorOnStartup]
    public static class RimMercenaries
    {
        static RimMercenaries()
        {
            var harmony = new Harmony("rimMercenaries");
            harmony.PatchAll();
            Log.Message("RimMercenaries mod initialized");
        }
    }
}

