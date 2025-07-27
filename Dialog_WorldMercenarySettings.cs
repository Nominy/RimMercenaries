using System;
using UnityEngine;
using Verse;

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
            list.Label(FollowingGlobals ? "Currently following global settings" : "Using world-specific settings");
            list.GapLine();

            // Freeze button - creates snapshot without changing any values
            Action deferred = null; // run after we EndScrollView 

            if (FollowingGlobals && list.ButtonText("Freeze Current Global Values Here")) 
            { 
                deferred = () => 
                { 
                    CommitEdits(); // creates snapshot identical to globals 
                    Close(); 
                }; 
            }

            // Return to global button - destroy local override
            if (list.ButtonText("Return to Global (Follow)")) 
            { 
                deferred = () => 
                { 
                    Comp.WorldSettings = null; // destroy local override 
                    Close(); 
                }; 
            }

            list.GapLine();

            list.Label("Tier 1 mercenaries:");
            int beforeI = working.tier1Count;
            list.TextFieldNumericLabeled("Count", ref working.tier1Count, ref t1Buf, 0, 100);
            if (working.tier1Count != beforeI) CommitEdits();

            beforeI = working.tier1Price.min;
            list.TextFieldNumericLabeled("Price min", ref working.tier1Price.min, ref t1PriceMinBuf, 0, 10000);
            if (working.tier1Price.min != beforeI) CommitEdits();

            beforeI = working.tier1Price.max;
            list.TextFieldNumericLabeled("Price max", ref working.tier1Price.max, ref t1PriceMaxBuf, 0, 10000);
            if (working.tier1Price.max != beforeI) CommitEdits();

            list.Label("Tier 2 mercenaries:");
            beforeI = working.tier2Count;
            list.TextFieldNumericLabeled("Count", ref working.tier2Count, ref t2Buf, 0, 100);
            if (working.tier2Count != beforeI) CommitEdits();

            beforeI = working.tier2Price.min;
            list.TextFieldNumericLabeled("Price min", ref working.tier2Price.min, ref t2PriceMinBuf, 0, 10000);
            if (working.tier2Price.min != beforeI) CommitEdits();

            beforeI = working.tier2Price.max;
            list.TextFieldNumericLabeled("Price max", ref working.tier2Price.max, ref t2PriceMaxBuf, 0, 10000);
            if (working.tier2Price.max != beforeI) CommitEdits();

            list.Label("Tier 3 mercenaries:");
            beforeI = working.tier3Count;
            list.TextFieldNumericLabeled("Count", ref working.tier3Count, ref t3Buf, 0, 100);
            if (working.tier3Count != beforeI) CommitEdits();

            beforeI = working.tier3Price.min;
            list.TextFieldNumericLabeled("Price min", ref working.tier3Price.min, ref t3PriceMinBuf, 0, 10000);
            if (working.tier3Price.min != beforeI) CommitEdits();

            beforeI = working.tier3Price.max;
            list.TextFieldNumericLabeled("Price max", ref working.tier3Price.max, ref t3PriceMaxBuf, 0, 10000);
            if (working.tier3Price.max != beforeI) CommitEdits();

            list.GapLine();
            list.Label("Refresh interval (days):");
            beforeI = working.refreshIntervalDays;
            list.TextFieldNumericLabeled("Days", ref working.refreshIntervalDays, ref refreshBuf, 1, 1000);
            if (working.refreshIntervalDays != beforeI) CommitEdits();

            list.Label("Period for which mercenaries can't be converted(days):");
            beforeI = working.mercenaryConversionPeriodDays;
            list.TextFieldNumericLabeled("Days", ref working.mercenaryConversionPeriodDays, ref mercenaryConversionPeriodDaysBuf, 1, 1000);
            if (working.mercenaryConversionPeriodDays != beforeI) CommitEdits();

            list.Label("Chance for mercenary to be converted after social interaction(0.00 - 1.00):");
            float beforeF = working.mercenaryConversionChance;
            list.TextFieldNumericLabeled("Chance", ref working.mercenaryConversionChance, ref mercenaryConversionChanceBuf, 0f, 1f);
            if (!Mathf.Approximately(working.mercenaryConversionChance, beforeF)) CommitEdits();

            list.GapLine();
            
            bool beforeB = working.mercenaryTraitsBuiltinRandom;
            list.CheckboxLabeled("Use RimWorld's builtin random traits", ref working.mercenaryTraitsBuiltinRandom);
            if (working.mercenaryTraitsBuiltinRandom != beforeB) CommitEdits();

            beforeB = working.mercenaryConversionEnabled;
            list.CheckboxLabeled("Enable mercenary conversion", ref working.mercenaryConversionEnabled);
            if (working.mercenaryConversionEnabled != beforeB) CommitEdits();

            list.GapLine();

            list.Label("Traits tier 1 disallowed:");

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

            list.Label("Traits tier 3 disallowed:");
            
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
    }
}
