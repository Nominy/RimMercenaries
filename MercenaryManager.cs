using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimMercenaries
{
    public static class MercenaryManager
    {
        private static Dictionary<XenotypeDef, List<MercenaryOffer>> xenotypeBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();
        private static Dictionary<int, int> globalTierCounters = new Dictionary<int, int> { { 1, 10 }, { 2, 5 }, { 3, 2 } };
        private static List<MercenaryOffer> availableMercenaries = new List<MercenaryOffer>();
        private static int lastMercenaryRefreshTick = -99999;
        public static int RefreshIntervalTicks = 3600000;
        public static int LastRefreshTick => lastMercenaryRefreshTick;

        public static void ExposeData()
        {
            Scribe_Collections.Look(ref availableMercenaries, "availableMercenaries", LookMode.Deep);
            Scribe_Values.Look(ref lastMercenaryRefreshTick, "lastMercenaryRefreshTick");
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                availableMercenaries?.RemoveAll(offer => offer == null || offer.pawn == null);
                if (availableMercenaries == null)
                    availableMercenaries = new List<MercenaryOffer>();
                if (xenotypeBatches == null)
                    xenotypeBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();
            }
        }

        public static List<MercenaryOffer> GetAvailableMercenaries(XenotypeDef selectedXenotypeDef = null)
        {
            if (xenotypeBatches == null || !xenotypeBatches.Any())
                RefreshMercenariesList(Find.CurrentMap, true);

            if (xenotypeBatches == null)
                xenotypeBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();

            foreach (var batch in xenotypeBatches.Values)
                batch.RemoveAll(o => o.pawn == null || o.pawn.Destroyed || o.pawn.Dead);

            var keyToUse = selectedXenotypeDef ?? DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == "Baseliner");
            if (keyToUse != null && xenotypeBatches.TryGetValue(keyToUse, out var selectedBatch))
            {
                availableMercenaries = selectedBatch;
                return selectedBatch;
            }
            
            if (availableMercenaries == null || !availableMercenaries.Any())
            {
                var baseliner = DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == "Baseliner");
                if (baseliner != null)
                {
                    GenerateMercenaryOffersForXenotype(Find.CurrentMap, baseliner);
                    if (xenotypeBatches.TryGetValue(baseliner, out var baselinerBatch))
                    {
                        availableMercenaries = baselinerBatch;
                        return baselinerBatch;
                    }
                }
            }
            
            return availableMercenaries ?? new List<MercenaryOffer>();
        }

        public static int GetRemainingTierCount(int tier) => globalTierCounters.TryGetValue(tier, out var count) ? count : 0;

        public static Dictionary<int, int> GetAllRemainingTierCounts() => new Dictionary<int, int>(globalTierCounters);

        public static void TryRefreshMercenaries(Map map)
        {
            if (Find.TickManager.TicksGame >= lastMercenaryRefreshTick + RefreshIntervalTicks)
                RefreshMercenariesList(map, true);
        }

        public static void RefreshMercenariesList(Map map, bool forceClear = true)
        {
            if (!forceClear && Find.TickManager.TicksGame < lastMercenaryRefreshTick + RefreshIntervalTicks)
                return;
                
            foreach (var batch in xenotypeBatches.Values)
                foreach (var offer in batch)
                    if (offer?.pawn != null)
                        MercenaryOffer.DiscardPawnIfNeeded(offer.pawn);

            xenotypeBatches.Clear();
            availableMercenaries.Clear();

            globalTierCounters[1] = 10;
            globalTierCounters[2] = 5;
            globalTierCounters[3] = 2;

            GenerateMercenaryOffers(map);
            lastMercenaryRefreshTick = Find.TickManager.TicksGame;
        }

        private static void GenerateMercenaryOffers(Map map)
        {
            foreach (var xenotype in DefDatabase<XenotypeDef>.AllDefs.Where(x => x.canGenerateAsCombatant))
                GenerateMercenaryOffersForXenotype(map, xenotype);
        }
        
        private static void GenerateMercenaryOffersForXenotype(Map map, XenotypeDef xenotype)
        {
            if (xenotype == null || !xenotype.canGenerateAsCombatant) return;
            
            var offers = new List<MercenaryOffer>();
            foreach (var tier in globalTierCounters.Keys)
            {
                for (int i = 0; i < globalTierCounters[tier]; i++)
                {
                    var offer = MercenaryOfferGenerator.GenerateOffer(map, tier, xenotype);
                    if (offer != null)
                        offers.Add(offer);
                }
            }
            xenotypeBatches[xenotype] = offers;
        }

        public static bool TryHireMercenary(Map map, IntVec3 dropCell, Pawn negotiator, MercenaryOffer offer)
        {
            if (offer?.pawn == null) return false;

            int tier = 1;
            if (offer.buildType != null)
            {
                foreach (var kvp in MercenaryBuilds.Builds)
                {
                    if (kvp.Value == offer.buildType)
                    {
                        tier = kvp.Key;
                        break;
                    }
                }
            }

            if (!globalTierCounters.ContainsKey(tier) || globalTierCounters[tier] <= 0)
            {
                Messages.Message($"RimMercenaries_HireBudgetReachedTier".Translate(tier, globalTierCounters[tier]), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!TradeUtility.ColonyHasEnoughSilver(map, offer.price))
            {
                Messages.Message("RimMercenaries_NotEnoughSilver".Translate(offer.price), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            TradeUtility.LaunchSilver(map, offer.price);
            offer.pawn.SetFaction(Faction.OfPlayer);
            offer.pawn.relations?.Notify_ChangedFaction();

            var podInfo = new ActiveDropPodInfo();
            podInfo.innerContainer.TryAdd(offer.pawn);
            podInfo.leaveSlag = false;
            DropPodUtility.MakeDropPodAt(dropCell, map, podInfo);

            Messages.Message(
                "RimMercenaries_MercenaryHired".Translate(offer.pawn.LabelShortCap, negotiator.LabelShortCap, offer.price),
                (LookTargets)offer.pawn,
                MessageTypeDefOf.PositiveEvent,
                false
            );

            SoundStarter.PlayOneShot(SoundDefOf.ExecuteTrade, new TargetInfo(dropCell, map));

            foreach (var batch in xenotypeBatches.Values)
            {
                var toRemove = batch.FirstOrDefault(o => o == offer);
                if (toRemove != null)
                {
                    batch.Remove(toRemove);
                }
                else
                {
                    var sameTier = batch.Where(o => {
                        int t = 1;
                        if (o.buildType != null)
                        {
                            foreach (var kvp in MercenaryBuilds.Builds)
                            {
                                if (kvp.Value == o.buildType)
                                {
                                    t = kvp.Key;
                                    break;
                                }
                            }
                        }
                        return t == tier;
                    }).ToList();
                    if (sameTier.Any())
                        batch.Remove(sameTier[0]);
                }
            }

            availableMercenaries.Remove(offer);
            globalTierCounters[tier]--;

            if (globalTierCounters[tier] <= 0)
            {
                foreach (var batch in xenotypeBatches.Values)
                {
                    batch.RemoveAll(o => {
                        int t = 1;
                        if (o.buildType != null)
                        {
                            foreach (var kvp in MercenaryBuilds.Builds)
                            {
                                if (kvp.Value == o.buildType)
                                {
                                    t = kvp.Key;
                                    break;
                                }
                            }
                        }
                        return t == tier;
                    });
                }
            }

            return true;
        }
    }
}
