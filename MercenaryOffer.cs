using UnityEngine;
using Verse;

namespace RimMercenaries
{
    public class MercenaryOffer : IExposable
    {
        public Pawn pawn;
        public int price;
        public MercenaryBuild buildType;
        
        // Helper field for saving/loading - stores the tier number instead of the build object
        private int buildTier = 1;

        public MercenaryOffer() { }

        public MercenaryOffer(Pawn p, int cost, MercenaryBuild build)
        {
            pawn = p;
            price = cost;
            buildType = build;
            
            // Store the tier number for saving
            if (build != null)
            {
                foreach (var kvp in MercenaryBuilds.Builds)
                {
                    if (kvp.Value == build)
                    {
                        buildTier = kvp.Key;
                        break;
                    }
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref price, "price");
            Scribe_Values.Look(ref buildTier, "buildTier", 1);
            
            // Reconstruct the buildType reference from the tier number when loading
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (MercenaryBuilds.Builds.TryGetValue(buildTier, out MercenaryBuild build))
                {
                    buildType = build;
                }
                else
                {
                    Log.Warning($"[RimMercenaries] Invalid build tier {buildTier} found in save, defaulting to tier 1");
                    buildType = MercenaryBuilds.Builds[1];
                    buildTier = 1;
                }
            }
            
            // Store the tier number when saving
            if (Scribe.mode == LoadSaveMode.Saving && buildType != null)
            {
                foreach (var kvp in MercenaryBuilds.Builds)
                {
                    if (kvp.Value == buildType)
                    {
                        buildTier = kvp.Key;
                        break;
                    }
                }
            }
        }

        public static int CalculatePrice(Pawn pawn, MercenaryBuild build)
        {
            float basePrice = Mathf.CeilToInt(pawn.MarketValue * 1.25f);
            basePrice = Mathf.Max(100, basePrice);

            var set = RimMercenariesMod.ActiveSettings;
            if (build == MercenaryBuilds.Builds[1])
                basePrice = Mathf.Clamp(basePrice, set.tier1Price.min, set.tier1Price.max);
            else if (build == MercenaryBuilds.Builds[2])
                basePrice = Mathf.Clamp(basePrice, set.tier2Price.min, set.tier2Price.max);
            else if (build == MercenaryBuilds.Builds[3])
                basePrice = Mathf.Clamp(basePrice, set.tier3Price.min, set.tier3Price.max);

            return (int)(basePrice / 50) * 50;
        }

        public static void DiscardPawnIfNeeded(Pawn pawn)
        {
            if (pawn != null && !pawn.Destroyed && !pawn.Spawned && !Find.WorldPawns.Contains(pawn))
                pawn.Discard();
        }
    }
}