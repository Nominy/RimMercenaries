using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimMercenaries
{
    public class MercenaryOfferGenerator
    {
        private static readonly Dictionary<TraitDef, int[]> TraitPool = new Dictionary<TraitDef, int[]>
        {
            { TraitDefOf.Greedy, new[] { 0 } },
            { TraitDefOf.Pyromaniac, new[] { 0 } },
            { TraitDef.Named("Cannibal"), new[] { 0 } },
            { TraitDef.Named("SlowLearner"), new[] { 0 } },
            { TraitDefOf.DislikesMen, new[] { 0 } },
            { TraitDefOf.DislikesWomen, new[] { 0 } },
            { TraitDefOf.DrugDesire, new[] { 1, 2 } },
            { TraitDefOf.Industriousness, new[] { -1, -2 } },
            { TraitDefOf.AnnoyingVoice, new[] { 0 } },
            { TraitDefOf.CreepyBreathing, new[] { 0 } },
            { TraitDefOf.Bloodlust, new[] { 0 } },
            { TraitDef.Named("Tough"), new[] { 0 } },
            { TraitDef.Named("SpeedOffset"), new[] { 1 } },
            { TraitDef.Named("PsychicSensitivity"), new[] { -1, 1 } },
            { TraitDef.Named("FastLearner"), new[] { 0 } },
            { TraitDef.Named("QuickSleeper"), new[] { 0 } }
        };

        private static readonly List<TraitDef> BadTraits = new[] {
            TraitDefOf.Greedy, TraitDefOf.Pyromaniac, TraitDef.Named("Cannibal"),
            TraitDef.Named("SlowLearner"), TraitDefOf.DislikesMen, TraitDefOf.DislikesWomen,
            TraitDefOf.DrugDesire, TraitDefOf.Industriousness, TraitDefOf.AnnoyingVoice,
            TraitDefOf.CreepyBreathing
        }.ToList();

        private static readonly List<TraitDef> GoodTraits = new[] {
            TraitDefOf.Bloodlust, TraitDef.Named("Tough"), TraitDef.Named("SpeedOffset"),
            TraitDef.Named("PsychicSensitivity"), TraitDef.Named("FastLearner"),
            TraitDef.Named("QuickSleeper")
        }.ToList();

        public static MercenaryOffer GenerateOffer(Map map, int buildType, XenotypeDef forcedXenotype = null)
        {
            var build = MercenaryBuilds.Builds[buildType];
            var allowedXenotypes = forcedXenotype == null ? DefDatabase<XenotypeDef>.AllDefsListForReading : null;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                var request = new PawnGenerationRequest(
                    kind: PawnKindDefOf.Colonist,
                    faction: null,
                    context: PawnGenerationContext.NonPlayer,
                    tile: map?.Tile ?? -1,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: true,
                    mustBeCapableOfViolence: forcedXenotype?.canGenerateAsCombatant ?? true,
                    colonistRelationChanceFactor: 0f,
                    forceAddFreeWarmLayerIfNeeded: false,
                    allowGay: true,
                    allowFood: true,
                    inhabitant: false,
                    certainlyBeenInCryptosleep: false,
                    developmentalStages: DevelopmentalStage.Adult,
                    forcedXenotype: forcedXenotype,
                    forcedCustomXenotype: null,
                    allowedXenotypes: allowedXenotypes,
                    forceBaselinerChance: 0f,
                    fixedIdeo: Faction.OfPlayer.ideos.PrimaryIdeo,
                    biologicalAgeRange: new FloatRange(17f, 55f)
                );

                var pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null) continue;

                if (forcedXenotype != null && (pawn.genes == null || pawn.genes.Xenotype != forcedXenotype))
                {
                    MercenaryOffer.DiscardPawnIfNeeded(pawn);
                    continue;
                }

                ApplyMercenaryBuild(pawn, build);
                return new MercenaryOffer(pawn, MercenaryOffer.CalculatePrice(pawn, build), build);
            }

            Log.Warning("RimMercenaries: Failed to generate valid mercenary after 10 attempts");
            return null;
        }

        public static List<MercenaryOffer> GenerateOffersForConsole(Map map, int[] quotas, XenotypeDef forcedXenotype = null)
        {
            var offers = new List<MercenaryOffer>();
            for (int tier = 0; tier < quotas.Length; tier++)
            {
                for (int i = 0; i < quotas[tier]; i++)
                {
                    var offer = GenerateOffer(map, tier + 1, forcedXenotype);
                    if (offer != null) offers.Add(offer);
                }
            }
            return offers;
        }

        private static void ApplyMercenaryBuild(Pawn pawn, MercenaryBuild build)
        {
            if (pawn.skills == null) return;

            var excludedSkills = new HashSet<SkillDef> { SkillDefOf.Shooting, SkillDefOf.Melee };
            if (build.MedicineLevel.HasValue) excludedSkills.Add(SkillDefOf.Medicine);

            // Check if this is a tier 3 mercenary
            bool isTier3 = build == MercenaryBuilds.Builds[3];
            Passion primarySkillPassion = isTier3 ? Passion.Major : Passion.Minor;

            foreach (var skill in pawn.skills.skills)
            {
                if (skill.def == SkillDefOf.Shooting)
                {
                    skill.Level = build.ShootingLevel;
                    skill.passion = primarySkillPassion;
                }
                else if (skill.def == SkillDefOf.Melee)
                {
                    skill.Level = build.MeleeLevel;
                    skill.passion = primarySkillPassion;
                }
                else if (skill.def == SkillDefOf.Medicine && build.MedicineLevel.HasValue)
                {
                    skill.Level = build.MedicineLevel.Value;
                    skill.passion = primarySkillPassion;
                }
                else if (!excludedSkills.Contains(skill.def))
                {
                    skill.Level = Rand.RangeInclusive(0, build.OtherSkillMax);
                    skill.passion = Passion.None;
                }
            }

            ApplyTierTraits(pawn, build);
        }

        private static void ApplyTierTraits(Pawn pawn, MercenaryBuild build)
        {
            pawn.story.traits.allTraits.Clear();

            if (build == MercenaryBuilds.Builds[1])
                EnsureTraits(pawn, BadTraits, Rand.RangeInclusive(1, 2), GoodTraits);
            else if (build == MercenaryBuilds.Builds[3])
                EnsureTraits(pawn, GoodTraits, Rand.RangeInclusive(1, 2), BadTraits);
        }

        private static void EnsureTraits(Pawn pawn, List<TraitDef> desiredTraits, int targetCount, List<TraitDef> forbiddenTraits)
        {
            var currentTraits = pawn.story.traits.allTraits
                .Where(t => desiredTraits.Contains(t.def))
                .ToList();

            while (currentTraits.Count > targetCount)
            {
                pawn.story.traits.RemoveTrait(currentTraits.Pop());
            }

            var availableTraits = TraitPool
                .Where(kvp => desiredTraits.Contains(kvp.Key) && 
                             !forbiddenTraits.Contains(kvp.Key) &&
                             !pawn.story.traits.allTraits.Any(t => t.def == kvp.Key || kvp.Key.ConflictsWith(t.def)))
                .SelectMany(kvp => kvp.Value.Select(degree => new Trait(kvp.Key, degree)))
                .ToList();

            availableTraits.Shuffle();

            while (currentTraits.Count < targetCount && availableTraits.Any())
            {
                var trait = availableTraits.Pop();
                if (!pawn.story.traits.allTraits.Any(t => trait.def.ConflictsWith(t.def)))
                    pawn.story.traits.GainTrait(trait);
            }
        }
    }
}