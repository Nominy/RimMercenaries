using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;

namespace RimMercenaries
{
    public class MercenaryTrader : ITrader, IThingHolder
    {
        private ThingOwner<Thing> goods;
        private Faction faction;

        // Make sure you define a TraderKindDef named "MercenaryTraderKind" in XML, or change it to an existing defName
        public TraderKindDef TraderKind => DefDatabase<TraderKindDef>.GetNamed("MercenaryTraderKind", true);

        public Faction Faction => faction;

        // This is what the trade UI displays as the trader’s name
        public string TraderName => "Mercenary Recruiter";

        public bool CanTradeNow => true; // For a console-based trader, it's always "available."

        // Typically you want silver as the currency for these transactions
        public TradeCurrency TradeCurrency => TradeCurrency.Silver;

        public IEnumerable<Thing> Goods => goods;

        // This seed affects the price calculations. Setting it random or constant is up to you
        public int RandomPriceFactorSeed { get; private set; } = Rand.Int;

        // Adjust the trade price if you want to favor or penalize the player
        public float TradePriceImprovementOffsetForPlayer => 5f;

        public IThingHolder ParentHolder => null;

        public MercenaryTrader()
        {
            faction = Faction.OfPirates;
            goods = new ThingOwner<Thing>(this);

            // Generate some merc pawns and add them to 'goods'
            for (int i = 0; i < 3; i++)
            {
                Pawn merc = GenerateRandomMerc();
                goods.TryAdd(merc);
            }
        }

        private Pawn GenerateRandomMerc()
        {
            PawnGenerationRequest req = new PawnGenerationRequest(
                PawnKindDefOf.Drifter,
                faction, // or some neutral faction
                PawnGenerationContext.NonPlayer,
                tile: -1,
                mustBeCapableOfViolence: true
            );
            return PawnGenerator.GeneratePawn(req);
        }

        // Called when the player finalizes a trade and "buys" items from the trader.
        // For pawns, we want them to join the player faction and spawn on the map.
        public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            if (toGive is Pawn pawn)
            {
                pawn.SetFaction(Faction.OfPlayer);
                if (playerNegotiator?.MapHeld != null)
                {
                    IntVec3 spawnCell = playerNegotiator.PositionHeld;
                    GenSpawn.Spawn(pawn, spawnCell, playerNegotiator.MapHeld);
                }
            }
            else
            {
                // For non-pawn items (if you ever add them to this trader)
                // they’ll drop in a pod near the player
                TradeUtility.SpawnDropPod(
                    DropCellFinder.TradeDropSpot(playerNegotiator.MapHeld),
                    playerNegotiator.MapHeld,
                    toGive
                );
            }
        }

        // Called if the player sells something to this trader
        public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            Thing splitThing = toGive.SplitOff(countToGive);
            goods.TryAdd(splitThing);
        }

        // Unused for a “hiring mercs” scenario, but must be defined.
        // Typically you’d return the things in your colony that this trader is willing to buy.
        // For simplicity, here’s an empty list to avoid errors.
        public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator)
        {
            return TradeUtility.AllLaunchableThingsForTrade(playerNegotiator.MapHeld, playerNegotiator);
        }

        // IThingHolder
        public ThingOwner GetDirectlyHeldThings() => goods;
        public void GetChildHolders(List<IThingHolder> outChildren) { }
    }
}
