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
                Pawn pawn = null;
                try
                {
                    // For xenotypes that are inherently non-combatant (like Highmates), allow violence-disabled pawns
                    // For other xenotypes, require violence capability to avoid spawning defective mercenaries
                    bool allowNonCombatant = forcedXenotype != null && IsNonCombatantXenotype(forcedXenotype);
                    
                    var request = new PawnGenerationRequest(
                        kind: PawnKindDefOf.Colonist,
                        faction: null,
                        context: PawnGenerationContext.NonPlayer,
                        tile: map?.Tile ?? -1,
                        forceGenerateNewPawn: true,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: true,
                        mustBeCapableOfViolence: !allowNonCombatant,
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

                    pawn = PawnGenerator.GeneratePawn(request);
                    if (pawn == null) continue;

                    // Ensure the pawn's skills are properly initialized before checking xenotype
                    if (pawn.skills?.skills == null)
                    {
                        Log.Warning($"[RimMercenaries] Generated pawn {pawn.LabelShortCap} has null skills, discarding and retrying");
                        MercenaryOffer.DiscardPawnIfNeeded(pawn);
                        continue;
                    }

                    if (forcedXenotype != null && (pawn.genes == null || pawn.genes.Xenotype != forcedXenotype))
                    {
                        MercenaryOffer.DiscardPawnIfNeeded(pawn);
                        continue;
                    }

                    ApplyMercenaryBuild(pawn, build);
                    return new MercenaryOffer(pawn, MercenaryOffer.CalculatePrice(pawn, build), build);
                }
                catch (System.Exception ex)
                {
                    // Log the error but don't let it break the generation process
                    Log.Warning($"[RimMercenaries] Exception during pawn generation attempt {attempt + 1}/10 for xenotype {forcedXenotype?.defName ?? "none"}: {ex.Message}");
                    
                    // Clean up the pawn if it was created but had issues
                    if (pawn != null)
                    {
                        MercenaryOffer.DiscardPawnIfNeeded(pawn);
                    }
                    
                    // Continue to next attempt
                    continue;
                }
            }

            Log.Warning($"[RimMercenaries] Failed to generate valid mercenary after 10 attempts for xenotype {forcedXenotype?.defName ?? "none"}");
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
            if (pawn.skills?.skills == null) 
            {
                Log.Warning($"[RimMercenaries] Cannot apply mercenary build to {pawn.LabelShortCap} - skills are null");
                return;
            }

            var excludedSkills = new HashSet<SkillDef> { SkillDefOf.Shooting, SkillDefOf.Melee };
            if (build.MedicineLevel.HasValue) excludedSkills.Add(SkillDefOf.Medicine);

            // Check if this is a tier 3 mercenary
            bool isTier3 = build == MercenaryBuilds.Builds[3];
            Passion primarySkillPassion = isTier3 ? Passion.Major : Passion.Minor;

            try
            {
                foreach (var skill in pawn.skills.skills)
                {
                    if (skill?.def == null) continue; // Skip null skills
                    
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
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMercenaries] Exception while applying mercenary build to {pawn.LabelShortCap}: {ex.Message}");
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

        /// <summary>
        /// Checks if a xenotype is intentionally designed as non-combatant by looking for the ViolenceDisabled gene.
        /// This automatically identifies xenotypes like Highmates that are meant to be non-combatant.
        /// </summary>
        /// <param name="xenotype">The xenotype to check</param>
        /// <returns>True if the xenotype has the ViolenceDisabled gene, false otherwise</returns>
        public static bool IsNonCombatantXenotype(XenotypeDef xenotype)
        {
            // Check if the xenotype has the "Violence disabled" gene
            // This automatically identifies xenotypes that are intentionally designed as non-combatants
            if (xenotype?.genes != null)
            {
                return xenotype.genes.Any(geneDef => geneDef.defName == "ViolenceDisabled");
            }
            
            return false;
        }

        private static void EnsureTraits(Pawn pawn, List<TraitDef> desiredTraits, int targetCount, List<TraitDef> forbiddenTraits)
        {
            // Clear all existing traits first
            pawn.story.traits.allTraits.Clear();

            // Get available traits from the pool
            var availableTraits = TraitPool
                .Where(kvp => desiredTraits.Contains(kvp.Key) && 
                             !forbiddenTraits.Contains(kvp.Key))
                .SelectMany(kvp => kvp.Value.Select(degree => new Trait(kvp.Key, degree)))
                .ToList();

            availableTraits.Shuffle();

            // Add traits up to the target count
            int traitsAdded = 0;
            while (traitsAdded < targetCount && availableTraits.Any())
            {
                var trait = availableTraits.Pop();
                // Check for conflicts with already added traits
                if (!pawn.story.traits.allTraits.Any(t => trait.def.ConflictsWith(t.def) || t.def == trait.def))
                {
                    pawn.story.traits.GainTrait(trait);
                    traitsAdded++;
                }
            }
        }
    }
}