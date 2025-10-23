using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimMercenaries
{
    public class RimMercenariesMod : Mod
    {
        private Vector2 scrollPos = Vector2.zero;
        private float viewHeight = 3000f;


        public static RimMercenariesSettings Settings;

        private string t1Buf = "10";
        private string t2Buf = "5";
        private string t3Buf = "2";
        private string refreshBuf = "60";
        private string t1PriceMinBuf = "450";
        private string t1PriceMaxBuf = "750";
        private string t2PriceMinBuf = "950";
        private string t2PriceMaxBuf = "1300";
        private string t3PriceMinBuf = "1850";
        private string t3PriceMaxBuf = "2500";
        private string mercenaryConversionPeriodDaysBuf = "60";
        private string mercenaryConversionChanceBuf = "0.02";
        private string loadoutPerItemCostBuf = "200";
        private string bionicsStaticPriceBuf = "1200";
        private string disallowedBionicHediffsBuf = string.Empty;
        private string disallowedBionicImplantsBuf = string.Empty;
        public RimMercenariesMod(ModContentPack content) : base(content)
        {   
            Settings = GetSettings<RimMercenariesSettings>();
            t1Buf = Settings.tier1Count.ToString();
            t2Buf = Settings.tier2Count.ToString();
            t3Buf = Settings.tier3Count.ToString();
            refreshBuf = Settings.refreshIntervalDays.ToString();
            t1PriceMinBuf = Settings.tier1Price.min.ToString();
            t1PriceMaxBuf = Settings.tier1Price.max.ToString();
            t2PriceMinBuf = Settings.tier2Price.min.ToString();
            t2PriceMaxBuf = Settings.tier2Price.max.ToString();
            t3PriceMinBuf = Settings.tier3Price.min.ToString();
            t3PriceMaxBuf = Settings.tier3Price.max.ToString();
            mercenaryConversionPeriodDaysBuf = Settings.mercenaryConversionPeriodDays.ToString();
            mercenaryConversionChanceBuf = Settings.mercenaryConversionChance.ToString();
            loadoutPerItemCostBuf = Settings.loadoutPerItemCost.ToString();
            bionicsStaticPriceBuf = Settings.bionicsStaticPrice.ToString();
            disallowedBionicHediffsBuf = PatternsToBuffer(Settings.disallowedBionicHediffs);
            disallowedBionicImplantsBuf = PatternsToBuffer(Settings.disallowedBionicImplants);
        }

        public static RimMercenariesSettings ActiveSettings
        {
            get
            {
                var comp = Current.Game?.GetComponent<MercenaryGameComponent>();
                return comp?.WorldSettings ?? Settings;
            }
        }

        public override string SettingsCategory() => "RimMercenaries";

       public override void DoSettingsWindowContents(Rect inRect)
        {
            var outRect = inRect.ContractedBy(10f);
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);

            var list = new Listing_Standard();
            list.Begin(viewRect);

            list.Label("RimMercenaries_Tier1Section".Translate());
            list.TextFieldNumericLabeled("RimMercenaries_Count".Translate(), ref Settings.tier1Count, ref t1Buf, 0, 100);
            list.TextFieldNumericLabeled("RimMercenaries_PriceMin".Translate(), ref Settings.tier1Price.min, ref t1PriceMinBuf, 0, 10000);
            list.TextFieldNumericLabeled("RimMercenaries_PriceMax".Translate(), ref Settings.tier1Price.max, ref t1PriceMaxBuf, 0, 10000);

            list.Label("RimMercenaries_Tier2Section".Translate());
            list.TextFieldNumericLabeled("RimMercenaries_Count".Translate(), ref Settings.tier2Count, ref t2Buf, 0, 100);
            list.TextFieldNumericLabeled("RimMercenaries_PriceMin".Translate(), ref Settings.tier2Price.min, ref t2PriceMinBuf, 0, 10000);
            list.TextFieldNumericLabeled("RimMercenaries_PriceMax".Translate(), ref Settings.tier2Price.max, ref t2PriceMaxBuf, 0, 10000);

            list.Label("RimMercenaries_Tier3Section".Translate());
            list.TextFieldNumericLabeled("RimMercenaries_Count".Translate(), ref Settings.tier3Count, ref t3Buf, 0, 100);
            list.TextFieldNumericLabeled("RimMercenaries_PriceMin".Translate(), ref Settings.tier3Price.min, ref t3PriceMinBuf, 0, 10000);
            list.TextFieldNumericLabeled("RimMercenaries_PriceMax".Translate(), ref Settings.tier3Price.max, ref t3PriceMaxBuf, 0, 10000);

            list.GapLine();
            list.Label("RimMercenaries_RefreshInterval".Translate());
            list.TextFieldNumericLabeled("RimMercenaries_Days".Translate(), ref Settings.refreshIntervalDays, ref refreshBuf, 1, 1000);
            list.Label("RimMercenaries_ConversionPeriod".Translate());
            list.TextFieldNumericLabeled("RimMercenaries_Days".Translate(), ref Settings.mercenaryConversionPeriodDays, ref mercenaryConversionPeriodDaysBuf, 1, 1000);
            list.Label("RimMercenaries_ConversionChance".Translate());
            list.TextFieldNumericLabeled("RimMercenaries_Chance".Translate(), ref Settings.mercenaryConversionChance, ref mercenaryConversionChanceBuf, 0, 1);

            list.GapLine();

            list.CheckboxLabeled("RimMercenaries_BuiltinRandomTraits".Translate(), ref Settings.mercenaryTraitsBuiltinRandom);
            list.CheckboxLabeled("RimMercenaries_EnableConversion".Translate(), ref Settings.mercenaryConversionEnabled);

            list.GapLine();

            // Loadout settings section
            list.Label("RimMercenaries_LoadoutSettings".Translate());
            list.CheckboxLabeled("RimMercenaries_EnableLoadoutCustomization".Translate(), ref Settings.enableDevLoadoutCustomization);
            list.CheckboxLabeled("RimMercenaries_UseActualItemPrices".Translate(), ref Settings.useActualItemPrices);
            list.TextFieldNumericLabeled("RimMercenaries_LoadoutPerItemCost".Translate(), ref Settings.loadoutPerItemCost, ref loadoutPerItemCostBuf, 0, 100000);

            list.GapLine();
            list.Label("RimMercenaries_BionicsSettings".Translate());
            bool beforeBionicsEnabled = Settings.enableBionicsCustomization;
            list.CheckboxLabeled("RimMercenaries_EnableBionicsCustomization".Translate(), ref Settings.enableBionicsCustomization);
            if (Settings.enableBionicsCustomization != beforeBionicsEnabled)
            {
                Settings.Write();
            }

            list.Label("RimMercenaries_BionicsPricingMode".Translate());
            // Safer UI than radio to avoid API/overload issues: two small buttons
            var bm = Settings.bionicsPricingMode;
            var calcBtn = list.ButtonTextLabeled("RimMercenaries_BionicsPricing_Calculated".Translate(), bm == BionicsPricingMode.Calculated ? "RimMercenaries_Selected".Translate() : "".Translate());
            if (calcBtn)
            {
                Settings.bionicsPricingMode = BionicsPricingMode.Calculated;
                Settings.Write();
            }
            var staticBtn = list.ButtonTextLabeled("RimMercenaries_BionicsPricing_Static".Translate(), bm == BionicsPricingMode.Static ? "RimMercenaries_Selected".Translate() : "".Translate());
            if (staticBtn)
            {
                Settings.bionicsPricingMode = BionicsPricingMode.Static;
                Settings.Write();
            }

            if (Settings.bionicsPricingMode == BionicsPricingMode.Static)
            {
                int beforeStatic = Settings.bionicsStaticPrice;
                list.TextFieldNumericLabeled("RimMercenaries_BionicsStaticPrice".Translate(), ref Settings.bionicsStaticPrice, ref bionicsStaticPriceBuf, 0, 100000);
                if (Settings.bionicsStaticPrice != beforeStatic)
                {
                    Settings.Write();
                }
            }

            bool beforeArchotech = Settings.disallowArchotechBionics;
            list.CheckboxLabeled("RimMercenaries_DisallowArchotechBionics".Translate(), ref Settings.disallowArchotechBionics);
            if (Settings.disallowArchotechBionics != beforeArchotech)
            {
                Settings.Write();
            }

            list.Label("RimMercenaries_DisallowedBionicHediffs".Translate());
            Rect hediffRect = list.GetRect(60f);
            string newHediffBuf = Widgets.TextArea(hediffRect, disallowedBionicHediffsBuf ?? string.Empty);
            if (newHediffBuf != disallowedBionicHediffsBuf)
            {
                disallowedBionicHediffsBuf = newHediffBuf;
                Settings.disallowedBionicHediffs = ParsePatternList(disallowedBionicHediffsBuf);
                Settings.Write();
            }

            list.Label("RimMercenaries_DisallowedBionicImplants".Translate());
            Rect implantRect = list.GetRect(60f);
            string newImplantBuf = Widgets.TextArea(implantRect, disallowedBionicImplantsBuf ?? string.Empty);
            if (newImplantBuf != disallowedBionicImplantsBuf)
            {
                disallowedBionicImplantsBuf = newImplantBuf;
                Settings.disallowedBionicImplants = ParsePatternList(disallowedBionicImplantsBuf);
                Settings.Write();
            }

            // Dev-only section for additional customization
            if (Prefs.DevMode)
            {
                list.GapLine();
                list.Label("RimMercenaries_DevSection".Translate());
                // Loadout customization setting moved to regular settings section above
            }

            if (list.ButtonText("RimMercenaries_ReturnToDefault".Translate()))
            {
                Settings.ResetToDefaults();
                // Update buffer values
                t1Buf = Settings.tier1Count.ToString();
                t2Buf = Settings.tier2Count.ToString();
                t3Buf = Settings.tier3Count.ToString();
                refreshBuf = Settings.refreshIntervalDays.ToString();
                t1PriceMinBuf = Settings.tier1Price.min.ToString();
                t1PriceMaxBuf = Settings.tier1Price.max.ToString();
                t2PriceMinBuf = Settings.tier2Price.min.ToString();
                t2PriceMaxBuf = Settings.tier2Price.max.ToString();
                t3PriceMinBuf = Settings.tier3Price.min.ToString();
                t3PriceMaxBuf = Settings.tier3Price.max.ToString();
                mercenaryConversionPeriodDaysBuf = Settings.mercenaryConversionPeriodDays.ToString();
                mercenaryConversionChanceBuf = Settings.mercenaryConversionChance.ToString();
                loadoutPerItemCostBuf = Settings.loadoutPerItemCost.ToString();
                bionicsStaticPriceBuf = Settings.bionicsStaticPrice.ToString();
                disallowedBionicHediffsBuf = PatternsToBuffer(Settings.disallowedBionicHediffs);
                disallowedBionicImplantsBuf = PatternsToBuffer(Settings.disallowedBionicImplants);
                // Note: useActualItemPrices is a boolean, no buffer needed
            }
            list.GapLine();

            list.Label("RimMercenaries_TraitsTier1Disallowed".Translate());

            foreach (var trait in MercenaryOfferGenerator.BadTraits)
            {
                bool allowed = Settings.TraitAllowed(trait.defName);
                list.CheckboxLabeled(trait.defName, ref allowed);
                if (allowed)
                    Settings.disabledTraits.Remove(trait.defName);
                else if (!Settings.disabledTraits.Contains(trait.defName))
                    Settings.disabledTraits.Add(trait.defName);
            }

            list.GapLine();

            list.Label("RimMercenaries_TraitsTier3Disallowed".Translate());
            
            foreach (var trait in MercenaryOfferGenerator.GoodTraits)
            {
                bool allowed = Settings.TraitAllowed(trait.defName);
                list.CheckboxLabeled(trait.defName, ref allowed);
                if (allowed)
                    Settings.disabledTraits.Remove(trait.defName);
                else if (!Settings.disabledTraits.Contains(trait.defName))
                    Settings.disabledTraits.Add(trait.defName);
            }

            list.Gap();

            viewHeight = Mathf.Max(list.CurHeight, outRect.height + 1f);

            list.End();
            Widgets.EndScrollView();
        }

        private string PatternsToBuffer(List<string> patterns)
        {
            if (patterns == null || patterns.Count == 0) return string.Empty;
            return string.Join(Environment.NewLine, patterns);
        }

        private List<string> ParsePatternList(string buffer)
        {
            if (string.IsNullOrWhiteSpace(buffer)) return new List<string>();
            var list = new List<string>();
            var lines = buffer.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    list.Add(trimmed);
                }
            }
            return list;
        }
    }
}
