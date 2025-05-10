using System.Collections.Generic;

namespace RimMercenaries
{
    public class MercenaryBuild
    {
        public int ShootingLevel { get; }
        public int MeleeLevel { get; }
        public int? MedicineLevel { get; }
        public int OtherSkillMax { get; }

        public MercenaryBuild(int shootingLevel, int meleeLevel, int? medicineLevel, int otherSkillMax)
        {
            ShootingLevel = shootingLevel;
            MeleeLevel = meleeLevel;
            MedicineLevel = medicineLevel;
            OtherSkillMax = otherSkillMax;
        }
    }

    public static class MercenaryBuilds
    {
        public static readonly Dictionary<int, MercenaryBuild> Builds = new Dictionary<int, MercenaryBuild>
        {
            { 1, new MercenaryBuild(3, 3, null, 3) },
            { 2, new MercenaryBuild(5, 5, null, 5) },
            { 3, new MercenaryBuild(10, 10, 7, 4) }
        };
    }
}