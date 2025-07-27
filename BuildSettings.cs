using Verse;
using System.Collections.Generic;
using RimWorld;

namespace RimMercenaries
{
    public class BuildSettings : IExposable
    {
        public int shooting = 3;
        public int melee = 3;
        public int medicine = -1; // -1 means none
        public int otherMax = 3;

        public void ExposeData()
        {
            Scribe_Values.Look(ref shooting, "shooting", 3);
            Scribe_Values.Look(ref melee, "melee", 3);
            Scribe_Values.Look(ref medicine, "medicine", -1);
            Scribe_Values.Look(ref otherMax, "otherMax", 3);
        }
    }
}
