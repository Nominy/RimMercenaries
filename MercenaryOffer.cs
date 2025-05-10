using UnityEngine;
using Verse;

namespace RimMercenaries
{
    public class MercenaryOffer : IExposable
    {
        public Pawn pawn;
        public int price;
        public MercenaryBuild buildType;

        public MercenaryOffer() { }

        public MercenaryOffer(Pawn p, int cost, MercenaryBuild build)
        {
            pawn = p;
            price = cost;
            buildType = build;
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref price, "price");
            Scribe_Values.Look(ref buildType, "buildType");
        }

        public static int CalculatePrice(Pawn pawn, MercenaryBuild build)
        {
            float basePrice = Mathf.CeilToInt(pawn.MarketValue * 1.25f);
            basePrice = Mathf.Max(100, basePrice);

            if (build == MercenaryBuilds.Builds[1])
                basePrice = Mathf.Clamp(basePrice, 450, 750);
            else if (build == MercenaryBuilds.Builds[2])
                basePrice = Mathf.Clamp(basePrice, 950, 1300);
            else if (build == MercenaryBuilds.Builds[3])
                basePrice = Mathf.Clamp(basePrice, 1850, 2500);

            return (int)(basePrice / 50) * 50;
        }

        public static void DiscardPawnIfNeeded(Pawn pawn)
        {
            if (pawn != null && !pawn.Destroyed && !pawn.Spawned && !Find.WorldPawns.Contains(pawn))
                pawn.Discard();
        }
    }
}