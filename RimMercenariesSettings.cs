using System.Collections.Generic;
using Verse;

namespace RimMercenaries
{
    public class RimMercenariesSettings : ModSettings
    {
        public List<string> disabledTraits = new List<string>();
        public int tier1Count = 10;
        public int tier2Count = 5;
        public int tier3Count = 2;
        public BuildSettings tier1Build = new BuildSettings { shooting = 3, melee = 3, medicine = -1, otherMax = 3 };
        public BuildSettings tier2Build = new BuildSettings { shooting = 5, melee = 5, medicine = -1, otherMax = 5 };
        public BuildSettings tier3Build = new BuildSettings { shooting = 10, melee = 10, medicine = 7, otherMax = 4 };
        public IntRange tier1Price = new IntRange(450, 750);
        public IntRange tier2Price = new IntRange(950, 1300);
        public IntRange tier3Price = new IntRange(1850, 2500);
        public int refreshIntervalDays = 60;
        public int mercenaryConversionPeriodDays = 60;
        public float mercenaryConversionChance = 0.02f;
        public bool mercenaryConversionEnabled = true;
        public bool mercenaryTraitsBuiltinRandom = false;

        // Dev-only: enable customization dialog and per-item cost for gear picks
        public bool enableDevLoadoutCustomization = false;
        public int loadoutPerItemCost = 200;

        // Use actual item prices instead of fixed cost per item (default: true)
        public bool useActualItemPrices = true;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref disabledTraits, "disabledTraits", LookMode.Value);
            Scribe_Values.Look(ref tier1Count, "tier1Count", 10);
            Scribe_Values.Look(ref tier2Count, "tier2Count", 5);
            Scribe_Values.Look(ref tier3Count, "tier3Count", 2);
            Scribe_Deep.Look(ref tier1Build, "tier1Build");
            Scribe_Deep.Look(ref tier2Build, "tier2Build");
            Scribe_Deep.Look(ref tier3Build, "tier3Build");
            Scribe_Values.Look(ref tier1Price, "tier1Price", new IntRange(450, 750));
            Scribe_Values.Look(ref tier2Price, "tier2Price", new IntRange(950, 1300));
            Scribe_Values.Look(ref tier3Price, "tier3Price", new IntRange(1850, 2500));
            Scribe_Values.Look(ref refreshIntervalDays, "refreshIntervalDays", 60);
            Scribe_Values.Look(ref mercenaryConversionPeriodDays, "mercenaryConversionPeriodDays", 60);
            Scribe_Values.Look(ref mercenaryConversionChance, "mercenaryConversionChance", 0.02f);
            Scribe_Values.Look(ref mercenaryConversionEnabled, "mercenaryConversionEnabled", true);
            Scribe_Values.Look(ref mercenaryTraitsBuiltinRandom, "mercenaryTraitsBuiltinRandom", false);
            Scribe_Values.Look(ref enableDevLoadoutCustomization, "enableDevLoadoutCustomization", false);
            Scribe_Values.Look(ref loadoutPerItemCost, "loadoutPerItemCost", 200);
            Scribe_Values.Look(ref useActualItemPrices, "useActualItemPrices", true);
        }

        public RimMercenariesSettings Clone()
        {
            var s = new RimMercenariesSettings();
            s.disabledTraits = new List<string>(disabledTraits);
            s.tier1Count = tier1Count;
            s.tier2Count = tier2Count;
            s.tier3Count = tier3Count;
            s.tier1Build = new BuildSettings { shooting = tier1Build.shooting, melee = tier1Build.melee, medicine = tier1Build.medicine, otherMax = tier1Build.otherMax };
            s.tier2Build = new BuildSettings { shooting = tier2Build.shooting, melee = tier2Build.melee, medicine = tier2Build.medicine, otherMax = tier2Build.otherMax };
            s.tier3Build = new BuildSettings { shooting = tier3Build.shooting, melee = tier3Build.melee, medicine = tier3Build.medicine, otherMax = tier3Build.otherMax };
            s.tier1Price = tier1Price;
            s.tier2Price = tier2Price;
            s.tier3Price = tier3Price;
            s.refreshIntervalDays = refreshIntervalDays;
            s.mercenaryConversionPeriodDays = mercenaryConversionPeriodDays;
            s.mercenaryConversionChance = mercenaryConversionChance;
            s.mercenaryConversionEnabled = mercenaryConversionEnabled;
            s.mercenaryTraitsBuiltinRandom = mercenaryTraitsBuiltinRandom;
            s.enableDevLoadoutCustomization = enableDevLoadoutCustomization;
            s.loadoutPerItemCost = loadoutPerItemCost;
            s.useActualItemPrices = useActualItemPrices;
            return s;
        }


        public void CopyFrom(RimMercenariesSettings o)
        {
            tier1Count = o.tier1Count;
            tier2Count = o.tier2Count;
            tier3Count = o.tier3Count;
            refreshIntervalDays = o.refreshIntervalDays;
            mercenaryConversionPeriodDays = o.mercenaryConversionPeriodDays;
            mercenaryConversionChance = o.mercenaryConversionChance;
            mercenaryTraitsBuiltinRandom = o.mercenaryTraitsBuiltinRandom;
            mercenaryConversionEnabled = o.mercenaryConversionEnabled;

            tier1Price = o.tier1Price;
            tier2Price = o.tier2Price;
            tier3Price = o.tier3Price;

            enableDevLoadoutCustomization = o.enableDevLoadoutCustomization;
            loadoutPerItemCost = o.loadoutPerItemCost;
            useActualItemPrices = o.useActualItemPrices;

            disabledTraits.Clear();
            disabledTraits.AddRange(o.disabledTraits);
        }

        public void Apply()
        {
            // Instead of replacing the build objects, update their properties
            // This preserves object references that may be held by MercenaryOffer instances
            var build1 = MercenaryBuilds.Builds[1];
            var build2 = MercenaryBuilds.Builds[2];
            var build3 = MercenaryBuilds.Builds[3];
            
            // Update build1 properties
            build1.ShootingLevel = tier1Build.shooting;
            build1.MeleeLevel = tier1Build.melee;
            build1.MedicineLevel = tier1Build.medicine >= 0 ? (int?)tier1Build.medicine : null;
            build1.OtherSkillMax = tier1Build.otherMax;
            
            // Update build2 properties
            build2.ShootingLevel = tier2Build.shooting;
            build2.MeleeLevel = tier2Build.melee;
            build2.MedicineLevel = tier2Build.medicine >= 0 ? (int?)tier2Build.medicine : null;
            build2.OtherSkillMax = tier2Build.otherMax;
            
            // Update build3 properties
            build3.ShootingLevel = tier3Build.shooting;
            build3.MeleeLevel = tier3Build.melee;
            build3.MedicineLevel = tier3Build.medicine >= 0 ? (int?)tier3Build.medicine : null;
            build3.OtherSkillMax = tier3Build.otherMax;
        }

        public bool TraitAllowed(string defName)
        {
            return !disabledTraits.Contains(defName);
        }
        
        public void ResetToDefaults()
        {
            disabledTraits.Clear();
            tier1Count = 10;
            tier2Count = 5;
            tier3Count = 2;
            tier1Build = new BuildSettings { shooting = 3, melee = 3, medicine = -1, otherMax = 3 };
            tier2Build = new BuildSettings { shooting = 5, melee = 5, medicine = -1, otherMax = 5 };
            tier3Build = new BuildSettings { shooting = 10, melee = 10, medicine = 7, otherMax = 4 };
            tier1Price = new IntRange(450, 750);
            tier2Price = new IntRange(950, 1300);
            tier3Price = new IntRange(1850, 2500);
            refreshIntervalDays = 60;
            mercenaryConversionPeriodDays = 60;
            mercenaryConversionChance = 0.02f;
            mercenaryConversionEnabled = true;
            mercenaryTraitsBuiltinRandom = false;
            enableDevLoadoutCustomization = false;
            loadoutPerItemCost = 200;
            useActualItemPrices = true;
        }
    }
}
