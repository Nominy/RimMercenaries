using System;
using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace RimMercenaries
{
    public class Dialog_WorldMercenarySettings : Window
    {
        private RimMercenariesSettings working;
        private string t1Buf;
        private string t2Buf;
        private string t3Buf;
        private string refreshBuf;
        private string mercenaryConversionPeriodDaysBuf;
        private string mercenaryConversionChanceBuf;
        private string t1PriceMinBuf;
        private string t1PriceMaxBuf;
        private string t2PriceMinBuf;
        private string t2PriceMaxBuf;
        private string t3PriceMinBuf;
        private string t3PriceMaxBuf;
        private string loadoutPerItemCostBuf;
        private string bionicsStaticPriceBuf;
        private string disallowedBionicHediffsBuf;
        private string disallowedBionicImplantsBuf;
        private Vector2 scrollPos = Vector2.zero;
        private float viewHeight = 3000f;

        public override Vector2 InitialSize => new Vector2(500f, 700f);
        
        private MercenaryGameComponent Comp => Current.Game.GetComponent<MercenaryGameComponent>();
        
        private void CommitEdits()
        {
            // Create snapshot on first change
            if (Comp.WorldSettings == null)
                Comp.WorldSettings = RimMercenariesMod.Settings.Clone();
            Comp.WorldSettings.CopyFrom(working);
        }
        
        private bool FollowingGlobals => Comp.WorldSettings == null;

        public Dialog_WorldMercenarySettings()
        {
            forcePause = true;
            doCloseX = true;
            doCloseButton = true;
            // Show what the map effectively uses right now
            working = (FollowingGlobals ? RimMercenariesMod.Settings : Comp.WorldSettings).Clone();
            RefillBuffers();
        }

        private void RefillBuffers()
        {
            t1Buf = this.working.tier1Count.ToString();
            t2Buf = this.working.tier2Count.ToString();
            t3Buf = this.working.tier3Count.ToString();
            refreshBuf = this.working.refreshIntervalDays.ToString();
            t1PriceMinBuf = this.working.tier1Price.min.ToString();
            t1PriceMaxBuf = this.working.tier1Price.max.ToString();
            t2PriceMinBuf = this.working.tier2Price.min.ToString();
            t2PriceMaxBuf = this.working.tier2Price.max.ToString();
            t3PriceMinBuf = this.working.tier3Price.min.ToString();
            t3PriceMaxBuf = this.working.tier3Price.max.ToString();
            mercenaryConversionPeriodDaysBuf = this.working.mercenaryConversionPeriodDays.ToString();
            mercenaryConversionChanceBuf = this.working.mercenaryConversionChance.ToString();
            loadoutPerItemCostBuf = this.working.loadoutPerItemCost.ToString();
            bionicsStaticPriceBuf = this.working.bionicsStaticPrice.ToString();
            disallowedBionicHediffsBuf = PatternsToBuffer(this.working.disallowedBionicHediffs);
            disallowedBionicImplantsBuf = PatternsToBuffer(this.working.disallowedBionicImplants);
        }

        // No auto-save on close - immediate mode already saves on each change
        public override void PreClose()
        {
            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            var outRect = inRect.ContractedBy(10f);
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);

            var list = new Listing_Standard();
            list.Begin(viewRect);

            // Status indicator
            list.Label(FollowingGlobals ? "RimMercenaries_CurrentlyFollowingGlobals".Translate() : "RimMercenaries_UsingWorldSpecific".Translate());
            list.GapLine();

            // Freeze button - creates snapshot without changing any values
            Action deferred = null; // run after we EndScrollView 

            if (FollowingGlobals && list.ButtonText("RimMercenaries_FreezeGlobalsHere".Translate()))
            { 
                deferred = () => 
                { 
                    CommitEdits(); // creates snapshot identical to globals 
                    Close(); 
                }; 
            }

            // Return to global button - destroy local override
            if (list.ButtonText("RimMercenaries_ReturnToGlobal".Translate()))
            { 
                deferred = () => 
                { 
                    Comp.WorldSettings = null; // destroy local override 
                    Close(); 
                }; 
            }

            list.GapLine();

            list.Label("RimMercenaries_Tier1Section".Translate());
            int beforeI = working.tier1Count;
            list.TextFieldNumericLabeled("RimMercenaries_Count".Translate(), ref working.tier1Count, ref t1Buf, 0, 100);
            if (working.tier1Count != beforeI) CommitEdits();

            beforeI = working.tier1Price.min;
            list.TextFieldNumericLabeled("RimMercenaries_PriceMin".Translate(), ref working.tier1Price.min, ref t1PriceMinBuf, 0, 10000);
            if (working.tier1Price.min != beforeI) CommitEdits();

            beforeI = working.tier1Price.max;
            list.TextFieldNumericLabeled("RimMercenaries_PriceMax".Translate(), ref working.tier1Price.max, ref t1PriceMaxBuf, 0, 10000);
            if (working.tier1Price.max != beforeI) CommitEdits();

            list.Label("RimMercenaries_Tier2Section".Translate());
            beforeI = working.tier2Count;
            list.TextFieldNumericLabeled("RimMercenaries_Count".Translate(), ref working.tier2Count, ref t2Buf, 0, 100);
            if (working.tier2Count != beforeI) CommitEdits();

            beforeI = working.tier2Price.min;
            list.TextFieldNumericLabeled("RimMercenaries_PriceMin".Translate(), ref working.tier2Price.min, ref t2PriceMinBuf, 0, 10000);
            if (working.tier2Price.min != beforeI) CommitEdits();

            beforeI = working.tier2Price.max;
            list.TextFieldNumericLabeled("RimMercenaries_PriceMax".Translate(), ref working.tier2Price.max, ref t2PriceMaxBuf, 0, 10000);
            if (working.tier2Price.max != beforeI) CommitEdits();

            list.Label("RimMercenaries_Tier3Section".Translate());
            beforeI = working.tier3Count;
            list.TextFieldNumericLabeled("RimMercenaries_Count".Translate(), ref working.tier3Count, ref t3Buf, 0, 100);
            if (working.tier3Count != beforeI) CommitEdits();

            beforeI = working.tier3Price.min;
            list.TextFieldNumericLabeled("RimMercenaries_PriceMin".Translate(), ref working.tier3Price.min, ref t3PriceMinBuf, 0, 10000);
            if (working.tier3Price.min != beforeI) CommitEdits();

            beforeI = working.tier3Price.max;
            list.TextFieldNumericLabeled("RimMercenaries_PriceMax".Translate(), ref working.tier3Price.max, ref t3PriceMaxBuf, 0, 10000);
            if (working.tier3Price.max != beforeI) CommitEdits();

            list.GapLine();
            list.Label("RimMercenaries_RefreshInterval".Translate());
            beforeI = working.refreshIntervalDays;
            list.TextFieldNumericLabeled("RimMercenaries_Days".Translate(), ref working.refreshIntervalDays, ref refreshBuf, 1, 1000);
            if (working.refreshIntervalDays != beforeI) CommitEdits();

            list.Label("RimMercenaries_ConversionPeriod".Translate());
            beforeI = working.mercenaryConversionPeriodDays;
            list.TextFieldNumericLabeled("RimMercenaries_Days".Translate(), ref working.mercenaryConversionPeriodDays, ref mercenaryConversionPeriodDaysBuf, 1, 1000);
            if (working.mercenaryConversionPeriodDays != beforeI) CommitEdits();

            list.Label("RimMercenaries_ConversionChance".Translate());
            float beforeF = working.mercenaryConversionChance;
            list.TextFieldNumericLabeled("RimMercenaries_Chance".Translate(), ref working.mercenaryConversionChance, ref mercenaryConversionChanceBuf, 0f, 1f);
            if (!Mathf.Approximately(working.mercenaryConversionChance, beforeF)) CommitEdits();

            list.GapLine();
            
            bool beforeB = working.mercenaryTraitsBuiltinRandom;
            list.CheckboxLabeled("RimMercenaries_BuiltinRandomTraits".Translate(), ref working.mercenaryTraitsBuiltinRandom);
            if (working.mercenaryTraitsBuiltinRandom != beforeB) CommitEdits();

            beforeB = working.mercenaryConversionEnabled;
            list.CheckboxLabeled("RimMercenaries_EnableConversion".Translate(), ref working.mercenaryConversionEnabled);
            if (working.mercenaryConversionEnabled != beforeB) CommitEdits();

            list.GapLine();
            list.Label("RimMercenaries_LoadoutSettings".Translate());

            beforeB = working.enableDevLoadoutCustomization;
            list.CheckboxLabeled("RimMercenaries_EnableLoadoutCustomization".Translate(), ref working.enableDevLoadoutCustomization);
            if (working.enableDevLoadoutCustomization != beforeB) CommitEdits();

            beforeB = working.useActualItemPrices;
            list.CheckboxLabeled("RimMercenaries_UseActualItemPrices".Translate(), ref working.useActualItemPrices);
            if (working.useActualItemPrices != beforeB) CommitEdits();

            beforeI = working.loadoutPerItemCost;
            list.TextFieldNumericLabeled("RimMercenaries_LoadoutPerItemCost".Translate(), ref working.loadoutPerItemCost, ref loadoutPerItemCostBuf, 0, 10000);
            if (working.loadoutPerItemCost != beforeI) CommitEdits();

            list.GapLine();
            list.Label("RimMercenaries_BionicsSettings".Translate());

            bool beforeBionicsEnabled = working.enableBionicsCustomization;
            list.CheckboxLabeled("RimMercenaries_EnableBionicsCustomization".Translate(), ref working.enableBionicsCustomization);
            if (working.enableBionicsCustomization != beforeBionicsEnabled) CommitEdits();

            list.Label("RimMercenaries_BionicsPricingMode".Translate());
            var bm = working.bionicsPricingMode;
            var calcBtn = list.ButtonTextLabeled("RimMercenaries_BionicsPricing_Calculated".Translate(), bm == BionicsPricingMode.Calculated ? "RimMercenaries_Selected".Translate() : "".Translate());
            if (calcBtn)
            {
                working.bionicsPricingMode = BionicsPricingMode.Calculated;
                CommitEdits();
            }
            var staticBtn = list.ButtonTextLabeled("RimMercenaries_BionicsPricing_Static".Translate(), bm == BionicsPricingMode.Static ? "RimMercenaries_Selected".Translate() : "".Translate());
            if (staticBtn)
            {
                working.bionicsPricingMode = BionicsPricingMode.Static;
                CommitEdits();
            }

            if (working.bionicsPricingMode == BionicsPricingMode.Static)
            {
                int beforeStatic = working.bionicsStaticPrice;
                list.TextFieldNumericLabeled("RimMercenaries_BionicsStaticPrice".Translate(), ref working.bionicsStaticPrice, ref bionicsStaticPriceBuf, 0, 100000);
                if (working.bionicsStaticPrice != beforeStatic) CommitEdits();
            }

            bool beforeArchotech = working.disallowArchotechBionics;
            list.CheckboxLabeled("RimMercenaries_DisallowArchotechBionics".Translate(), ref working.disallowArchotechBionics);
            if (working.disallowArchotechBionics != beforeArchotech) CommitEdits();

            list.Label("RimMercenaries_DisallowedBionicHediffs".Translate());
            Rect hediffRect = list.GetRect(60f);
            string newHediffBuf = Widgets.TextArea(hediffRect, disallowedBionicHediffsBuf ?? string.Empty);
            if (newHediffBuf != disallowedBionicHediffsBuf)
            {
                disallowedBionicHediffsBuf = newHediffBuf;
                working.disallowedBionicHediffs = ParsePatternList(disallowedBionicHediffsBuf);
                CommitEdits();
            }

            list.Label("RimMercenaries_DisallowedBionicImplants".Translate());
            Rect implantRect = list.GetRect(60f);
            string newImplantBuf = Widgets.TextArea(implantRect, disallowedBionicImplantsBuf ?? string.Empty);
            if (newImplantBuf != disallowedBionicImplantsBuf)
            {
                disallowedBionicImplantsBuf = newImplantBuf;
                working.disallowedBionicImplants = ParsePatternList(disallowedBionicImplantsBuf);
                CommitEdits();
            }

            list.GapLine();

            list.Label("RimMercenaries_TraitsTier1Disallowed".Translate());

            foreach (var trait in MercenaryOfferGenerator.BadTraits)
            {
                bool allowed = working.TraitAllowed(trait.defName);
                bool beforeAllowed = allowed;
                list.CheckboxLabeled(trait.defName, ref allowed);
                if (allowed != beforeAllowed)
                {
                    if (allowed) working.disabledTraits.Remove(trait.defName);
                    else if (!working.disabledTraits.Contains(trait.defName)) working.disabledTraits.Add(trait.defName);
                    CommitEdits();
                }
            }

            list.GapLine();

            list.Label("RimMercenaries_TraitsTier3Disallowed".Translate());
            
            foreach (var trait in MercenaryOfferGenerator.GoodTraits)
            {
                bool allowed = working.TraitAllowed(trait.defName);
                bool beforeAllowed = allowed;
                list.CheckboxLabeled(trait.defName, ref allowed);
                if (allowed != beforeAllowed)
                {
                    if (allowed) working.disabledTraits.Remove(trait.defName);
                    else if (!working.disabledTraits.Contains(trait.defName)) working.disabledTraits.Add(trait.defName);
                    CommitEdits();
                }
            }

            list.Gap();

            viewHeight = Mathf.Max(list.CurHeight, outRect.height + 1f);

            list.End(); 
            Widgets.EndScrollView(); 

            deferred?.Invoke();
        }

        private string PatternsToBuffer(List<string> patterns)
        {
            if (patterns == null || patterns.Count == 0) return string.Empty;
            return string.Join("\n", patterns);
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
