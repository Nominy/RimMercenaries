using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;
using System.Reflection;

namespace RimMercenaries
{
    public class ApparelCustomizationData
    {
        public ThingDef apparelDef;
        public ThingDef materialDef;
        public QualityCategory quality = QualityCategory.Normal;
        public int hitPoints = -1;
        public Color color = Color.white;
        public bool useCustomColor = false;
        public ThingStyleDef styleDef = null;

        public ApparelCustomizationData(ThingDef apparelDef)
        {
            this.apparelDef = apparelDef;
            this.materialDef = GenStuff.DefaultStuffFor(apparelDef);
            this.hitPoints = Mathf.RoundToInt(apparelDef.GetStatValueAbstract(StatDefOf.MaxHitPoints) * Rand.Range(0.5f, 1f));
            this.color = apparelDef.uiIconColor;
        }

        public Apparel CreateApparel()
        {
            if (materialDef == null)
            {
                materialDef = GenStuff.DefaultStuffFor(apparelDef);
            }

            var apparel = ThingMaker.MakeThing(apparelDef, materialDef) as Apparel;
            if (apparel != null)
            {
                // Set quality
                apparel.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);

                // Set hit points
                if (hitPoints > 0)
                {
                    apparel.HitPoints = hitPoints;
                }

                // Apply color override only if explicitly using a custom color.
                // This prevents forcing white (or any saved default) when loading presets
                // that did not opt into a custom color.
                var colorableComp = apparel.TryGetComp<CompColorable>();
                if (colorableComp != null && useCustomColor)
                {
                    color.a = 1f; // Ensure proper alpha
                    colorableComp.SetColor(color);
                    apparel.SetColor(color);
                }

                // Apply style (Ideology). Use reflection for broad compatibility across versions.
                try
                {
                    if (styleDef != null)
                    {
                        var styleable = apparel.GetCompByReflectedType("RimWorld.CompStyleable");
                        if (styleable != null)
                        {
                            // Try SetStyle(ThingStyleDef, bool)
                            var setStyle = styleable.GetType().GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (setStyle != null)
                            {
                                var pars = setStyle.GetParameters();
                                if (pars.Length >= 1)
                                {
                                    var args = pars.Length == 2 ? new object[] { styleDef, true } : new object[] { styleDef };
                                    setStyle.Invoke(styleable, args);
                                }
                            }
                            else
                            {
                                // Try SetStyleDef(ThingStyleDef)
                                var setStyleDef = styleable.GetType().GetMethod("SetStyleDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (setStyleDef != null)
                                {
                                    setStyleDef.Invoke(styleable, new object[] { styleDef });
                                }
                                else
                                {
                                    // Try write property/field directly and notify
                                    var prop = styleable.GetType().GetProperty("StyleDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (prop != null && prop.CanWrite)
                                    {
                                        prop.SetValue(styleable, styleDef);
                                    }
                                    else
                                    {
                                        var field = styleable.GetType().GetField("styleDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (field != null)
                                        {
                                            field.SetValue(styleable, styleDef);
                                        }
                                    }
                                    var notify = styleable.GetType().GetMethod("Notify_StyleChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    notify?.Invoke(styleable, null);
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            return apparel;
        }

        public ApparelCustomizationData Clone()
        {
            var clone = new ApparelCustomizationData(this.apparelDef)
            {
                materialDef = this.materialDef,
                quality = this.quality,
                hitPoints = this.hitPoints,
                color = new Color(this.color.r, this.color.g, this.color.b, 1f),
                useCustomColor = this.useCustomColor,
                styleDef = this.styleDef
            };
            return clone;
        }
    }

    public class SelectedBionic : IExposable
    {
        public string bodyPartPath;
        public int bodyPartIndex;
        public string hediffDefName;
        public string recipeDefName;
        public string implantThingDefName;

        public void ExposeData()
        {
            Scribe_Values.Look(ref bodyPartPath, "bodyPartPath");
            Scribe_Values.Look(ref bodyPartIndex, "bodyPartIndex", 0);
            Scribe_Values.Look(ref hediffDefName, "hediffDefName");
            Scribe_Values.Look(ref recipeDefName, "recipeDefName");
            Scribe_Values.Look(ref implantThingDefName, "implantThingDefName");
        }
    }

    public class MercenaryLoadoutSelection : IExposable
    {
        public ThingDef selectedWeaponDef;
        public ThingStyleDef selectedWeaponStyle;
        public List<ThingDef> selectedApparelDefs = new List<ThingDef>();
        public Dictionary<ThingDef, ApparelCustomizationData> apparelCustomizations = new Dictionary<ThingDef, ApparelCustomizationData>();
        public List<SelectedBionic> selectedBionics = new List<SelectedBionic>();

        public int SelectedItemCount => (selectedWeaponDef != null ? 1 : 0) + (selectedApparelDefs?.Distinct().Count() ?? 0);

        public void EnsureUniqueApparel()
        {
            if (selectedApparelDefs == null)
            {
                selectedApparelDefs = new List<ThingDef>();
            }
            else
            {
                selectedApparelDefs = selectedApparelDefs
                    .Where(d => d != null)
                    .Distinct()
                    .ToList();
            }

            // Drop customizations for items no longer selected
            if (apparelCustomizations != null)
            {
                var keys = apparelCustomizations.Keys.ToList();
                foreach (var k in keys)
                {
                    if (!selectedApparelDefs.Contains(k))
                    {
                        apparelCustomizations.Remove(k);
                    }
                }
            }
        }

        public int CalculateAdditionalCost(Pawn negotiator = null)
        {
            EnsureUniqueApparel();
            var settings = RimMercenariesMod.ActiveSettings;

            // Use actual item prices if enabled, otherwise use fixed cost
            if (settings != null && settings.useActualItemPrices)
            {
                return CalculateActualItemCosts(negotiator) + CalculateBionicsCost();
            }
            else
            {
                return SelectedItemCount * (settings?.loadoutPerItemCost ?? 0) + CalculateBionicsCost();
            }
        }

        public int CalculateActualItemCosts(Pawn negotiator = null)
        {
            int totalCost = 0;

            // Add weapon cost
            if (selectedWeaponDef != null)
            {
                totalCost += GetItemPrice(selectedWeaponDef, negotiator);
            }

            // Add apparel costs
            if (selectedApparelDefs != null)
            {
                foreach (var apparelDef in selectedApparelDefs.Distinct())
                {
                    if (apparelDef != null)
                    {
                        // Use customization if present for this apparel
                        totalCost += GetItemPriceConsideringCustomization(apparelDef, negotiator, null);
                    }
                }
            }

            return totalCost;
        }

        private int CalculateBionicsCost()
        {
            var settings = RimMercenariesMod.ActiveSettings;
            if (selectedBionics == null || selectedBionics.Count == 0 || settings == null || !settings.enableBionicsCustomization)
            {
                return 0;
            }

            if (settings.bionicsPricingMode == BionicsPricingMode.Static)
            {
                return Mathf.Max(0, settings.bionicsStaticPrice) * selectedBionics.Count;
            }

            int sum = 0;
            foreach (var b in selectedBionics)
            {
                try
                {
                    var hediff = string.IsNullOrEmpty(b.hediffDefName) ? null : DefDatabase<HediffDef>.GetNamed(b.hediffDefName, false);
                    var option = BionicsCatalog.GetOption(hediff);
                    if (option != null)
                    {
                        sum += Mathf.Max(0, Mathf.RoundToInt(option.CalculatedMarketValue));
                    }
                }
                catch { }
            }
            return sum;
        }

        public int GetItemCost(ThingDef itemDef, Pawn negotiator = null)
        {
            if (itemDef == null) return 0;

            var settings = RimMercenariesMod.ActiveSettings;
            if (settings != null && settings.useActualItemPrices)
            {
                return GetItemPriceConsideringCustomization(itemDef, negotiator, null);
            }
            else
            {
                return settings?.loadoutPerItemCost ?? 0;
            }
        }

        public int GetItemCost(ThingDef itemDef, Pawn negotiator, ApparelCustomizationData overrideCustomization)
        {
            if (itemDef == null) return 0;

            var settings = RimMercenariesMod.ActiveSettings;
            if (settings != null && settings.useActualItemPrices)
            {
                return GetItemPriceConsideringCustomization(itemDef, negotiator, overrideCustomization);
            }
            else
            {
                return settings?.loadoutPerItemCost ?? 0;
            }
        }

        private int GetItemBasePrice(ThingDef itemDef)
        {
            if (itemDef == null) return 0;

            try
            {
                float baseMarketValue = itemDef.BaseMarketValue;

                // Use the buying price (1.4x base price) as this is what the player would actually pay
                float buyingPrice = baseMarketValue * 1.4f;

                return Mathf.Max(1, Mathf.RoundToInt(buyingPrice));
            }
            catch
            {
                // Fallback to fixed cost if there's any error
                var settings = RimMercenariesMod.ActiveSettings;
                return settings?.loadoutPerItemCost ?? 200;
            }
        }

        private int GetItemPrice(ThingDef itemDef, Pawn negotiator = null)
        {
            if (itemDef == null) return 0;

            try
            {
                int basePrice = GetItemBasePrice(itemDef);

                // Apply negotiation bonuses if negotiator is provided
                if (negotiator != null)
                {
                    // Create a temporary thing to use with RimWorld's trade price system
                    var stuff = GenStuff.DefaultStuffFor(itemDef);
                    var tempThing = ThingMaker.MakeThing(itemDef, stuff);

                    if (tempThing != null)
                    {
                        // Use RimWorld's standard price calculation which includes:
                        // - Negotiator social skill
                        // - Faction relations
                        // - Trade price improvements from traits/buildings
                        float negotiatorSkillFactor = negotiator.skills.GetSkill(SkillDefOf.Social).Level / 20f; // 0-1 range
                        float negotiatedPrice = basePrice * (1.0f - negotiatorSkillFactor * 0.3f); // Up to 30% discount based on social skill

                        // Clean up temporary thing
                        tempThing.Destroy();

                        return Mathf.Max(1, Mathf.RoundToInt(negotiatedPrice));
                    }
                }

                // Fallback to base price if no negotiator or error occurred
                return basePrice;
            }
            catch
            {
                // Fallback to fixed cost if there's any error
                var settings = RimMercenariesMod.ActiveSettings;
                return settings?.loadoutPerItemCost ?? 200;
            }
        }

        private int GetItemPriceConsideringCustomization(ThingDef itemDef, Pawn negotiator, ApparelCustomizationData overrideCustomization)
        {
            try
            {
                // For apparels, if we have customization (override or stored), compute price from the actual customized apparel instance
                if (itemDef != null && itemDef.apparel != null)
                {
                    ApparelCustomizationData customization = overrideCustomization;
                    if (customization == null)
                    {
                        apparelCustomizations.TryGetValue(itemDef, out customization);
                    }

                    if (customization != null)
                    {
                        var apparel = customization.CreateApparel();
                        if (apparel != null)
                        {
                            // Market value already reflects stuff, quality and HP on the thing
                            float marketValue = StatExtension.GetStatValue(apparel, StatDefOf.MarketValue, true);
                            // Use buying price factor akin to baseline logic
                            float buyingPrice = marketValue * 1.4f;

                            if (negotiator != null)
                            {
                                float negotiatorSkillFactor = negotiator.skills.GetSkill(SkillDefOf.Social).Level / 20f;
                                buyingPrice *= (1.0f - negotiatorSkillFactor * 0.3f);
                            }

                            int price = Mathf.Max(1, Mathf.RoundToInt(buyingPrice));
                            apparel.Destroy();
                            return price;
                        }
                    }
                }

                // Fallback: default pricing by def
                return GetItemPrice(itemDef, negotiator);
            }
            catch
            {
                var settings = RimMercenariesMod.ActiveSettings;
                return settings?.loadoutPerItemCost ?? 200;
            }
        }

        public void AddApparelCustomization(ThingDef apparelDef, ApparelCustomizationData customization)
        {
            apparelCustomizations[apparelDef] = customization;
        }

        public ApparelCustomizationData GetApparelCustomization(ThingDef apparelDef)
        {
            if (apparelCustomizations.TryGetValue(apparelDef, out var customization))
            {
                return customization;
            }
            return new ApparelCustomizationData(apparelDef);
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref selectedWeaponDef, "selectedWeaponDef");
            Scribe_Defs.Look(ref selectedWeaponStyle, "selectedWeaponStyle");
            Scribe_Collections.Look(ref selectedApparelDefs, "selectedApparelDefs", LookMode.Def);
            Scribe_Collections.Look(ref selectedBionics, "selectedBionics", LookMode.Deep);
            Scribe_Collections.Look(ref apparelCustomizations, "apparelCustomizations", LookMode.Def, LookMode.Deep, ref apparelCustomizationsWorkingKeys, ref apparelCustomizationsWorkingValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (selectedApparelDefs == null) selectedApparelDefs = new List<ThingDef>();
                if (selectedBionics == null) selectedBionics = new List<SelectedBionic>();
                if (apparelCustomizations == null) apparelCustomizations = new Dictionary<ThingDef, ApparelCustomizationData>();
                selectedBionics.RemoveAll(b => b == null || string.IsNullOrEmpty(b.bodyPartPath) || string.IsNullOrEmpty(b.hediffDefName));
                EnsureUniqueApparel();
            }
        }

        private List<ThingDef> apparelCustomizationsWorkingKeys;
        private List<ApparelCustomizationData> apparelCustomizationsWorkingValues;

        public MercenaryLoadoutSelection Clone()
        {
            var clone = new MercenaryLoadoutSelection();
            clone.selectedWeaponDef = this.selectedWeaponDef;
            clone.selectedWeaponStyle = this.selectedWeaponStyle;
            clone.selectedApparelDefs = this.selectedApparelDefs != null
                ? new List<ThingDef>(this.selectedApparelDefs)
                : new List<ThingDef>();
            clone.apparelCustomizations = new Dictionary<ThingDef, ApparelCustomizationData>();
            if (this.apparelCustomizations != null)
            {
                foreach (var kvp in this.apparelCustomizations)
                {
                    if (kvp.Key != null && kvp.Value != null)
                    {
                        clone.apparelCustomizations[kvp.Key] = kvp.Value.Clone();
                    }
                }
            }
            clone.selectedBionics = new List<SelectedBionic>();
            if (this.selectedBionics != null)
            {
                foreach (var b in this.selectedBionics)
                {
                    if (b == null) continue;
                    clone.selectedBionics.Add(new SelectedBionic
                    {
                        bodyPartPath = b.bodyPartPath,
                        bodyPartIndex = b.bodyPartIndex,
                        hediffDefName = b.hediffDefName,
                        recipeDefName = b.recipeDefName,
                        implantThingDefName = b.implantThingDefName
                    });
                }
            }
            clone.EnsureUniqueApparel();
            return clone;
        }
    }
}

