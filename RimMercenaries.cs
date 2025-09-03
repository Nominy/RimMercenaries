using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using HarmonyLib;
using System.Reflection;
using Verse.AI;

namespace RimMercenaries
{
    /// <summary>
    /// Global theme provider for consistent theming across all mod windows
    /// </summary>
    public static class ThemeProvider
    {
        // --- Theme Colors ---
        private static readonly Color DefaultBgColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        private static readonly Color DeepPurple = new Color(0.3f, 0.1f, 0.5f, 0.8f);
        private static readonly Color DeepBlue = new Color(0.1f, 0.2f, 0.5f, 0.8f);
        private static readonly Color DeepGreen = new Color(0.1f, 0.4f, 0.2f, 0.8f);
        private static readonly Color DeepYellow = new Color(0.5f, 0.4f, 0.1f, 0.8f);

        private static readonly List<(string translationKey, Color color)> themes = new List<(string, Color)>()
        {
            ("RimMercenaries_ThemeDefault", DefaultBgColor),
            ("RimMercenaries_ThemeDeepPurple", DeepPurple),
            ("RimMercenaries_ThemeDeepBlue", DeepBlue),
            ("RimMercenaries_ThemeDeepGreen", DeepGreen),
            ("RimMercenaries_ThemeDeepYellow", DeepYellow)
        };

        private static int selectedThemeIndex = 0;

        /// <summary>
        /// Blends two colors together with the specified ratio
        /// </summary>
        public static Color BlendColor(Color a, Color b, float t)
        {
            return new Color(
                Mathf.Lerp(a.r, b.r, t),
                Mathf.Lerp(a.g, b.g, t),
                Mathf.Lerp(a.b, b.b, t),
                Mathf.Lerp(a.a, b.a, t));
        }

        /// <summary>
        /// Gets the currently selected theme color
        /// </summary>
        public static Color CurrentThemeColor => themes[selectedThemeIndex].color;

        /// <summary>
        /// Gets the translation key for the currently selected theme
        /// </summary>
        public static string CurrentThemeTranslationKey => themes[selectedThemeIndex].translationKey;

        /// <summary>
        /// Gets all available themes
        /// </summary>
        public static List<(string translationKey, Color color)> Themes => themes;

        /// <summary>
        /// Gets the currently selected theme index
        /// </summary>
        public static int SelectedThemeIndex => selectedThemeIndex;

        /// <summary>
        /// Cycles to the next theme and returns the new theme info
        /// </summary>
        public static (string translationKey, Color color) CycleTheme()
        {
            selectedThemeIndex = (selectedThemeIndex + 1) % themes.Count;
            return themes[selectedThemeIndex];
        }

        /// <summary>
        /// Gets a themed background color for the main window content
        /// </summary>
        public static Color GetWindowBackgroundColor()
        {
            Color vanillaBg = new Color(0.13f, 0.13f, 0.13f, 0.92f);
            return BlendColor(vanillaBg, CurrentThemeColor, 0.22f);
        }

        /// <summary>
        /// Gets a themed color for window sections/cells
        /// </summary>
        public static Color GetSectionBackgroundColor()
        {
            Color vanillaCellBg = new Color(0.18f, 0.18f, 0.18f, 0.92f);
            return BlendColor(vanillaCellBg, CurrentThemeColor, 0.28f);
        }
    }

    [StaticConstructorOnStartup]
    public static class RimMercenaries
    {
        public static XenotypeDef selectedXenotypeDef = null;
        public static JobDef UseMercenaryConsoleJobDef;

        static RimMercenaries()
        {
            var harmony = new Harmony("rimmercenaries.commsconsolepatch");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // Cache the job def for better performance
            UseMercenaryConsoleJobDef = DefDatabase<JobDef>.GetNamed("UseMercenaryConsole");

            Log.Message("[RimMercenaries] Mod Initialized. Added Mercenary functionality to Comms Console.");
        }

        /// <summary>
        /// Safely sets the selected xenotype, with validation to ensure it exists and is valid
        /// </summary>
        public static void SetSelectedXenotype(XenotypeDef xenotype)
        {
            if (xenotype != null && !DefDatabase<XenotypeDef>.AllDefsListForReading.Contains(xenotype))
            {
                Log.Warning($"[RimMercenaries] Attempted to select invalid xenotype {xenotype.defName}, resetting to null");
                selectedXenotypeDef = null;
                return;
            }
            
            selectedXenotypeDef = xenotype;
        }

        public static void OpenMercenaryHireWindow(Building commsConsole, Pawn negotiator)
        {
            if (negotiator == null)
            {
                Messages.Message("RimMercenaries_NoNegotiator".Translate(), commsConsole, MessageTypeDefOf.RejectInput, false);
                return;
            }

            var powerComp = commsConsole.GetComp<CompPowerTrader>();
            if (powerComp != null && !powerComp.PowerOn)
            {
                Messages.Message("RimMercenaries_NoPower".Translate(), commsConsole, MessageTypeDefOf.RejectInput, false);
                return;
            }

            // This will only refresh if needed
            MercenaryManager.TryRefreshMercenaries(commsConsole.Map);
            Find.WindowStack.Add(new Window_MercenaryHire(commsConsole, negotiator));
        }
    }

    [HarmonyPatch(typeof(Building_CommsConsole), "GetCommTargets")]
    public static class CommsConsole_GetCommTargets_Patch
    {
        public static void Postfix(ref IEnumerable<ICommunicable> __result)
        {
            var resultList = __result.ToList();
            resultList.Add(new MercenaryCommTarget());
            __result = resultList;
        }
    }

    [HarmonyPatch]
    public static class ScribeTable_GetCommTargets_Patch
    {
        private static bool? _isMedievalOverhaulLoaded = null;
        
        private static bool IsMedievalOverhaulLoaded()
        {
            if (_isMedievalOverhaulLoaded.HasValue)
                return _isMedievalOverhaulLoaded.Value;
                
            try
            {
                // Check for both the original and 1.5 version package IDs
                _isMedievalOverhaulLoaded = ModsConfig.IsActive("DankPyon.Medieval.Overhaul");
                
                if (_isMedievalOverhaulLoaded.Value)
                {
                    Log.Message("[RimMercenaries] MedievalOverhaul detected - enabling ScribeTable integration");
                }
                else
                {
                    Log.Message("[RimMercenaries] MedievalOverhaul not detected - ScribeTable integration disabled");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMercenaries] Error checking for MedievalOverhaul mod: {ex.Message}");
                _isMedievalOverhaulLoaded = false;
            }
            
            return _isMedievalOverhaulLoaded.Value;
        }
        

        public static bool Prepare()
        {
            if (!IsMedievalOverhaulLoaded())
            {
                Log.Message("[RimMercenaries] Skipping ScribeTable patch - MedievalOverhaul not loaded");
                return false;
            }
            
            // Check if Building_ScribeTable type exists
            try
            {
                var scribeTableType = AccessTools.TypeByName("MedievalOverhaul.Building_ScribeTable");
                if (scribeTableType == null)
                {
                    Log.Warning("[RimMercenaries] Building_ScribeTable type not found - ScribeTable patch disabled");
                    return false;
                }
                
                // Verify the GetCommTargets_Messenger method exists
                var targetMethod = AccessTools.Method(scribeTableType, "GetCommTargets_Messenger");
                if (targetMethod == null)
                {
                    Log.Warning("[RimMercenaries] GetCommTargets_Messenger method not found - ScribeTable patch disabled");
                    return false;
                }
                
                Log.Message("[RimMercenaries] ScribeTable patch will be applied to GetCommTargets_Messenger");
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMercenaries] Error checking Building_ScribeTable type: {ex.Message}");
                return false;
            }
        }
        
        public static MethodBase TargetMethod()
        {
            try
            {
                var scribeTableType = AccessTools.TypeByName("MedievalOverhaul.Building_ScribeTable");
                return AccessTools.Method(scribeTableType, "GetCommTargets_Messenger");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimMercenaries] Error getting ScribeTable target method: {ex.Message}");
                return null;
            }
        }
        
        public static void Postfix(ref IEnumerable<ICommunicable> __result)
        {
            try
            {
                var resultList = __result.ToList();
                resultList.Add(new MercenaryCommTarget());
                __result = resultList;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimMercenaries] Error in ScribeTable patch: {ex.Message}");
            }
        }
    }



    [StaticConstructorOnStartup]
    public class MercenaryCommTarget : ICommunicable
    {
        private static readonly Texture2D mercIcon = FactionDefOf.Pirate.FactionIcon;

        public string CommTargetTags => "";

        public string LabelCap => "RimMercenaries_MercenaryNetwork".Translate();

        public string TargetWorldObjectLabel => "";

        public Building TargetBuilding => null;

        public string Label => "RimMercenaries_MercenaryNetwork".Translate();

        public Faction TargetFaction => null;

        public bool CanCommunicateWith => true;

        public string GetCallLabel() => "RimMercenaries_CallMercenaryNetwork".Translate();

        public string GetInfoText()
        {
            int nextRefreshIn = MercenaryManager.LastRefreshTick + MercenaryManager.RefreshIntervalTicks - Find.TickManager.TicksGame;
            
            string baseText = "RimMercenaries_MercenaryNetworkInfo".Translate() + "\n\n";
            
            return nextRefreshIn <= 0
                ? baseText + "RimMercenaries_RefreshReady".Translate()
                : baseText + "RimMercenaries_NextRefresh".Translate(nextRefreshIn.ToStringTicksToPeriod());
        }

        public void TryOpenComms(Pawn negotiator)
        {
            Building_CommsConsole console = negotiator.Map.listerBuildings.AllBuildingsColonistOfClass<Building_CommsConsole>().FirstOrDefault();
            if (console != null)
            {
                // Create a job for the pawn to go to the console
                Job job = JobMaker.MakeJob(RimMercenaries.UseMercenaryConsoleJobDef, console);
                negotiator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }

        public Faction GetFaction() => null;

        public bool ValidateTarget(out string reason)
        {
            reason = null;
            return true;
        }

        public FloatMenuOption CommFloatMenuOption(Building_CommsConsole console, Pawn negotiator)
        {
            int nextRefreshIn = MercenaryManager.LastRefreshTick + MercenaryManager.RefreshIntervalTicks - Find.TickManager.TicksGame;
            string optionLabel = "RimMercenaries_ContactMercenaryNetwork".Translate();
            Color iconColor = Color.white;
            
            if (nextRefreshIn <= 0)
            {
                optionLabel += " ✓";
                iconColor = Color.green;
            }
            
            FloatMenuOption option = new FloatMenuOption(
                optionLabel,
                () => {
                    // Create a job for the pawn to go to the console
                    Job job = JobMaker.MakeJob(RimMercenaries.UseMercenaryConsoleJobDef, console);
                    negotiator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                },
                mercIcon,
                iconColor,
                MenuOptionPriority.Default
            );
            
            return FloatMenuUtility.DecoratePrioritizedTask(option, negotiator, console);
        }
    }
}