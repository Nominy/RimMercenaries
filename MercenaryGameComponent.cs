using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimMercenaries
{
    /// <summary>
    /// Helper class for saving/loading xenotype batches
    /// </summary>
    public class XenotypeBatchSaveData : IExposable
    {
        public string xenotypeDefName;
        public List<MercenaryOffer> offers;

        public XenotypeBatchSaveData() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref xenotypeDefName, "xenotypeDefName");
            Scribe_Collections.Look(ref offers, "offers", LookMode.Deep);
        }
    }

    public class MercenaryGameComponent : GameComponent
    {
        // Instance data - each save will have its own independent mercenary data
        private Dictionary<XenotypeDef, List<MercenaryOffer>> xenotypeBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();
        private Dictionary<int, int> globalTierCounters = new Dictionary<int, int> { { 1, 10 }, { 2, 5 }, { 3, 2 } };
        private List<MercenaryOffer> availableMercenaries = new List<MercenaryOffer>();
        private int lastMercenaryRefreshTick = -99999;
        private bool hasBeenInitialized = false;
        
        // Reserve batches for instant switching
        private Dictionary<XenotypeDef, List<MercenaryOffer>> reserveBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();
        private bool isGeneratingReserve = false;

        public const int RefreshIntervalTicks = 3600000;
        
        // Properties to access the data
        public Dictionary<XenotypeDef, List<MercenaryOffer>> XenotypeBatches => xenotypeBatches;
        public Dictionary<int, int> GlobalTierCounters => globalTierCounters;
        public List<MercenaryOffer> AvailableMercenaries => availableMercenaries;
        public int LastRefreshTick => lastMercenaryRefreshTick;
        public bool HasBeenInitialized => hasBeenInitialized;
        public Dictionary<XenotypeDef, List<MercenaryOffer>> ReserveBatches => reserveBatches;
        public bool IsGeneratingReserve => isGeneratingReserve;

        public MercenaryGameComponent(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            // Don't save availableMercenaries separately - we'll reconstruct it from xenotype batches
            // Scribe_Collections.Look(ref availableMercenaries, "availableMercenaries", LookMode.Deep);
            Scribe_Collections.Look(ref globalTierCounters, "globalTierCounters", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref lastMercenaryRefreshTick, "lastMercenaryRefreshTick", -99999);
            Scribe_Values.Look(ref hasBeenInitialized, "hasBeenInitialized", false);
            
            // Save/load xenotype batches properly
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Convert to a simpler structure for saving
                var xenotypeBatchSaveData = new List<XenotypeBatchSaveData>();
                foreach (var kvp in xenotypeBatches)
                {
                    xenotypeBatchSaveData.Add(new XenotypeBatchSaveData
                    {
                        xenotypeDefName = kvp.Key?.defName ?? "NULL_XENOTYPE", // Use special marker for null
                        offers = kvp.Value
                    });
                }
                Scribe_Collections.Look(ref xenotypeBatchSaveData, "xenotypeBatchSaveData", LookMode.Deep);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var xenotypeBatchSaveData = new List<XenotypeBatchSaveData>();
                Scribe_Collections.Look(ref xenotypeBatchSaveData, "xenotypeBatchSaveData", LookMode.Deep);
                
                // Reconstruct xenotype batches from save data
                xenotypeBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();
                if (xenotypeBatchSaveData != null)
                {
                    foreach (var saveData in xenotypeBatchSaveData)
                    {
                        XenotypeDef xenotypeDef = null;
                        if (!string.IsNullOrEmpty(saveData.xenotypeDefName))
                        {
                            if (saveData.xenotypeDefName == "NULL_XENOTYPE")
                            {
                                // This represents null xenotype (no Biotech)
                                xenotypeDef = null;
                            }
                            else
                            {
                                xenotypeDef = DefDatabase<XenotypeDef>.GetNamed(saveData.xenotypeDefName, false);
                            }
                        }
                        
                        if (saveData.offers != null)
                        {
                            xenotypeBatches[xenotypeDef] = saveData.offers;
                        }
                    }
                }
            }
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Initialize collections if they're null
                if (availableMercenaries == null)
                    availableMercenaries = new List<MercenaryOffer>();
                if (xenotypeBatches == null)
                    xenotypeBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();
                if (reserveBatches == null)
                    reserveBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();
                if (globalTierCounters == null || !globalTierCounters.Any())
                    globalTierCounters = new Dictionary<int, int> { { 1, 10 }, { 2, 5 }, { 3, 2 } };
                
                // Clean up xenotype batches and reconstruct availableMercenaries
                foreach (var batch in xenotypeBatches.Values)
                {
                    batch?.RemoveAll(offer => offer == null || offer.pawn == null);
                }
                
                // Reconstruct availableMercenaries from the current xenotype batch
                // This assumes the last used xenotype is what should be shown
                if (xenotypeBatches.Any())
                {
                    // Try to use the selected xenotype if it exists in our batches
                    if (RimMercenaries.selectedXenotypeDef != null && xenotypeBatches.ContainsKey(RimMercenaries.selectedXenotypeDef))
                    {
                        availableMercenaries = xenotypeBatches[RimMercenaries.selectedXenotypeDef];
                    }
                    else if (!ModsConfig.BiotechActive && xenotypeBatches.ContainsKey(null))
                    {
                        // For non-Biotech, use the null xenotype batch
                        availableMercenaries = xenotypeBatches[null];
                    }
                    else
                    {
                        // Otherwise use the first available batch
                        availableMercenaries = xenotypeBatches.Values.First();
                    }
                }
                else
                {
                    availableMercenaries.Clear();
                }
                
                Log.Message($"[RimMercenaries] Loaded save data: hasBeenInitialized={hasBeenInitialized}, lastRefreshTick={lastMercenaryRefreshTick}, mercenaries={availableMercenaries.Count}, tier1={globalTierCounters[1]}, tier2={globalTierCounters[2]}, tier3={globalTierCounters[3]}");
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            
            Log.Message($"[RimMercenaries] FinalizeInit called for save. HasBeenInitialized: {hasBeenInitialized}");
            
            // Validate the currently selected xenotype to ensure it's still valid
            if (ModsConfig.BiotechActive && RimMercenaries.selectedXenotypeDef != null)
            {
                if (!DefDatabase<XenotypeDef>.AllDefsListForReading.Contains(RimMercenaries.selectedXenotypeDef))
                {
                    Log.Warning($"[RimMercenaries] Previously selected xenotype {RimMercenaries.selectedXenotypeDef.defName} is no longer valid, resetting to default");
                    RimMercenaries.selectedXenotypeDef = DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == "Baseliner");
                }
            }
            
            // Initialize mercenaries if this is a new game or if they haven't been initialized yet
            if (Current.Game?.Maps != null && Current.Game.Maps.Count > 0)
            {
                var map = Current.Game.Maps[0];
                
                // Force initialization if mercenaries haven't been set up yet
                if (!hasBeenInitialized)
                {
                    Log.Message("[RimMercenaries] Initializing mercenaries for this save...");
                    ForceInitialization(map);
                }
            }
        }

        public void ForceInitialization(Map map)
        {
            Log.Message("[RimMercenaries] Force initializing mercenary system for this save...");
            
            // Clear any existing data
            foreach (var batch in xenotypeBatches.Values)
                foreach (var offer in batch)
                    if (offer?.pawn != null)
                        MercenaryOffer.DiscardPawnIfNeeded(offer.pawn);

            xenotypeBatches.Clear();
            availableMercenaries.Clear();
            reserveBatches.Clear();

            // Reset counters (force initialization always resets counters)
            globalTierCounters[1] = 10;
            globalTierCounters[2] = 5;
            globalTierCounters[3] = 2;

            // Set initialization time to trigger immediate refresh
            lastMercenaryRefreshTick = Find.TickManager.TicksGame - RefreshIntervalTicks - 1;
            hasBeenInitialized = true;

            // Generate initial mercenaries
            TryRefreshMercenaries(map);
            
            Log.Message("[RimMercenaries] Mercenary system initialized successfully for this save!");
        }

        public void TryRefreshMercenaries(Map map)
        {
            // Only refresh if enough time has passed or we don't have any mercenaries
            if (Find.TickManager.TicksGame >= lastMercenaryRefreshTick + RefreshIntervalTicks || xenotypeBatches == null || !xenotypeBatches.Any())
            {
                hasBeenInitialized = true;
                
                // Clear existing mercenaries
                foreach (var batch in xenotypeBatches.Values)
                    foreach (var offer in batch)
                        if (offer?.pawn != null)
                            MercenaryOffer.DiscardPawnIfNeeded(offer.pawn);

                xenotypeBatches.Clear();
                availableMercenaries.Clear();

                // Only reset counters if this is a genuine refresh (not loading from save)
                // Check if this is being called from initialization vs a genuine refresh
                bool isGenuineRefresh = lastMercenaryRefreshTick > -99999 && 
                                       Find.TickManager.TicksGame >= lastMercenaryRefreshTick + RefreshIntervalTicks;
                
                if (isGenuineRefresh)
                {
                    globalTierCounters[1] = 10;
                    globalTierCounters[2] = 5;
                    globalTierCounters[3] = 2;
                }

                // Handle xenotype logic based on whether Biotech is active
                XenotypeDef defaultXenotype = null;
                if (ModsConfig.BiotechActive)
                {
                    defaultXenotype = DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == "Baseliner");
                }
                
                // Generate mercenaries (with or without xenotype)
                if (ModsConfig.BiotechActive && defaultXenotype != null)
                {
                    // Use reserve batch if available
                    if (reserveBatches.TryGetValue(defaultXenotype, out List<MercenaryOffer> reserved))
                    {
                        xenotypeBatches[defaultXenotype] = reserved;
                        reserveBatches.Remove(defaultXenotype);
                        availableMercenaries = reserved;
                    }
                    else
                    {
                        // Fall back to synchronous generation
                        GenerateMercenariesForXenotype(map, defaultXenotype);
                    }
                    
                    // Always generate the next reserve batch
                    GenerateReserveBatchForXenotype(map, defaultXenotype);
                    
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
                else if (ModsConfig.BiotechActive)
                {
                    Log.Error("[RimMercenaries] ERROR: Could not find Baseliner xenotype! This will prevent mercenary generation.");
                }
                else
                {
                    // No Biotech - generate mercenaries without xenotype filtering
                    GenerateMercenariesForXenotype(map, null);
                }
            }
        }

        private void GenerateMercenariesForXenotype(Map map, XenotypeDef xenotype)
        {
            if (xenotype == null && ModsConfig.BiotechActive) 
            {
                Log.Error("[RimMercenaries] ERROR: Cannot generate mercenaries - xenotype is null but Biotech is active!");
                return;
            }

            if (map == null)
            {
                Log.Error("[RimMercenaries] ERROR: Cannot generate mercenaries - map is null!");
                return;
            }

            var offers = new List<MercenaryOffer>();
            
            foreach (var tier in globalTierCounters.Keys)
            {
                int tierCount = globalTierCounters[tier];
                
                for (int i = 0; i < tierCount; i++)
                {
                    try
                    {
                        var offer = MercenaryOfferGenerator.GenerateOffer(map, tier, xenotype);
                        if (offer != null)
                        {
                            offers.Add(offer);
                        }
                        else
                        {
                            Log.Warning($"[RimMercenaries] Failed to generate mercenary {i+1}/{tierCount} for tier {tier} - MercenaryOfferGenerator.GenerateOffer returned null");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[RimMercenaries] Exception while generating mercenary {i+1}/{tierCount} for tier {tier}: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
            
            if (xenotype != null)
            {
                xenotypeBatches[xenotype] = offers;
            }
            
            availableMercenaries = offers;
            lastMercenaryRefreshTick = Find.TickManager.TicksGame;
            hasBeenInitialized = true;
            
            Log.Message($"[RimMercenaries] Generated {offers.Count} mercenaries for {(xenotype?.defName ?? "no xenotype (no Biotech)")} - Tier counts: T1={globalTierCounters[1]}, T2={globalTierCounters[2]}, T3={globalTierCounters[3]}");
        }

        private void GenerateReserveBatchForXenotype(Map map, XenotypeDef xenotype)
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
                                try
                                {
                                    var offer = MercenaryOfferGenerator.GenerateOffer(currentMap, tier, xenotype);
                                    if (offer != null)
                                        offers.Add(offer);
                                }
                                catch (System.Exception ex)
                                {
                                    Log.Warning($"[RimMercenaries] Exception while generating reserve mercenary {i+1} for tier {tier}, xenotype {xenotype?.defName ?? "none"}: {ex.Message}");
                                    // Continue with next mercenary
                                }
                            }
                        }
                        
                        reserveBatches[xenotype] = offers;
                        Log.Message($"[RimMercenaries] Generated {offers.Count} reserve mercenaries for xenotype {xenotype?.defName ?? "none"}");
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[RimMercenaries] Critical error while generating reserve batch for xenotype {xenotype?.defName ?? "none"}: {ex.Message}");
                }
                finally
                {
                    isGeneratingReserve = false;
                }
            }, "GeneratingReserveMercenaries", false, null, true);
        }

        public void SetGeneratingReserve(bool value)
        {
            isGeneratingReserve = value;
        }

        public void SetAvailableMercenaries(List<MercenaryOffer> mercenaries)
        {
            availableMercenaries = mercenaries;
        }

        public List<MercenaryOffer> GetAvailableMercenariesForUI(XenotypeDef selectedXenotypeDef = null)
        {
            // Force initialization if not initialized yet
            if (!hasBeenInitialized && Find.Maps.Any())
            {
                ForceInitialization(Find.Maps.First());
                return availableMercenaries ?? new List<MercenaryOffer>();
            }
            
            if (selectedXenotypeDef != null && !xenotypeBatches.ContainsKey(selectedXenotypeDef))
            {
                // Try to use reserve batch first
                if (reserveBatches.TryGetValue(selectedXenotypeDef, out List<MercenaryOffer> reserved))
                {
                    xenotypeBatches[selectedXenotypeDef] = reserved;
                    reserveBatches.Remove(selectedXenotypeDef);
                    availableMercenaries = reserved; // Update the current list
                    
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
                if (ModsConfig.BiotechActive)
                {
                    var baseliner = DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == "Baseliner");
                    if (baseliner != null)
                    {
                        // Try to use reserve batch first
                        if (reserveBatches.TryGetValue(baseliner, out List<MercenaryOffer> reserved))
                        {
                            xenotypeBatches[baseliner] = reserved;
                            reserveBatches.Remove(baseliner);
                            availableMercenaries = reserved; // Update the current list
                            
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
                else
                {
                    // No Biotech - generate mercenaries without xenotype
                    GenerateMercenariesForXenotype(Find.CurrentMap, null);
                }
            }

            if (xenotypeBatches == null)
                xenotypeBatches = new Dictionary<XenotypeDef, List<MercenaryOffer>>();

            foreach (var batch in xenotypeBatches.Values)
                batch.RemoveAll(o => o.pawn == null || o.pawn.Destroyed || o.pawn.Dead);

            XenotypeDef keyToUse = null;
            if (ModsConfig.BiotechActive)
            {
                keyToUse = selectedXenotypeDef ?? DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == "Baseliner");
                
                if (keyToUse != null && xenotypeBatches.TryGetValue(keyToUse, out var selectedBatch))
                {
                    availableMercenaries = selectedBatch; // Update the current list
                    return selectedBatch;
                }
            }
            
            return availableMercenaries ?? new List<MercenaryOffer>();
        }
    }
} 