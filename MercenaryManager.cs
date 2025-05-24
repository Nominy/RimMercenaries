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
        // Static wrapper that accesses the current game's MercenaryGameComponent
        private static MercenaryGameComponent GetComponent()
        {
            if (Current.Game == null) return null;
            return Current.Game.GetComponent<MercenaryGameComponent>();
        }

        public static int RefreshIntervalTicks => MercenaryGameComponent.RefreshIntervalTicks;
        public static int LastRefreshTick => GetComponent()?.LastRefreshTick ?? -99999;

        public static void ExposeData()
        {
            // This is no longer needed since the GameComponent handles all save/load operations
            // Keeping this method for backward compatibility in case it's called from somewhere
        }

        public static List<MercenaryOffer> GetAvailableMercenaries(XenotypeDef selectedXenotypeDef = null)
        {
            var component = GetComponent();
            if (component == null) return new List<MercenaryOffer>();
            
            return component.GetAvailableMercenariesForUI(selectedXenotypeDef);
        }

        public static int GetRemainingTierCount(int tier) 
        {
            var component = GetComponent();
            return component?.GlobalTierCounters.TryGetValue(tier, out var count) == true ? count : 0;
        }

        public static Dictionary<int, int> GetAllRemainingTierCounts() 
        {
            var component = GetComponent();
            return component?.GlobalTierCounters != null ? new Dictionary<int, int>(component.GlobalTierCounters) : new Dictionary<int, int>();
        }

        public static bool HasBeenInitialized() 
        {
            var component = GetComponent();
            return component?.HasBeenInitialized ?? false;
        }

        public static void ForceInitialization(Map map)
        {
            var component = GetComponent();
            component?.ForceInitialization(map);
        }

        public static void TryRefreshMercenaries(Map map)
        {
            var component = GetComponent();
            component?.TryRefreshMercenaries(map);
        }

        public static bool TryHireMercenary(Map map, IntVec3 dropCell, Pawn negotiator, MercenaryOffer offer)
        {
            var component = GetComponent();
            if (component == null || offer?.pawn == null) return false;

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

            if (!component.GlobalTierCounters.ContainsKey(tier) || component.GlobalTierCounters[tier] <= 0)
            {
                Messages.Message($"RimMercenaries_HireBudgetReachedTier".Translate(tier, component.GlobalTierCounters[tier]), MessageTypeDefOf.RejectInput, false);
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

            foreach (var batch in component.XenotypeBatches.Values)
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

            component.AvailableMercenaries.Remove(offer);
            component.GlobalTierCounters[tier]--;

            if (component.GlobalTierCounters[tier] <= 0)
            {
                foreach (var batch in component.XenotypeBatches.Values)
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
