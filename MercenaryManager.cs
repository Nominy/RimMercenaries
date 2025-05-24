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
        
        // Reserve batches for instant switching
        private static Dictionary<XenotypeDef, List<MercenaryOffer>> reserveBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();
        private static bool isGeneratingReserve = false;

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
                if (reserveBatches == null)
                    reserveBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();
            }
        }

        public static List<MercenaryOffer> GetAvailableMercenaries(XenotypeDef selectedXenotypeDef = null)
        {
            if (selectedXenotypeDef != null && !xenotypeBatches.ContainsKey(selectedXenotypeDef))
            {
                // Try to use reserve batch first
                if (reserveBatches.TryGetValue(selectedXenotypeDef, out List<MercenaryOffer> reserved))
                {
                    xenotypeBatches[selectedXenotypeDef] = reserved;
                    reserveBatches.Remove(selectedXenotypeDef);
                    availableMercenaries = reserved;
                    
                    // Generate the next reserve batch for this xenotype
                    GenerateReserveBatchForXenotype(Find.CurrentMap, selectedXenotypeDef);
                }
                else
                {
                    // Fall back to synchronous generation if reserve batch isn't available
                    GenerateMercenariesForXenotype(Find.CurrentMap, selectedXenotypeDef);
                    
                    // Start generating the reserve batch for next time
                    GenerateReserveBatchForXenotype(Find.CurrentMap, selectedXenotypeDef);
                }
            }
            else if (xenotypeBatches == null || !xenotypeBatches.Any())
            {
                var baseliner = DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == "Baseliner");
                if (baseliner != null)
                {
                    // Try to use reserve batch first
                    if (reserveBatches.TryGetValue(baseliner, out List<MercenaryOffer> reserved))
                    {
                        xenotypeBatches[baseliner] = reserved;
                        reserveBatches.Remove(baseliner);
                        availableMercenaries = reserved;
                        
                        // Generate the next reserve batch
                        GenerateReserveBatchForXenotype(Find.CurrentMap, baseliner);
                    }
                    else
                    {
                        // Fall back to synchronous generation
                        GenerateMercenariesForXenotype(Find.CurrentMap, baseliner);
                        
                        // Generate reserve batch
                        GenerateReserveBatchForXenotype(Find.CurrentMap, baseliner);
                    }
                }
            }

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
            
            return availableMercenaries ?? new List<MercenaryOffer>();
        }

        private static void GenerateMercenariesForXenotype(Map map, XenotypeDef xenotype)
        {
            if (xenotype == null) return;

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
            availableMercenaries = offers;
            lastMercenaryRefreshTick = Find.TickManager.TicksGame;
        }
        
        private static void GenerateReserveBatchForXenotype(Map map, XenotypeDef xenotype)
        {
            if (xenotype == null || reserveBatches.ContainsKey(xenotype) || isGeneratingReserve)
                return;
                
            isGeneratingReserve = true;
            
            LongEventHandler.QueueLongEvent(() => 
            {
                try
                {
                    Map currentMap = map;
                    if (currentMap == null && Find.Maps.Any())
                        currentMap = Find.Maps.First();
                        
                    if (currentMap != null)
                    {
                        var offers = new List<MercenaryOffer>();
                        
                        foreach (var tier in globalTierCounters.Keys)
                        {
                            for (int i = 0; i < globalTierCounters[tier]; i++)
                            {
                                var offer = MercenaryOfferGenerator.GenerateOffer(currentMap, tier, xenotype);
                                if (offer != null)
                                    offers.Add(offer);
                            }
                        }
                        
                        reserveBatches[xenotype] = offers;
                    }
                }
                finally
                {
                    isGeneratingReserve = false;
                }
            }, "GeneratingReserveMercenaries", false, null, true);
        }

        public static int GetRemainingTierCount(int tier) => globalTierCounters.TryGetValue(tier, out var count) ? count : 0;

        public static Dictionary<int, int> GetAllRemainingTierCounts() => new Dictionary<int, int>(globalTierCounters);

        public static void TryRefreshMercenaries(Map map)
        {
            // Only refresh if enough time has passed or we don't have any mercenaries
            if (Find.TickManager.TicksGame >= lastMercenaryRefreshTick + RefreshIntervalTicks || xenotypeBatches == null || !xenotypeBatches.Any())
            {
                // Clear existing mercenaries
                foreach (var batch in xenotypeBatches.Values)
                    foreach (var offer in batch)
                        if (offer?.pawn != null)
                            MercenaryOffer.DiscardPawnIfNeeded(offer.pawn);

                xenotypeBatches.Clear();
                availableMercenaries.Clear();

                globalTierCounters[1] = 10;
                globalTierCounters[2] = 5;
                globalTierCounters[3] = 2;

                // Get the default xenotype
                var baseliner = DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == "Baseliner");
                if (baseliner != null)
                {
                    // Use reserve batch if available
                    if (reserveBatches.TryGetValue(baseliner, out List<MercenaryOffer> reserved))
                    {
                        xenotypeBatches[baseliner] = reserved;
                        reserveBatches.Remove(baseliner);
                        availableMercenaries = reserved;
                    }
                    else
                    {
                        // Fall back to synchronous generation
                        GenerateMercenariesForXenotype(map, baseliner);
                    }
                    
                    // Always generate the next reserve batch
                    GenerateReserveBatchForXenotype(map, baseliner);
                    
                    // Also pre-generate batches for common xenotypes if Biotech is active
                    if (ModsConfig.BiotechActive)
                    {
                        var commonXenotypes = new List<string> { "Hussar", "Sanguophage", "Pigskin", "Neanderthal" };
                        foreach (var xenotypeName in commonXenotypes)
                        {
                            var xenotype = DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == xenotypeName);
                            if (xenotype != null && !reserveBatches.ContainsKey(xenotype))
                            {
                                GenerateReserveBatchForXenotype(map, xenotype);
                            }
                        }
                    }
                }
            }
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

            // ---- START new logic for finding safe drop cell ----
            IntVec3 finalDropCell = dropCell; // Original target cell from parameter

            // Define a predicate for a safe landing spot:
            // - Unroofed or under a natural (rock) roof.
            // - Physically landable without punching any roof (DropCellFinder.CanPhysicallyDropPodAt with canRoofPunch = false).
            // - Pawns must be able to path away from the landing spot (canRideToEdge = true).
            Predicate<IntVec3, Map> isSafePodSpot = (c, m) =>
                (map.roofGrid.RoofAt(c) == null || map.roofGrid.RoofAt(c).isNatural) &&
                DropCellFinder.IsGoodDropSpot(c, map, false, true);

            // Check if the originally intended dropCell is safe
            if (!isSafePodSpot(finalDropCell, map))
            {
                bool foundAlternative = false;
                // Search nearby first, in an expanding radius
                for (int radius = 1; radius <= 20; radius++) // Max search radius: 20 cells
                {
                    if (CellFinder.TryFindRandomCellNear(dropCell, map, radius, c => isSafePodSpot(c, map), out IntVec3 candidateCell))
                    {
                        finalDropCell = candidateCell;
                        foundAlternative = true;
                        break;
                    }
                }

                if (!foundAlternative)
                {
                    // If no suitable cell found nearby, try a broader search on the map
                    if (CellFinderLoose.TryGetRandomCellWith(c => isSafePodSpot(c, map), map, 1000, out IntVec3 mapWideCandidate)) // Try 1000 attempts
                    {
                        finalDropCell = mapWideCandidate;
                        Log.Warning($"[RimMercenaries] Original drop cell {dropCell} for mercenary {offer.pawn.LabelShortCap} was unsafe. Found alternative safe cell {finalDropCell} via wider search.");
                        foundAlternative = true;
                    }
                }
                
                if (!foundAlternative)
                {
                    // If still no safe cell found anywhere, abort the hiring BEFORE spending silver
                    Messages.Message("RimMercenaries_NoSafeDropLocationFound".Translate(), MessageTypeDefOf.RejectInput, false);
                    Log.Error($"[RimMercenaries] Failed to find any safe drop pod location for mercenary {offer.pawn.LabelShortCap} on map {map}. Original target: {dropCell}. Aborting hire.");
                    return false; 
                }
            }
            // ---- END new logic for finding safe drop cell ----

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
            podInfo.moveItemsAsideBeforeSpawning = true;
            podInfo.openDelay = 60; // Faster opening (default is 110)
            podInfo.spawnWipeMode = WipeMode.Vanish; // Gentler wipe mode
            DropPodUtility.MakeDropPodAt(finalDropCell, map, podInfo);

            Messages.Message(
                "RimMercenaries_MercenaryHired".Translate(offer.pawn.LabelShortCap, negotiator.LabelShortCap, offer.price),
                (LookTargets)offer.pawn,
                MessageTypeDefOf.PositiveEvent,
                false
            );

            SoundStarter.PlayOneShot(SoundDefOf.ExecuteTrade, new TargetInfo(finalDropCell, map));

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
