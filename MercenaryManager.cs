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

        /// <summary>
        /// Checks if a building is a ScribeTable from MedievalOverhaul mod
        /// </summary>
        private static bool IsScribeTable(Building building)
        {
            if (building == null) 
            {
                return false;
            }
            
            try
            {
                // Check if MedievalOverhaul is loaded
                bool isMedievalOverhaulActive = ModsConfig.IsActive("DankPyon.Medieval.Overhaul");
                
                if (!isMedievalOverhaulActive)
                {
                    return false;
                }
                
                // Check if the building is a ScribeTable
                var scribeTableType = building.GetType();
                string fullTypeName = scribeTableType.FullName;
                bool isScribeTable = fullTypeName == "MedievalOverhaul.Building_ScribeTable";
                
                return isScribeTable;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMercenaries] Error checking if building is ScribeTable: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Alternative silver checking for ScribeTable - checks colony-wide silver without orbital beacon dependency
        /// </summary>
        private static bool ColonyHasEnoughSilverForScribeTable(Map map, int amount)
        {
            if (map?.listerThings?.ThingsOfDef(ThingDefOf.Silver) == null)
                return false;
                
            int totalSilver = 0;
            
            // Count silver in all accessible areas (similar to how TradeUtility works, but without beacon restriction)
            var silverThings = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            
            foreach (var thing in silverThings)
            {
                // Check if the silver is in a player-controlled area
                bool isPlayerControlled = IsInPlayerControlledArea(map, thing);
                
                if (isPlayerControlled)
                {
                    totalSilver += thing.stackCount;
                }
            }
            
            // Also check silver carried by colonists
            var freeColonists = map.mapPawns.FreeColonists;
            
            foreach (var pawn in freeColonists)
            {
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (var item in pawn.inventory.innerContainer)
                    {
                        if (item.def == ThingDefOf.Silver)
                        {
                            totalSilver += item.stackCount;
                        }
                    }
                }
            }
            
            return totalSilver >= amount;
        }

        /// <summary>
        /// Determines if a thing is in a player-controlled area of the map
        /// </summary>
        private static bool IsInPlayerControlledArea(Map map, Thing thing)
        {
            if (thing?.Position == null || !thing.Position.IsValid || !thing.Spawned)
            {
                return false;
            }

            // Check if it's in a room that belongs to the player
            Room room = thing.Position.GetRoom(map);
            if (room != null && !room.PsychologicallyOutdoors)
            {
                // Indoor room - check if any part of the room is owned by player buildings
                foreach (var building in room.ContainedAndAdjacentThings.OfType<Building>())
                {
                    if (building.Faction == Faction.OfPlayer)
                    {
                        return true;
                    }
                }
            }

            // Check if it's in a stockpile zone
            var zone = map.zoneManager.ZoneAt(thing.Position);
            if (zone is Zone_Stockpile)
            {
                return true;
            }

            // Check if it's near player buildings (within reasonable distance)
            var nearbyBuildings = GenRadial.RadialDistinctThingsAround(thing.Position, map, 10f, false)
                .OfType<Building>()
                .Where(b => b.Faction == Faction.OfPlayer);
            
            if (nearbyBuildings.Any())
            {
                return true;
            }

            // Check if it's in the home area
            if (map.areaManager.Home[thing.Position])
            {
                return true;
            }

            // If none of the above, consider it not player-controlled
            return false;
        }

        /// <summary>
        /// Alternative silver consumption for ScribeTable - removes silver from colony without orbital beacon dependency
        /// </summary>
        private static bool ConsumeColonySilverForScribeTable(Map map, int amount)
        {
            if (!ColonyHasEnoughSilverForScribeTable(map, amount))
                return false;
                
            int remainingToConsume = amount;
            var silverThings = new List<Thing>();
            
            // Collect all silver from player-controlled areas
            var mapSilverThings = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            
            foreach (var thing in mapSilverThings)
            {
                bool isPlayerControlled = IsInPlayerControlledArea(map, thing);
                
                if (isPlayerControlled && remainingToConsume > 0)
                {
                    silverThings.Add(thing);
                }
            }
            
            // Collect silver from colonist inventories
            var freeColonists = map.mapPawns.FreeColonists;
            
            foreach (var pawn in freeColonists)
            {
                if (pawn.inventory?.innerContainer != null && remainingToConsume > 0)
                {
                    foreach (var item in pawn.inventory.innerContainer.ToList())
                    {
                        if (item.def == ThingDefOf.Silver && remainingToConsume > 0)
                        {
                            silverThings.Add(item);
                        }
                    }
                }
            }
            
            // Consume silver from collected sources
            foreach (var silverThing in silverThings)
            {
                if (remainingToConsume <= 0) 
                {
                    break;
                }
                
                int toTake = Mathf.Min(remainingToConsume, silverThing.stackCount);
                remainingToConsume -= toTake;
                
                if (toTake >= silverThing.stackCount)
                {
                    silverThing.Destroy();
                }
                else
                {
                    silverThing.stackCount -= toTake;
                }
            }
            
            return remainingToConsume <= 0;
        }

        public static bool TryHireMercenary(Map map, IntVec3 dropCell, Pawn negotiator, MercenaryOffer offer)
        {
            return TryHireMercenary(map, dropCell, negotiator, offer, null);
        }

        public static bool TryHireMercenary(Map map, IntVec3 dropCell, Pawn negotiator, MercenaryOffer offer, Building sourceBuilding)
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

            // ---- START ScribeTable-aware silver handling ----
            bool isFromScribeTable = IsScribeTable(sourceBuilding);
            
            if (isFromScribeTable)
            {
                // Use alternative silver handling for ScribeTable
                if (!ColonyHasEnoughSilverForScribeTable(map, offer.price))
                {
                    Messages.Message("RimMercenaries_NotEnoughSilver".Translate(offer.price), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                
                if (!ConsumeColonySilverForScribeTable(map, offer.price))
                {
                    Messages.Message("RimMercenaries_NotEnoughSilver".Translate(offer.price), MessageTypeDefOf.RejectInput, false);
                    Log.Error($"[RimMercenaries] Failed to consume {offer.price} silver for ScribeTable hire despite having enough - this should not happen");
                    return false;
                }
                
                Log.Message($"[RimMercenaries] Successfully consumed {offer.price} silver via ScribeTable method for {offer.pawn.LabelShortCap}");
            }
            else
            {
                // Use vanilla TradeUtility for CommsConsole
                if (!TradeUtility.ColonyHasEnoughSilver(map, offer.price))
                {
                    Messages.Message("RimMercenaries_NotEnoughSilver".Translate(offer.price), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                
                TradeUtility.LaunchSilver(map, offer.price);
            }
            // ---- END ScribeTable-aware silver handling ----

            offer.pawn.SetFaction(Faction.OfPlayer);
            offer.pawn.relations?.Notify_ChangedFaction();

            // Apply mercenary status hediff to track their status and mood
            var mercenaryStatusDef = DefDatabase<HediffDef>.GetNamed("RimMercenaries_MercenaryStatus");
            if (mercenaryStatusDef != null && offer.pawn.health?.hediffSet != null)
            {
                var existingHediff = offer.pawn.health.hediffSet.GetFirstHediffOfDef(mercenaryStatusDef);
                if (existingHediff == null)
                {
                    var mercenaryHediff = HediffMaker.MakeHediff(mercenaryStatusDef, offer.pawn);
                    offer.pawn.health.AddHediff(mercenaryHediff);
                    Log.Message($"[RimMercenaries] Applied mercenary status to {offer.pawn.LabelShortCap}");
                }
            }
            else
            {
                Log.Warning("[RimMercenaries] Could not find RimMercenaries_MercenaryStatus hediff def or pawn has no health");
            }

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
