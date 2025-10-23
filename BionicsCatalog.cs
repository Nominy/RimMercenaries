using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimMercenaries
{
    public class BionicOption
    {
        public HediffDef Hediff;
        public RecipeDef Recipe;
        public ThingDef ImplantThing;
        public List<BodyPartDef> ApplicableParts = new List<BodyPartDef>();
        public float CalculatedMarketValue;

        public override string ToString()
        {
            return Hediff?.LabelCap ?? base.ToString();
        }
    }

    public static class BionicsCatalog
    {
        private static readonly Dictionary<HediffDef, BionicOption> OptionsByHediff = new Dictionary<HediffDef, BionicOption>();
        private static readonly Dictionary<BodyPartDef, List<BionicOption>> OptionsByBodyPart = new Dictionary<BodyPartDef, List<BionicOption>>();
        private static RimMercenariesSettings CachedSettings;
        private static int GeneratedTick;

        public static void Rebuild()
        {
            OptionsByHediff.Clear();
            OptionsByBodyPart.Clear();
            CachedSettings = RimMercenariesMod.ActiveSettings ?? RimMercenariesMod.Settings;
            GeneratedTick = Find.TickManager?.TicksAbs ?? 0;

            foreach (var recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                try
                {
                    if (recipe?.addsHediff == null) continue;
                    if (recipe.appliedOnFixedBodyParts == null || recipe.appliedOnFixedBodyParts.Count == 0) continue;

                    var hediff = recipe.addsHediff;
                    if (hediff == null) continue;

                    var option = GetOrCreateOption(hediff);
                    option.Recipe = option.Recipe ?? recipe;
                    foreach (var part in recipe.appliedOnFixedBodyParts)
                    {
                        if (part == null) continue;
                        if (!option.ApplicableParts.Contains(part))
                        {
                            option.ApplicableParts.Add(part);
                        }
                        if (!OptionsByBodyPart.TryGetValue(part, out var list))
                        {
                            list = new List<BionicOption>();
                            OptionsByBodyPart[part] = list;
                        }
                        if (!list.Contains(option))
                        {
                            list.Add(option);
                        }
                    }

                    if (option.ImplantThing == null)
                    {
                    option.ImplantThing = recipe.products?.Select(p => p.thingDef).FirstOrDefault(d => d != null && d.IsIngestible == false);
                    if (option.ImplantThing == null && recipe.ingredients != null)
                    {
                        option.ImplantThing = recipe.ingredients
                            .SelectMany(ingredient => ingredient.filter?.AllowedThingDefs ?? Enumerable.Empty<ThingDef>())
                            .FirstOrDefault(def => def != null && !def.IsIngestible);
                    }
                    }

                    option.CalculatedMarketValue = CalculateMarketValue(option);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMercenaries] Failed to process recipe {recipe?.defName}: {ex.Message}");
                }
            }
        }

        private static BionicOption GetOrCreateOption(HediffDef hediff)
        {
            if (!OptionsByHediff.TryGetValue(hediff, out var option))
            {
                option = new BionicOption
                {
                    Hediff = hediff
                };
                OptionsByHediff[hediff] = option;
            }
            return option;
        }

        public static IEnumerable<BionicOption> GetOptionsForPart(BodyPartRecord part)
        {
            if (part?.def == null)
            {
                yield break;
            }

            EnsureFresh();

            if (OptionsByBodyPart.TryGetValue(part.def, out var list))
            {
                foreach (var option in list)
                {
                    if (OptionAllowed(option))
                    {
                        yield return option;
                    }
                }
            }
        }

        public static BionicOption GetOption(HediffDef hediff)
        {
            EnsureFresh();
            if (hediff == null) return null;
            OptionsByHediff.TryGetValue(hediff, out var option);
            if (option != null && OptionAllowed(option))
            {
                return option;
            }
            return null;
        }

        public static void EnsureFresh()
        {
            var settings = RimMercenariesMod.ActiveSettings ?? RimMercenariesMod.Settings;
            if (settings != CachedSettings)
            {
                Rebuild();
                return;
            }

            var currentTick = Find.TickManager?.TicksAbs ?? 0;
            if (OptionsByHediff.Count == 0 || Math.Abs(currentTick - GeneratedTick) > 1000)
            {
                Rebuild();
            }
        }

        private static bool OptionAllowed(BionicOption option)
        {
            if (option?.Hediff == null) return false;

            var settings = CachedSettings ?? RimMercenariesMod.ActiveSettings ?? RimMercenariesMod.Settings;
            if (settings == null) return true;

            if (settings.disallowArchotechBionics)
            {
                if ((option.ImplantThing != null && option.ImplantThing.techLevel == TechLevel.Archotech) ||
                    option.Hediff.defName?.IndexOf("archotech", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    option.Hediff.label?.IndexOf("archotech", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            if (!settings.BionicHediffAllowed(option.Hediff.defName))
            {
                return false;
            }

            if (option.ImplantThing != null && !settings.BionicImplantAllowed(option.ImplantThing.defName))
            {
                return false;
            }

            return true;
        }

        private static float CalculateMarketValue(BionicOption option)
        {
            if (option == null) return 0f;

            if (option.ImplantThing != null)
            {
                try
                {
                    return option.ImplantThing.BaseMarketValue * 1.4f;
                }
                catch
                {
                    // ignore pricing errors, fallback below
                }
            }

            if (option.Recipe != null)
            {
                float total = 0f;
                if (option.Recipe.ingredients != null)
                {
                    foreach (var ingredient in option.Recipe.ingredients)
                    {
                        try
                        {
                            var fixedThing = ingredient.FixedIngredient;
                            if (fixedThing != null)
                            {
                                total += fixedThing.BaseMarketValue * ingredient.GetBaseCount();
                            }
                            else if (ingredient.filter?.AllowedThingDefs != null)
                            {
                                var cheapest = ingredient.filter.AllowedThingDefs.OrderBy(d => d.BaseMarketValue).FirstOrDefault();
                                if (cheapest != null)
                                {
                                    total += cheapest.BaseMarketValue * ingredient.GetBaseCount();
                                }
                            }
                        }
                        catch
                        {
                            // ignore ingredient errors
                        }
                    }
                }
                return total * 1.4f;
            }

            return 0f;
        }
    }
}

