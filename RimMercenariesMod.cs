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

            list.Label("Tier 1 mercenaries:");
            list.TextFieldNumericLabeled("Count", ref Settings.tier1Count, ref t1Buf, 0, 100);
            list.TextFieldNumericLabeled("Price min", ref Settings.tier1Price.min, ref t1PriceMinBuf, 0, 10000);
            list.TextFieldNumericLabeled("Price max", ref Settings.tier1Price.max, ref t1PriceMaxBuf, 0, 10000);

            list.Label("Tier 2 mercenaries:");
            list.TextFieldNumericLabeled("Count", ref Settings.tier2Count, ref t2Buf, 0, 100);
            list.TextFieldNumericLabeled("Price min", ref Settings.tier2Price.min, ref t2PriceMinBuf, 0, 10000);
            list.TextFieldNumericLabeled("Price max", ref Settings.tier2Price.max, ref t2PriceMaxBuf, 0, 10000);

            list.Label("Tier 3 mercenaries:");
            list.TextFieldNumericLabeled("Count", ref Settings.tier3Count, ref t3Buf, 0, 100);
            list.TextFieldNumericLabeled("Price min", ref Settings.tier3Price.min, ref t3PriceMinBuf, 0, 10000);
            list.TextFieldNumericLabeled("Price max", ref Settings.tier3Price.max, ref t3PriceMaxBuf, 0, 10000);

            list.GapLine();
            list.Label("Refresh interval (days):");
            list.TextFieldNumericLabeled("Days", ref Settings.refreshIntervalDays, ref refreshBuf, 1, 1000);
            list.Label("Period for which mercenaries can't be converted(days):");
            list.TextFieldNumericLabeled("Days", ref Settings.mercenaryConversionPeriodDays, ref mercenaryConversionPeriodDaysBuf, 1, 1000);
            list.Label("Chance for mercenary to be converted after social interaction(0.00 - 1.00):");
            list.TextFieldNumericLabeled("Chance", ref Settings.mercenaryConversionChance, ref mercenaryConversionChanceBuf, 0, 1);

            list.GapLine();

            list.CheckboxLabeled("Use RimWorld's builtin random traits", ref Settings.mercenaryTraitsBuiltinRandom);
            list.CheckboxLabeled("Enable mercenary conversion", ref Settings.mercenaryConversionEnabled);

            list.GapLine();

            if (list.ButtonText("Return to Default"))
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
            }
            list.GapLine();

            list.Label("Traits tier 1 disallowed:");

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

            list.Label("Traits tier 3 disallowed:");
            
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
    }
}
