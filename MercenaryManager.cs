using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Reflection;

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
                // Skip null or destroyed items
                if (thing == null || thing.Destroyed || !thing.Spawned)
                {
                    continue;
                }

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
                        // Skip null or destroyed items
                        if (item == null || item.Destroyed || item.def != ThingDefOf.Silver)
                        {
                            continue;
                        }

                        if (remainingToConsume > 0)
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

                // Double-check the item is still valid before processing
                if (silverThing == null || silverThing.Destroyed)
                {
                    continue;
                }

                int toTake = Mathf.Min(remainingToConsume, silverThing.stackCount);
                remainingToConsume -= toTake;

                if (toTake >= silverThing.stackCount)
                {
                    // Final validation before destroying
                    if (!silverThing.Destroyed)
                    {
                        silverThing.Destroy();
                    }
                }
                else
                {
                    // Only modify stack count if item is still valid
                    if (!silverThing.Destroyed && silverThing.stackCount >= toTake)
                    {
                        silverThing.stackCount -= toTake;
                    }
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
            return TryHireMercenary(map, dropCell, negotiator, offer, sourceBuilding, null, offer?.price ?? 0);
        }

        public static bool TryHireMercenary(Map map, IntVec3 dropCell, Pawn negotiator, MercenaryOffer offer, Building sourceBuilding, MercenaryLoadoutSelection loadout, int finalPrice)
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
                if (!ColonyHasEnoughSilverForScribeTable(map, finalPrice))
                {
                    Messages.Message("RimMercenaries_NotEnoughSilver".Translate(finalPrice), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                
                if (!ConsumeColonySilverForScribeTable(map, finalPrice))
                {
                    Messages.Message("RimMercenaries_NotEnoughSilver".Translate(finalPrice), MessageTypeDefOf.RejectInput, false);
                    Log.Error($"[RimMercenaries] Failed to consume {finalPrice} silver for ScribeTable hire despite having enough - this should not happen");
                    return false;
                }
                
                Log.Message($"[RimMercenaries] Successfully consumed {finalPrice} silver via ScribeTable method for {offer.pawn.LabelShortCap}");
            }
            else
            {
                // Use vanilla TradeUtility for CommsConsole
                if (!TradeUtility.ColonyHasEnoughSilver(map, finalPrice))
                {
                    Messages.Message("RimMercenaries_NotEnoughSilver".Translate(finalPrice), MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                
                TradeUtility.LaunchSilver(map, finalPrice);
            }
            // ---- END ScribeTable-aware silver handling ----

            // Apply selected loadout if provided (strip and equip)
            if (loadout != null)
            {
                ApplyLoadoutToPawn(offer.pawn, loadout);
            }

            offer.pawn.SetFaction(Faction.OfPlayer);
            offer.pawn.relations?.Notify_ChangedFaction();

            // Force currently worn apparel so outfit filters don't make them strip on spawn
            try
            {
                if (offer.pawn.outfits == null)
                {
                    offer.pawn.outfits = new Pawn_OutfitTracker(offer.pawn);
                }
                var forced = offer.pawn.outfits?.forcedHandler;
                var worn = offer.pawn.apparel?.WornApparel;
                if (forced != null && worn != null)
                {
                    foreach (var ap in worn.ToList())
                    {
                        if (ap != null && !ap.Destroyed)
                        {
                            forced.SetForced(ap, true);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMercenaries] Failed to force merc apparel for {offer.pawn.LabelShortCap}: {ex.Message}");
            }

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

            var podInfo = new ActiveTransporterInfo();
            podInfo.innerContainer.TryAdd(offer.pawn);
            podInfo.leaveSlag = false;
            podInfo.moveItemsAsideBeforeSpawning = true;
            podInfo.openDelay = 60; // Faster opening (default is 110)
            podInfo.spawnWipeMode = WipeMode.Vanish; // Gentler wipe mode
            DropPodUtility.MakeDropPodAt(finalDropCell, map, podInfo);

            Messages.Message(
                "RimMercenaries_MercenaryHired".Translate(offer.pawn.LabelShortCap, negotiator.LabelShortCap, finalPrice),
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

        public static void ApplyLoadoutToPawn(Pawn pawn, MercenaryLoadoutSelection loadout)
        {
            if (pawn == null || loadout == null) return;
            try
            {
                pawn.equipment?.DestroyAllEquipment();

                if (pawn.apparel != null)
                {
                    foreach (var ap in pawn.apparel.WornApparel.ToList())
                    {
                        if (ap == null || ap.Destroyed)
                        {
                            continue;
                        }
                        pawn.apparel.Remove(ap);
                        if (!ap.Destroyed)
                        {
                            ap.Destroy();
                        }
                    }
                }

                // Ensure outfit tracker exists so we can mark forced apparel below
                if (pawn.outfits == null)
                {
                    pawn.outfits = new Pawn_OutfitTracker(pawn);
                }

                if (loadout.selectedWeaponDef != null && IsResearchedOrNoPrereq(loadout.selectedWeaponDef))
                {
                    ThingDef stuff = GenStuff.DefaultStuffFor(loadout.selectedWeaponDef);
                    var weapon = ThingMaker.MakeThing(loadout.selectedWeaponDef, stuff) as ThingWithComps;
                    if (weapon != null)
                    {
                        if (EquipmentUtility.CanEquip(weapon, pawn))
                        {
                            if (pawn.equipment == null)
                            {
                                pawn.equipment = new Pawn_EquipmentTracker(pawn);
                            }
                            // Apply weapon style if chosen
                            try
                            {
                                if (loadout.selectedWeaponStyle != null)
                                {
                                    var styleable = weapon.GetCompByReflectedType("RimWorld.CompStyleable");
                                    if (styleable != null)
                                    {
                                        var setStyle = styleable.GetType().GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (setStyle != null)
                                        {
                                            var pars = setStyle.GetParameters();
                                            if (pars.Length >= 1)
                                            {
                                                var args = pars.Length == 2 ? new object[] { loadout.selectedWeaponStyle, true } : new object[] { loadout.selectedWeaponStyle };
                                                setStyle.Invoke(styleable, args);
                                            }
                                        }
                                        else
                                        {
                                            var setStyleDef = styleable.GetType().GetMethod("SetStyleDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                            if (setStyleDef != null)
                                            {
                                                setStyleDef.Invoke(styleable, new object[] { loadout.selectedWeaponStyle });
                                            }
                                            else
                                            {
                                                var prop = styleable.GetType().GetProperty("StyleDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                                if (prop != null && prop.CanWrite) prop.SetValue(styleable, loadout.selectedWeaponStyle);
                                                else
                                                {
                                                    var field = styleable.GetType().GetField("styleDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                                    if (field != null) field.SetValue(styleable, loadout.selectedWeaponStyle);
                                                }
                                                var notify = styleable.GetType().GetMethod("Notify_StyleChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                                notify?.Invoke(styleable, null);
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                            pawn.equipment.AddEquipment(weapon);
                        }
                        else
                        {
                            weapon.Destroy();
                        }
                    }
                }

                if (loadout.selectedApparelDefs != null && pawn.apparel != null)
                {
                    foreach (var appDef in loadout.selectedApparelDefs)
                    {
                        if (appDef == null) continue;
                        if (!IsResearchedOrNoPrereq(appDef)) continue;
                        if (!ApparelUtility.HasPartsToWear(pawn, appDef)) continue;
                        if (IsChildOrBabyApparel(appDef)) continue;
                        if (appDef.apparel.gender != Gender.None && pawn.gender != Gender.None && pawn.gender != appDef.apparel.gender) continue;

                        Apparel apparel = null;
                        var customization = loadout.GetApparelCustomization(appDef);
                        if (customization != null)
                        {
                            apparel = customization.CreateApparel();
                        }
                        else
                        {
                            ThingDef stuff = GenStuff.DefaultStuffFor(appDef);
                            apparel = ThingMaker.MakeThing(appDef, stuff) as Apparel;
                        }

                        if (apparel != null)
                        {
                            if (CanWearOnTopWithoutConflicts(pawn, apparel))
                            {
                                pawn.apparel.Wear(apparel, false);
                                // Mark as forced so outfit filters don't immediately strip it
                                try { pawn.outfits?.forcedHandler?.SetForced(apparel, true); } catch { }
                            }
                            else
                            {
                                apparel.Destroy();
                            }
                        }
                    }
                }

                // Apply selected bionics (hediffs) after equipping gear
                try
                {
                    if (RimMercenariesMod.ActiveSettings.enableBionicsCustomization && loadout.selectedBionics != null && loadout.selectedBionics.Count > 0)
                    {
                        foreach (var sb in loadout.selectedBionics)
                        {
                            if (sb == null || string.IsNullOrEmpty(sb.hediffDefName) || string.IsNullOrEmpty(sb.bodyPartPath)) continue;
                            var hediffDef = DefDatabase<HediffDef>.GetNamed(sb.hediffDefName, false);
                            if (hediffDef == null) continue;
                            var part = BionicsSelectionUtility.FindPartByPath(pawn, sb.bodyPartPath, sb.bodyPartIndex);
                            if (part == null) continue;
                            if (pawn.health?.hediffSet == null) continue;

                            // Avoid duplicates on same part
                            var existing = pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.def == hediffDef && h.Part == part);
                            if (existing != null) continue;

                            var hediff = HediffMaker.MakeHediff(hediffDef, pawn, part);
                            pawn.health.AddHediff(hediff, part);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[RimMercenaries] Error applying bionics: {ex.Message}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMercenaries] Error applying loadout: {ex.Message}");
            }
        }

        private static bool IsResearchedOrNoPrereq(ThingDef def)
        {
            if (def == null) return false;

            // Check ThingDef-attached research prerequisites if any
            try
            {
                var prereqField = typeof(ThingDef).GetField("researchPrerequisites", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prereqField != null)
                {
                    var list = prereqField.GetValue(def) as List<ResearchProjectDef>;
                    if (list != null)
                    {
                        foreach (var proj in list)
                        {
                            if (proj != null && !proj.IsFinished)
                                return false;
                        }
                    }
                }
            }
            catch { }

            // Check recipe-based research prerequisites
            try
            {
                var recipes = DefDatabase<RecipeDef>.AllDefsListForReading
                    .Where(r => r.products != null && r.products.Any(p => p.thingDef == def))
                    .ToList();
                foreach (var recipe in recipes)
                {
                    if (recipe.researchPrerequisites != null)
                    {
                        foreach (var res in recipe.researchPrerequisites)
                        {
                            if (!res.IsFinished)
                                return false;
                        }
                    }
                }
            }
            catch { }

            return true;
        }

        private static bool IsChildOrBabyApparel(ThingDef apparelDef)
        {
            try
            {
                string label = (apparelDef.label ?? apparelDef.defName ?? string.Empty).ToLowerInvariant();
                if (label.Contains("child") || label.Contains("kid") || label.Contains("baby") || label.Contains("toddler") || label.Contains("infant"))
                    return true;

                var tags = apparelDef.apparel?.tags;
                if (tags != null)
                {
                    foreach (var t in tags)
                    {
                        if (t == null) continue;
                        string tt = t.ToLowerInvariant();
                        if (tt.Contains("child") || tt.Contains("kid") || tt.Contains("baby") || tt.Contains("toddler") || tt.Contains("infant"))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool CanWearOnTopWithoutConflicts(Pawn pawn, Apparel apparel)
        {
            try
            {
                if (pawn == null || apparel == null || pawn.apparel == null) return false;
                if (!ApparelUtility.HasPartsToWear(pawn, apparel.def)) return false;
                var body = pawn.RaceProps?.body;
                if (body == null) return false;
                foreach (var worn in pawn.apparel.WornApparel)
                {
                    if (worn == null || worn.Destroyed) continue;
                    if (!ApparelUtility.CanWearTogether(apparel.def, worn.def, body))
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
