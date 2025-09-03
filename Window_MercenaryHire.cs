using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Reflection;
using System.CodeDom;

namespace RimMercenaries
{
    public class Window_MercenaryHire : Window
    {
        private readonly Building commsConsole;
        private readonly Pawn negotiator;
        private Vector2 scrollPosition = Vector2.zero;
        private List<MercenaryOffer> currentOffers;
        private Pawn hoveredPawnForSkills = null;
        private readonly Dictionary<MercenaryOffer, MercenaryLoadoutSelection> savedSelections = new Dictionary<MercenaryOffer, MercenaryLoadoutSelection>();
        private readonly Dictionary<MercenaryOffer, int> savedFinalPrices = new Dictionary<MercenaryOffer, int>();



        // --- Layout Constants ---
        private const float DropdownWidth = 150f;
        private const float RegenerateButtonWidth = 160f;
        private new const float Margin = 10f;
        private const float PortraitSize = 60f;
        private const float HireButtonWidth = 100f;
        private const float CustomizeButtonWidth = 110f;
        private const float ScrollBarWidth = 16f;

        private const float SkillRowHeight = 27f;
        private const float SkillTooltipWidth = 250f;
        private static bool skillUIPrimed = false;
        private static readonly FieldInfo fiLevelLabelWidth = typeof(SkillUI).GetField("levelLabelWidth", BindingFlags.NonPublic | BindingFlags.Static);

        public Window_MercenaryHire(Building building, Pawn negotiator)
        {
            this.commsConsole = building;
            this.negotiator = negotiator;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = true;
            this.doCloseX = true;
            this.doCloseButton = false;
            this.draggable = true;
            this.resizeable = true;
            this.windowRect = new Rect(0, 0, 1150f, 750f);
            RefreshOfferList();
            // Restore any session-saved selections from the game component
            var comp = Current.Game?.GetComponent<MercenaryGameComponent>();
            if (comp != null && currentOffers != null)
            {
                foreach (var offer in currentOffers)
                {
                    var sel = comp.GetSavedSelection(offer?.pawn?.thingIDNumber ?? 0);
                    if (sel != null)
                    {
                        savedSelections[offer] = sel;
                        savedFinalPrices[offer] = offer.price + sel.CalculateAdditionalCost();
                    }
                }
            }
        }

        private void RefreshOfferList()
        {
            this.currentOffers = new List<MercenaryOffer>(MercenaryManager.GetAvailableMercenaries(RimMercenaries.selectedXenotypeDef));
        }



        private int GetRemainingTierCount(int tier)
        {
            return MercenaryManager.GetRemainingTierCount(tier);
        }

        public override void PostOpen()
        {
            base.PostOpen();
            windowRect.center = new Vector2(UI.screenWidth / 2, UI.screenHeight / 2);
            RefreshOfferList();
        }

        public override Vector2 InitialSize => new Vector2(1150f, 750f);

        public override void DoWindowContents(Rect inRect)
        {
            Color stainedBg = ThemeProvider.GetWindowBackgroundColor();
            Widgets.DrawBoxSolid(inRect, stainedBg);

            if (Current.Game != null && Prefs.DevMode)
            {
                var worldSettingsButtonW = 150f;
                //var worldSettingsButtonText = "RimMercenaries_WorldSettings".Translate() + themes[selectedThemeIndex].translationKey.Translate();
                var worldSettingsButtonText = "RimMercenaries_WorldSettings".Translate();
                var worldSettingsButtonH = Text.CalcHeight(worldSettingsButtonText, worldSettingsButtonW) + 8f;
                Rect worldSettingsButtonRect = new Rect(inRect.x + inRect.xMax / 2 - worldSettingsButtonW/2, inRect.y, worldSettingsButtonW, worldSettingsButtonH);
                
                // Create a rect for the settings indicator text
                Rect settingsIndicatorRect = new Rect(worldSettingsButtonRect.xMax + 10f, inRect.y, 200f, 40f);
                
                // Check which settings are currently active
                var comp = Current.Game.GetComponent<MercenaryGameComponent>();
                // RimMercenariesMod.ActiveSettings returns comp?.WorldSettings ?? Settings
                // If WorldSettings is null, we're following globals
                // If WorldSettings is not null, we're using world-specific settings
                bool usingWorldSettings = comp?.WorldSettings != null;
                
                // Display the settings indicator text
                string settingsText = usingWorldSettings ? "RimMercenaries_UsingWorldSettings".Translate() : "RimMercenaries_UsingGlobalSettings".Translate();
                GUI.color = usingWorldSettings ? Color.yellow : Color.cyan;
                Widgets.Label(settingsIndicatorRect, settingsText);
                GUI.color = Color.white;
                
                // Draw the world settings button
                if (Widgets.ButtonText(worldSettingsButtonRect, "RimMercenaries_WorldSettings".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_WorldMercenarySettings());
                }
            }


            Rect contentRect = inRect;
            float layoutX = contentRect.x;
            float layoutWidth = contentRect.width;
            float currentY = contentRect.y;
            string titleStr = "RimMercenaries_HireMercenariesTitle".Translate();
            float titleHeight = Text.CalcHeight(titleStr, layoutWidth);
            Widgets.Label(new Rect(layoutX, currentY, layoutWidth, titleHeight), titleStr);
            currentY += titleHeight + Margin / 2f;
            string negotiatorStr = "RimMercenaries_Negotiator".Translate(
                negotiator.LabelShortCap, negotiator.GetStatValue(StatDefOf.SocialImpact).ToString("0.##"), negotiator.skills.GetSkill(SkillDefOf.Social).Level);
            float negotiatorHeight = Text.CalcHeight(negotiatorStr, layoutWidth);
            Widgets.Label(new Rect(layoutX, currentY, layoutWidth, negotiatorHeight), negotiatorStr);
            currentY += negotiatorHeight + Margin;

            float controlsHeight = CalculateControlsHeight();
            Rect controlsRect = new Rect(layoutX, currentY, layoutWidth, controlsHeight);
            float currentX = controlsRect.x;

            DrawControls(controlsRect);
            currentY += controlsHeight + Margin;

            float scrollStartY = controlsRect.yMax + Margin;
            float scrollHeight = (contentRect.y + contentRect.height) - scrollStartY;
            Rect outRect = new Rect(contentRect.x, scrollStartY, contentRect.width, scrollHeight);

            List<MercenaryOffer> tier1Offers = currentOffers
                        .Where(o => o.buildType == MercenaryBuilds.Builds[1])
                        .ToList();
            List<MercenaryOffer> tier2Offers = currentOffers
                        .Where(o => o.buildType == MercenaryBuilds.Builds[2])
                        .ToList();
            List<MercenaryOffer> tier3Offers = currentOffers
                        .Where(o => o.buildType == MercenaryBuilds.Builds[3])
                        .ToList();

            float availableWidth = outRect.width - ScrollBarWidth - Margin * 2;
            float colWidth = availableWidth / 3f;

            string build1Title = "<b>" + "RimMercenaries_Build1Title".Translate() + "</b>\n" + "RimMercenaries_Build1Desc".Translate();
            string build2Title = "<b>" + "RimMercenaries_Build2Title".Translate() + "</b>\n" + "RimMercenaries_Build2Desc".Translate();
            string build3Title = "<b>" + "RimMercenaries_Build3Title".Translate() + "</b>\n" + "RimMercenaries_Build3Desc".Translate();
            float colHeaderHeight1 = Text.CalcHeight(build1Title, colWidth);
            float colHeaderHeight2 = Text.CalcHeight(build2Title, colWidth);
            float colHeaderHeight3 = Text.CalcHeight(build3Title, colWidth);
            float columnHeaderHeight = Mathf.Max(colHeaderHeight1, colHeaderHeight2, colHeaderHeight3);

            List<float> tier1Heights = tier1Offers.Select(o => CalculateOfferCellHeight(o, colWidth)).ToList();
            List<float> tier2Heights = tier2Offers.Select(o => CalculateOfferCellHeight(o, colWidth)).ToList();
            List<float> tier3Heights = tier3Offers.Select(o => CalculateOfferCellHeight(o, colWidth)).ToList();

            float col1Height = columnHeaderHeight + Margin + tier1Heights.Sum() + Mathf.Max(0, tier1Heights.Count - 1) * Margin;
            float col2Height = columnHeaderHeight + Margin + tier2Heights.Sum() + Mathf.Max(0, tier2Heights.Count - 1) * Margin;
            float col3Height = columnHeaderHeight + Margin + tier3Heights.Sum() + Mathf.Max(0, tier3Heights.Count - 1) * Margin;
            float maxContentHeight = Mathf.Max(outRect.height, col1Height, col2Height, col3Height);

            Rect viewRect = new Rect(0, 0, outRect.width - ScrollBarWidth, maxContentHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            GUI.BeginGroup(viewRect);

            Rect col1Rect = new Rect(0, 0, colWidth, maxContentHeight);
            Rect col2Rect = new Rect(colWidth + Margin, 0, colWidth, maxContentHeight);
            Rect col3Rect = new Rect(2 * (colWidth + Margin), 0, colWidth, maxContentHeight);

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(col1Rect.x, col1Rect.y, col1Rect.width, columnHeaderHeight), build1Title);
            Widgets.Label(new Rect(col2Rect.x, col2Rect.y, col2Rect.width, columnHeaderHeight), build2Title);
            Widgets.Label(new Rect(col3Rect.x, col3Rect.y, col3Rect.width, columnHeaderHeight), build3Title);
            Text.Anchor = TextAnchor.UpperLeft;

            List<MercenaryOffer> offersToRemove = new List<MercenaryOffer>();

            DrawOfferColumn(col1Rect, tier1Offers, tier1Heights, negotiator, offersToRemove, colWidth, columnHeaderHeight);
            DrawOfferColumn(col2Rect, tier2Offers, tier2Heights, negotiator, offersToRemove, colWidth, columnHeaderHeight);
            DrawOfferColumn(col3Rect, tier3Offers, tier3Heights, negotiator, offersToRemove, colWidth, columnHeaderHeight);

            if (offersToRemove.Any())
            {
                foreach (var rem in offersToRemove)
                {
                    currentOffers.Remove(rem);
                }
            }

            GUI.EndGroup();
            Widgets.EndScrollView();

            if (hoveredPawnForSkills != null)
            {
                DrawSkillTooltipAtMouse(hoveredPawnForSkills);
                hoveredPawnForSkills = null;
            }

            // Draw non-combatant warning overlay in top-right corner
            if (ModsConfig.BiotechActive && RimMercenaries.selectedXenotypeDef != null && 
                MercenaryOfferGenerator.IsNonCombatantXenotype(RimMercenaries.selectedXenotypeDef))
            {
                var warningText = "RimMercenaries_NonCombatantWarning".Translate();
                var warningWidth = 400f; // Fixed width for the warning
                var warningHeight = Text.CalcHeight(warningText, warningWidth);
                
                // Position in top-right corner with some margin from edges
                var warningX = inRect.xMax - warningWidth - 20f;
                var warningY = inRect.y + 20f;
                var warningRect = new Rect(warningX, warningY, warningWidth, warningHeight);
                
                // Draw semi-transparent background
                var bgColor = new Color(0f, 0f, 0f, 0.7f);
                Widgets.DrawBoxSolid(warningRect, bgColor);
                
                // Draw red border
                var borderColor = GUI.color;
                GUI.color = Color.red;
                Widgets.DrawBox(warningRect);
                GUI.color = borderColor;
                
                // Save current color and font
                var oldColor = GUI.color;
                var oldFont = Text.Font;
                
                // Set warning style - red letters
                GUI.color = Color.red;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                
                Widgets.Label(warningRect, warningText);
                
                // Restore original settings
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private float CalculateControlsHeight()
        {
            var refreshText = "RimMercenaries_RefreshReady".Translate();
            var refreshHeight = Text.CalcHeight(refreshText, 200f);
            var tierCountText = $"Tier 1: 10/10   Tier 2: 5/5   Tier 3: 2/2";
            var tierHeight = Text.CalcHeight(tierCountText, 400f);
            var themeButtonHeight = Text.CalcHeight("RimMercenaries_Theme".Translate() + ThemeProvider.CurrentThemeTranslationKey.Translate(), 150f) + 8f;
            var xenotypeLabelHeight = ModsConfig.BiotechActive ? Text.CalcHeight("RimMercenaries_FilterXenotype".Translate(), 120f) : 0f;
            var dropdownButtonHeight = ModsConfig.BiotechActive ? Text.CalcHeight("RimMercenaries_XenotypeAny".Translate(), DropdownWidth) + 8f : 0f;

            return Mathf.Max(refreshHeight, tierHeight, themeButtonHeight, xenotypeLabelHeight, dropdownButtonHeight, 32f);
        }

        private void DrawControls(Rect controlsRect)
        {
            var currentX = controlsRect.x;

            var nextRefresh = MercenaryManager.LastRefreshTick + MercenaryManager.RefreshIntervalTicks - Find.TickManager.TicksGame;
            var refreshText = nextRefresh <= 0
                ? "RimMercenaries_RefreshReady".Translate()
                : "RimMercenaries_NextRefresh".Translate(nextRefresh.ToStringTicksToPeriod());

            var refreshW = 200f;
            var refreshH = Text.CalcHeight(refreshText, refreshW);
            var refreshInfoRect = new Rect(currentX, controlsRect.y + (controlsRect.height - refreshH) / 2, refreshW, refreshH);
            Widgets.Label(refreshInfoRect, refreshText);
            currentX += refreshInfoRect.width + Margin;

            var tierCounts = MercenaryManager.GetAllRemainingTierCounts();

            var settings = RimMercenariesMod.ActiveSettings;

            var tier1Text = "RimMercenaries_TierLabel".Translate(1, tierCounts[1], settings.tier1Count);
            var tier2Text = "RimMercenaries_TierLabel".Translate(2, tierCounts[2], settings.tier2Count);
            var tier3Text = "RimMercenaries_TierLabel".Translate(3, tierCounts[3], settings.tier3Count);
            var tierCountText = $"{tier1Text}   {tier2Text}   {tier3Text}";
            var tierW = 400f;
            var tierH = Text.CalcHeight(tierCountText, tierW);
            var tierCountRect = new Rect(currentX, controlsRect.y + (controlsRect.height - tierH) / 2, tierW, tierH);
            Widgets.Label(tierCountRect, tierCountText);
            currentX += tierCountRect.width + Margin;

            var themeButtonW = 150f;
            var themeButtonText = "RimMercenaries_Theme".Translate() + ThemeProvider.CurrentThemeTranslationKey.Translate();
            var themeButtonH = Text.CalcHeight(themeButtonText, themeButtonW) + 8f;
            var themeButtonRect = new Rect(currentX, controlsRect.y + (controlsRect.height - themeButtonH) / 2, themeButtonW, themeButtonH);
            if (Widgets.ButtonText(themeButtonRect, themeButtonText))
            {
                ThemeProvider.CycleTheme();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            currentX += themeButtonRect.width + Margin;

            if (ModsConfig.BiotechActive)
            {
                var xenotypeLabel = "RimMercenaries_FilterXenotype".Translate();
                var xenotypeLabelW = Text.CalcSize(xenotypeLabel).x;
                var xenotypeLabelH = Text.CalcHeight(xenotypeLabel, xenotypeLabelW);
                var xenotypeLabelRect = new Rect(currentX, controlsRect.y + (controlsRect.height - xenotypeLabelH) / 2, xenotypeLabelW, xenotypeLabelH);
                Widgets.Label(xenotypeLabelRect, xenotypeLabel);
                currentX += xenotypeLabelRect.width + Margin / 2;

                var defaultXenotype = DefDatabase<XenotypeDef>.AllDefsListForReading.FirstOrDefault(x => x.defName == "Baseliner");
                var buttonLabel = RimMercenaries.selectedXenotypeDef?.LabelCap ?? 
                                defaultXenotype?.LabelCap ?? 
                                "RimMercenaries_XenotypeAny".Translate();
                var dropdownButtonH = Text.CalcHeight(buttonLabel, DropdownWidth) + 8f;
                var dropdownButtonRect = new Rect(currentX, controlsRect.y + (controlsRect.height - dropdownButtonH) / 2, DropdownWidth, dropdownButtonH);

                if (Widgets.ButtonText(dropdownButtonRect, buttonLabel))
                {
                    var options = DefDatabase<XenotypeDef>.AllDefs
                        .Where(x => x.canGenerateAsCombatant || MercenaryOfferGenerator.IsNonCombatantXenotype(x))
                        .OrderBy(x => x.LabelCap.ToString())
                        .Select(xenotype => new FloatMenuOption(
                            xenotype.LabelCap,
                            () =>
                            {
                                if (RimMercenaries.selectedXenotypeDef != xenotype)
                                {
                                    RimMercenaries.SetSelectedXenotype(xenotype);
                                    RefreshOfferList();
                                }
                            },
                            xenotype.Icon,
                            Color.white,
                            MenuOptionPriority.Default,
                            null,
                            null,
                            0f,
                            null,
                            null,
                            true,
                            0))
                        .ToList();

                    Find.WindowStack.Add(new FloatMenu(options));
                }
                currentX += dropdownButtonRect.width + Margin;
            }
            

        }

        private void DrawOfferColumn(Rect columnRect, List<MercenaryOffer> offers, List<float> cellHeights, Pawn negotiator, List<MercenaryOffer> toRemove, float colWidth, float columnHeaderHeight)
        {
            float yPos = columnHeaderHeight + Margin;
            for (int i = 0; i < offers.Count; i++)
            {
                if (i >= cellHeights.Count)
                {
                    Log.Warning("RimMercenaries: Mismatch between offer count and cell height count.");
                    continue;
                }
                float cellHeight = cellHeights[i];
                Rect cellRect = new Rect(columnRect.x, yPos, columnRect.width, cellHeight);
                DrawOfferCell(cellRect, offers[i], negotiator, toRemove, colWidth);
                yPos += cellHeight + Margin;
            }
        }

        private float CalculateOfferCellHeight(MercenaryOffer offer, float availableWidth)
        {
            if (offer?.pawn == null) return 0f;
            float internalMargin = Margin / 2f;
            // Account for both buttons stacked horizontally plus margins
            float infoWidth = availableWidth - PortraitSize - internalMargin * 3 - Mathf.Max(HireButtonWidth, CustomizeButtonWidth);
            float currentY = internalMargin;
            Text.Font = GameFont.Medium;
            currentY += Text.CalcHeight(offer.pawn.LabelShortCap, infoWidth);
            Text.Font = GameFont.Small;
            if (ModsConfig.BiotechActive && offer.pawn.genes != null)
            {
                currentY += 2f;
                string xenotypeLabel = offer.pawn.genes?.XenotypeLabelCap ?? offer.pawn.genes?.Xenotype?.LabelCap ?? "RimMercenaries_XenotypeUnknown".Translate();
                currentY += Text.CalcHeight(xenotypeLabel, infoWidth);
            }
            currentY += 2f;
            var shooting = offer.pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            var melee = offer.pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            var shootingPenalty = shooting - offer.buildType.ShootingLevel;
            var meleePenalty = melee - offer.buildType.MeleeLevel;
            string shootingPenaltyString = shootingPenalty != 0 ? $"(<color={(shootingPenalty > 0 ? "green" : "red")}>{(shootingPenalty > 0 ? "+" : "-")}{Math.Abs(shootingPenalty)}</color>)" : "";
            string meleePenaltyString = meleePenalty != 0 ? $"(<color={(meleePenalty > 0 ? "green" : "red")}>{(meleePenalty > 0 ? "+" : "-")}{Math.Abs(meleePenalty)}</color>)" : "";
            TaggedString skillsStr = "RimMercenaries_SkillsShort".Translate()
                                        .Replace("[0]", shooting.ToString())
                                        .Replace("[1]", shootingPenaltyString)
                                        .Replace("[2]", melee.ToString())
                                        .Replace("[3]", meleePenaltyString);
            currentY += Text.CalcHeight(skillsStr, infoWidth);
            currentY += 2f;
            int displayPrice = offer.price;
            if (savedSelections.TryGetValue(offer, out var selTmp))
            {
                displayPrice = offer.price + (selTmp?.CalculateAdditionalCost() ?? 0);
            }
            string priceStr = "RimMercenaries_Price".Translate(displayPrice.ToString("N0"));
            currentY += Text.CalcHeight(priceStr, infoWidth);
            currentY += internalMargin;
            float portraitAreaHeight = PortraitSize + internalMargin * 2;
            // Account for two buttons stacked vertically (hire button + customize button + spacing)
            float buttonAreaHeight = (30f * 2) + 4f + internalMargin * 2;
            return Mathf.Max(portraitAreaHeight, currentY, buttonAreaHeight);
        }

        private void DrawOfferCell(Rect cellRect, MercenaryOffer offer, Pawn negotiator, List<MercenaryOffer> toRemove, float colWidth)
        {
            if (offer?.pawn == null) return;

            Color stainedCellColor = ThemeProvider.GetSectionBackgroundColor();
            Widgets.DrawBoxSolid(cellRect, stainedCellColor);
            Widgets.DrawBox(cellRect);

            float internalMargin = Margin / 2f;

            // Account for both buttons stacked horizontally plus margins
            float infoWidth = cellRect.width - PortraitSize - internalMargin * 3 - Mathf.Max(HireButtonWidth, CustomizeButtonWidth);
            float textBlockHeight = 0f;
            Text.Font = GameFont.Medium;
            float nameHeight = Text.CalcHeight(offer.pawn.LabelShortCap, infoWidth);
            textBlockHeight += nameHeight;
            Text.Font = GameFont.Small;
            float xenotypeHeight = 0f;
            if (ModsConfig.BiotechActive && offer.pawn.genes != null)
            {
                xenotypeHeight += 2f;
                string xenotypeLabel = offer.pawn.genes?.XenotypeLabelCap ?? offer.pawn.genes?.Xenotype?.LabelCap ?? "RimMercenaries_XenotypeUnknown".Translate();
                xenotypeHeight += Text.CalcHeight(xenotypeLabel, infoWidth);
            }
            textBlockHeight += xenotypeHeight;
            textBlockHeight += 2f;
            var shooting = offer.pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            var melee = offer.pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            var shootingPenalty = shooting - offer.buildType.ShootingLevel;
            var meleePenalty = melee - offer.buildType.MeleeLevel;
            string shootingPenaltyString = shootingPenalty != 0 ? $"(<color={(shootingPenalty > 0 ? "green" : "red")}>{(shootingPenalty > 0 ? "+" : "-")}{Math.Abs(shootingPenalty)}</color>)" : "";
            string meleePenaltyString = meleePenalty != 0 ? $"(<color={(meleePenalty > 0 ? "green" : "red")}>{(meleePenalty > 0 ? "+" : "-")}{Math.Abs(meleePenalty)}</color>)" : "";
            TaggedString skillsStr = "RimMercenaries_SkillsShort".Translate()
                                                .Replace("[0]", shooting.ToString())
                                                .Replace("[1]", shootingPenaltyString)
                                                .Replace("[2]", melee.ToString())
                                                .Replace("[3]", meleePenaltyString);
            float skillsHeight = Text.CalcHeight(skillsStr, infoWidth);
            textBlockHeight += skillsHeight;
            textBlockHeight += 2f;
            int displayPrice = offer.price;
            if (savedSelections.TryGetValue(offer, out var selTmp))
            {
                displayPrice = offer.price + (selTmp?.CalculateAdditionalCost() ?? 0);
            }
            string priceStr = "RimMercenaries_Price".Translate(displayPrice.ToString("N0"));
            float priceHeight = Text.CalcHeight(priceStr, infoWidth);
            textBlockHeight += priceHeight;

            float portraitHeight = PortraitSize;
            float contentHeight = Mathf.Max(textBlockHeight, portraitHeight);
            float contentYOffset = (cellRect.height - contentHeight) / 2f;

            Rect portraitRect = new Rect(cellRect.x + internalMargin, cellRect.y + contentYOffset + (contentHeight - portraitHeight) / 2f, PortraitSize, PortraitSize);
            Widgets.ThingIcon(portraitRect, offer.pawn);

            Rect infoIconRect = new Rect(portraitRect.xMax - 16f, portraitRect.y + 2f, 16f, 16f);
            Widgets.DrawTextureFitted(infoIconRect, TexButton.Info, 1f);
            if (Mouse.IsOver(infoIconRect)) hoveredPawnForSkills = offer.pawn;

            float infoX = portraitRect.xMax + internalMargin;
            float infoY = cellRect.y + contentYOffset;
            float infoRectWidth = infoWidth;
            float currentY = infoY;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(infoX, currentY, infoRectWidth, nameHeight), offer.pawn.LabelShortCap);
            currentY += nameHeight;
            Text.Font = GameFont.Small;
            if (ModsConfig.BiotechActive && offer.pawn.genes != null)
            {
                currentY += 2f;
                string xenotypeLabel = offer.pawn.genes?.XenotypeLabelCap ?? offer.pawn.genes?.Xenotype?.LabelCap ?? "RimMercenaries_XenotypeUnknown".Translate();
                float xenotypeLabelHeight = Text.CalcHeight(xenotypeLabel, infoRectWidth);
                GUI.color = Color.gray;
                Widgets.Label(new Rect(infoX, currentY, infoRectWidth, xenotypeLabelHeight), xenotypeLabel);
                GUI.color = Color.white;
                currentY += xenotypeLabelHeight;
            }
            currentY += 2f;
            Widgets.Label(new Rect(infoX, currentY, infoRectWidth, skillsHeight), skillsStr);
            currentY += skillsHeight;
            currentY += 2f;
            Widgets.Label(new Rect(infoX, currentY, infoRectWidth, priceHeight), priceStr);
            currentY += priceHeight;

            float buttonHeight = 30f;
            float buttonY = cellRect.y + contentYOffset + (contentHeight - buttonHeight) / 2f;
            int tier = 1;
            if (offer.buildType != null)
            {
                foreach (var kvp in MercenaryBuilds.Builds)
                {
                    if (kvp.Value == offer.buildType)
                    {
                        tier = kvp.Key;
                        break;
                    }
                }
            }
            bool canHire = GetRemainingTierCount(tier) > 0;

            Rect hireButtonRect;
            // Only show customize button if loadout customization is enabled
            if (RimMercenariesMod.ActiveSettings.enableDevLoadoutCustomization)
            {
                // Place customize button below hire button with proper alignment
                Rect customizeButtonRect = new Rect(cellRect.xMax - CustomizeButtonWidth - internalMargin, buttonY + buttonHeight + 4f, CustomizeButtonWidth, buttonHeight);
                hireButtonRect = new Rect(customizeButtonRect.xMax - HireButtonWidth - internalMargin, buttonY, HireButtonWidth, buttonHeight);

                // Customize button - only shown when enabled
                if (Widgets.ButtonText(customizeButtonRect, "RimMercenaries_Loadout_Customize".Translate()))
                {
                    savedSelections.TryGetValue(offer, out var existingSel);
                    var dlg = new Dialog_MercenaryLoadout(commsConsole.Map, commsConsole.InteractionCell, negotiator, offer, commsConsole, existingSel);
                    dlg.onConfirm = (sel, finalPrice) =>
                    {
                        if (sel != null)
                        {
                            savedSelections[offer] = sel.Clone();
                            savedFinalPrices[offer] = finalPrice;
                            // Persist selection in session via game component (survives reopen)
                            var comp = Current.Game?.GetComponent<MercenaryGameComponent>();
                            comp?.SetSavedSelection(offer.pawn.thingIDNumber, sel);
                        }
                    };
                    Find.WindowStack.Add(dlg);
                }
            }
            else
            {
                // No customize button, hire button takes full width
                hireButtonRect = new Rect(cellRect.xMax - HireButtonWidth - internalMargin, buttonY, HireButtonWidth, buttonHeight);
            }
            if (!canHire) GUI.color = Color.gray;
            if (Widgets.ButtonText(hireButtonRect, "RimMercenaries_HireButton".Translate()))
            {
                if (!canHire)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                else
                {
                    // Use saved selection if available; otherwise base hire
                    MercenaryLoadoutSelection selToHire = null;
                    int finalPrice = offer.price;
                    if (savedSelections.TryGetValue(offer, out var savedSel))
                    {
                        selToHire = savedSel;
                        finalPrice = offer.price + savedSel.CalculateAdditionalCost();
                    }

                    if (MercenaryManager.TryHireMercenary(commsConsole.Map, commsConsole.InteractionCell, negotiator, offer, commsConsole, selToHire, finalPrice))
                    {
                        toRemove.Add(offer);
                        savedSelections.Remove(offer);
                        savedFinalPrices.Remove(offer);
                        // Clear persisted selection once hired
                        var comp = Current.Game?.GetComponent<MercenaryGameComponent>();
                        comp?.RemoveSavedSelection(offer.pawn.thingIDNumber);
                    }
                    else
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    }
                }
            }
            GUI.color = Color.white;
        }

        private bool TryHireMercenary(Pawn negotiator, MercenaryOffer offer)
        {
            return MercenaryManager.TryHireMercenary(commsConsole.Map, commsConsole.InteractionCell, negotiator, offer, commsConsole);
        }

        private void DrawSkillTooltipAtMouse(Pawn pawn)
        {
            EnsureSkillUICache();
            Vector2 pos = Event.current.mousePosition
              + this.windowRect.position
              + new Vector2(32f, 48f);

            int rows = pawn.skills?.skills?.Count ?? 0;
            float height = rows * SkillRowHeight + 8f;

            if (pos.x + SkillTooltipWidth > UI.screenWidth)
                pos.x = UI.screenWidth - SkillTooltipWidth - 4f;
            if (pos.y + height > UI.screenHeight)
                pos.y = UI.screenHeight - height - 4f;

            Rect winRect = new Rect(pos.x, pos.y, SkillTooltipWidth, height);
            int winID = 0xC0DE ^ pawn.thingIDNumber;

            Find.WindowStack.ImmediateWindow(
                winID, winRect, WindowLayer.Super,
                delegate
                {
                    Widgets.DrawWindowBackground(new Rect(0f, 0f, winRect.width, winRect.height));

                    float curY = 4f;
                    foreach (SkillRecord rec in pawn.skills.skills.OrderByDescending(s => s.def.listOrder))
                    {
                        Rect rowRect = new Rect(4f, curY, winRect.width - 8f, SkillUI.SkillHeight);
                        SkillUI.DrawSkill(rec, rowRect, SkillUI.SkillDrawMode.Menu);
                        curY += SkillRowHeight;
                    }
                },
                doBackground: false, absorbInputAroundWindow: false);
        }

        private static void EnsureSkillUICache()
        {
            if (skillUIPrimed) return;

            SkillUI.Reset();
            float widest = 0f;
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            foreach (SkillDef def in DefDatabase<SkillDef>.AllDefsListForReading)
            {
                float w = Text.CalcSize(def.skillLabel.CapitalizeFirst()).x;
                if (w > widest) widest = w;
            }
            Text.Font = oldFont;

            fiLevelLabelWidth.SetValue(null, widest);

            skillUIPrimed = true;
        }
    }
}