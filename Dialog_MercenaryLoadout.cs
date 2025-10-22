using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using System.Reflection;

namespace RimMercenaries
{


    public class Dialog_MercenaryLoadout : Window
    {
        private readonly Map map;
        private readonly IntVec3 dropCell;
        private readonly Pawn negotiator;
        private readonly MercenaryOffer offer;
        private readonly Building sourceBuilding;

        private MercenaryLoadoutSelection selection = new MercenaryLoadoutSelection();
        private readonly MercenaryLoadoutSelection initialSelection;
        private Vector2 weaponsScroll = Vector2.zero;
        private Vector2 apparelScroll = Vector2.zero;
        private Vector2 selectedApparelScroll = Vector2.zero;
        private string searchFilter = string.Empty;
        private int selectionHash = -1;
        private bool previewInitialized = false;
        private List<Apparel> originalApparel = new List<Apparel>();
        private ThingWithComps originalPrimaryWeapon = null;
        private List<Thing> previewCreatedThings = new List<Thing>();

        // Apparel customization
        private ThingDef expandedApparelDef = null;
        private ApparelCustomizationData currentCustomization = null;
        private Vector2 customizationScroll = Vector2.zero;
        private bool showCustomization = false;

        // Track if changes were confirmed
        private bool changesConfirmed = false;

        private List<ThingDef> availableWeaponDefs;
        private List<ThingDef> availableApparelDefs;

        // Performance caching
        private List<ThingDef> cachedFilteredWeapons = null;
        private List<ThingDef> cachedEligibleApparel = null;
        private string lastSearchFilter = null;
        private bool lastFilterRanged = true;
        private bool lastFilterMelee = true;
        private int lastSelectionHash = -2;
        private int apparelEligibilityFrameCache = -1;
        private Dictionary<ThingDef, bool> apparelEligibilityCache = new Dictionary<ThingDef, bool>();
        private Dictionary<ThingDef, int> itemCostCache = new Dictionary<ThingDef, int>();

        public Action<MercenaryLoadoutSelection, int> onConfirm;



        public override Vector2 InitialSize => new Vector2(1020f, 720f);

        public Dialog_MercenaryLoadout(Map map, IntVec3 dropCell, Pawn negotiator, MercenaryOffer offer, Building sourceBuilding, MercenaryLoadoutSelection prefilledSelection = null)
        {
            this.map = map;
            this.dropCell = dropCell;
            this.negotiator = negotiator;
            this.offer = offer;
            this.sourceBuilding = sourceBuilding;
            this.initialSelection = prefilledSelection?.Clone();

            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;

            BuildAvailableDefs();
            if (this.initialSelection != null)
            {
                // Seed the selection with a deep clone so dialog edits don't mutate external state until confirmed
                this.selection = this.initialSelection.Clone();
                selectionHash = -1;
                SanitizeSelection();
            }
        }

        private void BuildAvailableDefs()
        {
            // Weapons: IsWeapon, tech/research allowed
            IEnumerable<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;

            availableWeaponDefs = allDefs
                .Where(d => d.IsWeapon && d.equipmentType != EquipmentType.None)
                .Where(IsResearchedOrNoPrereq)
                .Where(CanPawnUseWeapon)
                .OrderBy(d => d.label)
                .ToList();

            // Apparel: has apparel component and wearable by pawn
            availableApparelDefs = allDefs
                .Where(d => d.apparel != null)
                .Where(IsResearchedOrNoPrereq)
                // Do not consider currently worn apparel when building availability; check only if pawn can ever wear it
                .Where(d => CanPawnWear(d))
                .OrderBy(d => d.apparel.bodyPartGroups?.Count ?? 0)
                .ThenBy(d => d.label)
                .ToList();
        }

        private bool CanPawnUseWeapon(ThingDef weaponDef)
        {
            if (offer?.pawn == null || weaponDef == null) return false;

            // Universal exclusion criteria for non-pawn weapons
            if (IsNonPawnWeapon(weaponDef))
                return false;

            try
            {
                ThingDef stuff = GenStuff.DefaultStuffFor(weaponDef);
                var weapon = ThingMaker.MakeThing(weaponDef, stuff) as ThingWithComps;
                if (weapon == null) return false;
                bool can = EquipmentUtility.CanEquip(weapon, offer.pawn);
                weapon.Destroy();
                return can;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Universal check to determine if a weapon is not suitable for pawn use.
        /// This covers turret weapons, building components, and other non-portable items.
        /// </summary>
        private bool IsNonPawnWeapon(ThingDef weaponDef)
        {
            // 1. Turret weapons - identified by tags, properties, and behavior
            if (weaponDef.weaponTags != null)
            {
                if (weaponDef.weaponTags.Contains("TurretGun"))
                    return true;
            }

            // 2. Weapons that destroy themselves when dropped (turret behavior)
            if (weaponDef.destroyOnDrop)
                return true;

            // 3. Items not meant for trading/colonist use
            if (weaponDef.tradeability == Tradeability.None)
                return true;

            // 4. Items without hit points (typically building components)
            if (!weaponDef.useHitPoints)
                return true;

            // 5. Items that can't be hauled (not portable)
            if (!weaponDef.alwaysHaulable)
                return true;

            // 6. Items marked as non-selectable (building components)
            if (!weaponDef.selectable)
                return true;

            // 7. Items that can't generate default designator (not meant for player interaction)
            if (!weaponDef.canGenerateDefaultDesignator)
                return true;

            // 8. Check for building-specific properties
            if (weaponDef.building != null)
                return true;

            // 9. Check for extremely heavy items (impractical for pawns)
            if (weaponDef.statBases != null)
            {
                var massStat = weaponDef.statBases.Find(s => s.stat == StatDefOf.Mass);
                if (massStat != null && massStat.value > 50f) // Arbitrary heavy threshold
                    return true;
            }

            // 10. Check for special categories that indicate non-pawn use
            if (weaponDef.category == ThingCategory.Building || weaponDef.category == ThingCategory.Item)
            {
                // Additional check: if it's an item but has building-like properties
                if (weaponDef.category == ThingCategory.Item)
                {
                    if (weaponDef.drawerType == DrawerType.RealtimeOnly)
                        return true;
                }
            }

            return false;
        }

        private bool IsResearchedOrNoPrereq(ThingDef def)
        {
            if (def == null) return false;

            // 1) Direct research prerequisites on the ThingDef (if present)
            try
            {
                var prereqField = typeof(ThingDef).GetField("researchPrerequisites", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prereqField != null)
                {
                    var list = prereqField.GetValue(def) as List<ResearchProjectDef>;
                    if (list != null && list.Count > 0)
                    {
                        foreach (var proj in list)
                        {
                            if (proj != null && !proj.IsFinished)
                                return false;
                        }
                    }
                }
            }
            catch { /* ignore and fall back to recipes */ }

            // 2) Recipe-based gating: any recipe that produces this item must have finished research
            try
            {
                var recipes = DefDatabase<RecipeDef>.AllDefsListForReading
                    .Where(r => r.products != null && r.products.Any(p => p.thingDef == def))
                    .ToList();

                foreach (var recipe in recipes)
                {
                    if (recipe.researchPrerequisites != null)
                    {
                        foreach (var res in recipe.researchPrerequisites)
                        {
                            if (!res.IsFinished)
                                return false;
                        }
                    }
                }
            }
            catch { /* ignore */ }

            // If neither direct prerequisites nor recipes gate it, consider it available
            return true;
        }

        private bool CanPawnWear(ThingDef apparelDef)
        {
            if (offer?.pawn == null) return false;
            try
            {
                if (apparelDef == null || apparelDef.apparel == null)
                    return false;

                // Filter out items obviously not meant to be worn in normal play
                if (apparelDef.destroyOnDrop) return false;
                if (!apparelDef.useHitPoints) return false;
                if (apparelDef.tradeability == Tradeability.None) return false;

                // Exclude child/baby specific clothing heuristically
                if (IsChildOrBabyApparel(apparelDef)) return false;

                // Respect gender-locked apparel
                if (apparelDef.apparel.gender != Gender.None && offer.pawn.gender != Gender.None && offer.pawn.gender != apparelDef.apparel.gender)
                    return false;

                // Body part compatibility
                if (!ApparelUtility.HasPartsToWear(offer.pawn, apparelDef))
                    return false;

                // Do NOT check against currently worn apparel here. Availability should be based on whether the pawn can ever wear it.
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsChildOrBabyApparel(ThingDef apparelDef)
        {
            try
            {
                string label = (apparelDef.label ?? apparelDef.defName ?? string.Empty).ToLowerInvariant();
                if (label.Contains("child") || label.Contains("kid") || label.Contains("baby") || label.Contains("toddler") || label.Contains("infant"))
                    return true;

                var tags = apparelDef.apparel?.tags;
                if (tags != null)
                {
                    foreach (var t in tags)
                    {
                        if (t == null) continue;
                        string tt = t.ToLowerInvariant();
                        if (tt.Contains("child") || tt.Contains("kid") || tt.Contains("baby") || tt.Contains("toddler") || tt.Contains("infant"))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void SanitizeSelection()
        {
            // Remove invalid weapon
            if (selection.selectedWeaponDef != null)
            {
                if (!availableWeaponDefs.Contains(selection.selectedWeaponDef) || !CanPawnUseWeapon(selection.selectedWeaponDef) || !IsResearchedOrNoPrereq(selection.selectedWeaponDef))
                {
                    selection.selectedWeaponDef = null;
                }
            }

            // Remove invalid apparels and resolve conflicts
            if (selection.selectedApparelDefs == null || selection.selectedApparelDefs.Count == 0) return;

            var valid = new List<ThingDef>();
            var body = offer.pawn?.RaceProps?.body;
            foreach (var def in selection.selectedApparelDefs)
            {
                if (def == null) continue;
                if (!availableApparelDefs.Contains(def)) continue;
                if (!CanPawnWear(def)) continue;
                bool conflicts = false;
                foreach (var other in valid)
                {
                    if (!ApparelUtility.CanWearTogether(def, other, body))
                    {
                        conflicts = true; break;
                    }
                }
                if (!conflicts)
                {
                    valid.Add(def);
                }
            }
            // Update selection to sanitized list
            selection.selectedApparelDefs = valid;

            // Drop customizations for removed items
            var keys = selection.apparelCustomizations.Keys.ToList();
            foreach (var k in keys)
            {
                if (!valid.Contains(k))
                {
                    selection.apparelCustomizations.Remove(k);
                }
            }
        }

        private int ComputeSelectionHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (selection.selectedWeaponDef != null ? selection.selectedWeaponDef.shortHash : 0);
                if (selection.selectedApparelDefs != null)
                {
                    foreach (var ad in selection.selectedApparelDefs)
                        h = h * 31 + (ad != null ? ad.shortHash : 0);
                }
                return h;
            }
        }

        private void EnsurePreviewInitialized()
        {
            if (previewInitialized) return;
            previewInitialized = true;
            if (offer?.pawn == null) return;

            // Record the true original state (before any dialog manipulation)
            if (offer.pawn.apparel != null)
                originalApparel = offer.pawn.apparel.WornApparel.ToList();
            if (offer.pawn.equipment != null)
                originalPrimaryWeapon = offer.pawn.equipment.Primary;

            // Only strip equipment if we don't have a prefilled selection
            // If we have a prefilled selection, the pawn might already be wearing customized equipment
            if (initialSelection == null)
            {
                // Remove originals for clean preview baseline
                if (offer.pawn.apparel != null)
                {
                    foreach (var ap in originalApparel.ToList())
                        offer.pawn.apparel.Remove(ap);
                }
                if (offer.pawn.equipment != null && originalPrimaryWeapon != null)
                    offer.pawn.equipment.Remove(originalPrimaryWeapon);
            }

            PortraitsCache.SetDirty(offer.pawn);
        }

        private void ApplySelectionToPreviewPawnIfChanged()
        {
            EnsurePreviewInitialized();
            int current = ComputeSelectionHash();
            if (current == selectionHash) return;
            selectionHash = current;

            // Clear existing worn gear carefully:
            // - Destroy only items that were created by a previous preview application
            // - Keep original items alive so they can be restored on cancel
            var previousPreviewThings = previewCreatedThings.ToList();

            if (offer.pawn.equipment != null)
            {
                // Remove all equipment but only destroy those known to be preview-created
                foreach (var eq in offer.pawn.equipment.AllEquipmentListForReading.ToList())
                {
                    offer.pawn.equipment.Remove(eq);
                    if (previousPreviewThings.Contains(eq) && eq.def.destroyable)
                    {
                        eq.Destroy();
                    }
                }
            }
            if (offer.pawn.apparel != null)
            {
                foreach (var ap in offer.pawn.apparel.WornApparel.ToList())
                {
                    offer.pawn.apparel.Remove(ap);
                    if (previousPreviewThings.Contains(ap) && ap.def.destroyable)
                    {
                        ap.Destroy();
                    }
                }
            }

            // Reset tracking for the new preview items
            previewCreatedThings.Clear();

                // Apply weapon
            if (selection.selectedWeaponDef != null)
            {
                var stuff = GenStuff.DefaultStuffFor(selection.selectedWeaponDef);
                var weapon = ThingMaker.MakeThing(selection.selectedWeaponDef, stuff) as ThingWithComps;
                if (weapon != null)
                {
                    if (EquipmentUtility.CanEquip(weapon, offer.pawn))
                    {
                        if (offer.pawn.equipment == null)
                            offer.pawn.equipment = new Pawn_EquipmentTracker(offer.pawn);
                            // Apply preview weapon style if any
                            try
                            {
                                if (selection.selectedWeaponStyle != null)
                                {
                                    var styleable = weapon.GetCompByReflectedType("RimWorld.CompStyleable");
                                    if (styleable != null)
                                    {
                                        var setStyle = styleable.GetType().GetMethod("SetStyle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (setStyle != null)
                                        {
                                            var pars = setStyle.GetParameters();
                                            if (pars.Length >= 1)
                                            {
                                                var args = pars.Length == 2 ? new object[] { selection.selectedWeaponStyle, true } : new object[] { selection.selectedWeaponStyle };
                                                setStyle.Invoke(styleable, args);
                                            }
                                        }
                                        else
                                        {
                                            var setStyleDef = styleable.GetType().GetMethod("SetStyleDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                            if (setStyleDef != null)
                                            {
                                                setStyleDef.Invoke(styleable, new object[] { selection.selectedWeaponStyle });
                                            }
                                            else
                                            {
                                                var prop = styleable.GetType().GetProperty("StyleDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                                if (prop != null && prop.CanWrite) prop.SetValue(styleable, selection.selectedWeaponStyle);
                                                else
                                                {
                                                    var field = styleable.GetType().GetField("styleDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                                    if (field != null) field.SetValue(styleable, selection.selectedWeaponStyle);
                                                }
                                                var notify = styleable.GetType().GetMethod("Notify_StyleChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                                notify?.Invoke(styleable, null);
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        offer.pawn.equipment.AddEquipment(weapon);
                        previewCreatedThings.Add(weapon);
                    }
                    else
                    {
                        weapon.Destroy();
                    }
                }
            }

            // Apply apparel
            if (selection.selectedApparelDefs != null && offer.pawn.apparel != null)
            {
                var appliedDefs = new HashSet<ThingDef>();
                foreach (var ad in selection.selectedApparelDefs)
                {
                    // Prevent multiple copies of the same apparel definition
                    if (ad == null || !appliedDefs.Add(ad))
                    {
                        continue;
                    }
                    Apparel apparel = null;

                    // Check if this apparel has customization data
                    var customization = selection.GetApparelCustomization(ad);
                    if (customization != null)
                    {
                        // Use the customized version
                        apparel = customization.CreateApparel();
                    }
                    else
                    {
                        // Use default version (allow null stuff for non-stuffable apparel like many helmets)
                        var stuff = GenStuff.DefaultStuffFor(ad);
                        apparel = ThingMaker.MakeThing(ad, stuff) as Apparel;
                    }

                    if (apparel != null)
                    {
                        if (CanWearOnTopWithoutConflicts(offer.pawn, apparel))
                        {
                            offer.pawn.apparel.Wear(apparel, false);
                            previewCreatedThings.Add(apparel);
                        }
                        else
                        {
                            apparel.Destroy();
                        }
                    }
                }
            }
            
            // Only call SetDirty after all equipment changes are complete
            PortraitsCache.SetDirty(offer.pawn);
        }

        private bool CanWearOnTopWithoutConflicts(Pawn pawn, Apparel apparel)
        {
            try
            {
                if (pawn == null || apparel == null || pawn.apparel == null) return false;
                if (!ApparelUtility.HasPartsToWear(pawn, apparel.def)) return false;
                var body = pawn.RaceProps?.body;
                if (body == null) return false;
                foreach (var worn in pawn.apparel.WornApparel)
                {
                    if (worn == null || worn.Destroyed) continue;
                    if (!ApparelUtility.CanWearTogether(apparel.def, worn.def, body))
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Apply themed background
            Color themedBg = ThemeProvider.GetWindowBackgroundColor();
            Widgets.DrawBoxSolid(inRect, themedBg);

            ApplySelectionToPreviewPawnIfChanged();
            float margin = 10f;
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "RimMercenaries_Loadout_Title".Translate());
            Text.Font = GameFont.Small;

            Rect subtitleRect = new Rect(inRect.x, titleRect.yMax + 2f, inRect.width, 24f);
            string pawnName = offer?.pawn?.LabelShortCap ?? "Unknown";
            Widgets.Label(subtitleRect, "RimMercenaries_Loadout_Subtitle".Translate(pawnName));

            string searchLabel = "RimMercenaries_Loadout_Filter_Search".Translate();
            Vector2 searchLabelSize = Text.CalcSize(searchLabel);
            Rect searchLabelRect = new Rect(inRect.x, subtitleRect.yMax + 6f, searchLabelSize.x + 6f, 24f);
            Widgets.Label(searchLabelRect, searchLabel);
            Rect searchRect = new Rect(searchLabelRect.xMax + 6f, subtitleRect.yMax + 6f, 220f, 24f);
            searchFilter = Widgets.TextField(searchRect, searchFilter ?? string.Empty);


            float listsTop = searchRect.yMax + margin;

            float leftPanelWidth = 260f;
            float rightPanelWidth = inRect.width - leftPanelWidth - margin * 2f;
            float panelHeight = inRect.height - listsTop - 70f;

            Rect previewRect = new Rect(inRect.x, listsTop, leftPanelWidth, panelHeight);
            Rect rightRect = new Rect(previewRect.xMax + margin, listsTop, rightPanelWidth, panelHeight);

            DrawPreview(previewRect);

            float half = (panelHeight - margin) / 2f;
            Rect weaponsRect = new Rect(rightRect.x, rightRect.y, rightRect.width, half);
            Rect apparelRect = new Rect(rightRect.x, weaponsRect.yMax + margin, rightRect.width, half);

            DrawWeaponsList(weaponsRect);
            DrawApparelList(apparelRect);

            DrawFooter(new Rect(inRect.x, inRect.yMax - 70f, inRect.width, 60f));
        }

        private bool filterRanged = true;
        private bool filterMelee = true;

        private void DrawWeaponsList(Rect rect)
        {
            Color themedBg = ThemeProvider.GetSectionBackgroundColor();
            Widgets.DrawBoxSolid(rect, themedBg);
            Widgets.DrawBox(rect);
            var header = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 24f);
            Widgets.Label(header, "RimMercenaries_Loadout_Weapon".Translate());

            // Filters: melee/ranged
            Rect filtersRect = new Rect(rect.x + 6f, header.yMax + 2f, rect.width - 12f, 24f);
            float fx = filtersRect.x;
            bool ranged = filterRanged, melee = filterMelee;
            Widgets.CheckboxLabeled(new Rect(fx, filtersRect.y, 120f, 24f), "RimMercenaries_Loadout_Filter_Ranged".Translate(), ref ranged);
            fx += 126f;
            Widgets.CheckboxLabeled(new Rect(fx, filtersRect.y, 120f, 24f), "RimMercenaries_Loadout_Filter_Melee".Translate(), ref melee);
            filterRanged = ranged; filterMelee = melee;

            float listTop = filtersRect.yMax + 2f;
            float listHeight = Mathf.Max(0f, rect.yMax - listTop - 6f);
            Rect outRect = new Rect(rect.x + 6f, listTop, rect.width - 12f, listHeight);
            
            // Use cached filtered weapons
            var filteredWeapons = GetCachedFilteredWeapons();
            int displayableWeapons = filteredWeapons.Count;
            // Extra space if weapon customization is shown
            float extraWeaponRows = (selection.selectedWeaponDef != null && showWeaponCustomization) ? CalculateWeaponCustomizationPanelHeight() : 0f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(displayableWeapons * 28f + extraWeaponRows, outRect.height + 1f));
            Widgets.BeginScrollView(outRect, ref weaponsScroll, viewRect);
            float y = 0f;
            foreach (var def in filteredWeapons)
            {
                bool isSelected = selection.selectedWeaponDef == def;
                float baseRowHeight = 26f;
                float customizationHeight = (isSelected && showWeaponCustomization) ? CalculateWeaponCustomizationPanelHeight() : 0f;
                float rowHeight = baseRowHeight + customizationHeight;
                Rect row = new Rect(0f, y, viewRect.width, baseRowHeight);
                DrawWeaponRow(row, def, isSelected);
                if (Widgets.ButtonInvisible(row))
                {
                    selection.selectedWeaponDef = def;
                    // Reset style when switching weapons
                    selection.selectedWeaponStyle = null;
                    selectionHash = -1; // Force preview refresh
                    InvalidateApparelCaches(); // Weapon changes may affect apparel eligibility
                    InvalidateCostCache();
                }
                if (isSelected)
                {
                    Rect custRect = new Rect(0f, y + baseRowHeight, viewRect.width, customizationHeight);
                    DrawWeaponCustomization(custRect, def);
                }
                y += rowHeight + 2f;
            }
            Widgets.EndScrollView();
        }

        private void DrawWeaponRow(Rect row, ThingDef weaponDef, bool isSelected)
        {
            // Draw selection highlight
            if (isSelected)
            {
                Widgets.DrawHighlightSelected(row);
            }
            else if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }

            // Radio button circle
            Rect radioRect = new Rect(row.x, row.y + 2f, 20f, 20f);
            bool radioClicked = Widgets.RadioButton(radioRect.x, radioRect.y, isSelected);
            if (radioClicked)
            {
                // Make radio button behave like clicking the weapon row
                if (selection.selectedWeaponDef != weaponDef)
                {
                    selection.selectedWeaponDef = weaponDef;
                    selectionHash = -1; // Force preview refresh
                    InvalidateApparelCaches();
                    InvalidateCostCache();
                }
            }

            // Icon
            float iconSize = 24f;
            Rect iconRect = new Rect(row.x + 26f, row.y + 1f, iconSize, iconSize);
            Texture2D icon = weaponDef.uiIcon;
            if (icon != null)
            {
                Color oldColor = GUI.color;
                GUI.color = weaponDef.uiIconColor;
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                GUI.color = oldColor;
            }

            // Text and price
            Rect textRect = new Rect(row.x + 56f, row.y, row.width - 120f, row.height);
            Widgets.Label(textRect, weaponDef.LabelCap);

            // Show price (cached)
            int itemCost = GetCachedItemCost(weaponDef);
            string priceText = itemCost.ToString() + "s";
            Vector2 priceSize = Text.CalcSize(priceText);
            Rect priceRect = new Rect(row.xMax - priceSize.x - 6f, row.y, priceSize.x, row.height);
            Widgets.Label(priceRect, priceText);
        }

        private bool showWeaponCustomization = true;

        private void DrawWeaponCustomization(Rect rect, ThingDef weaponDef)
        {
            if (weaponDef == null) return;
            Color themedBg = ThemeProvider.GetSectionBackgroundColor();
            Widgets.DrawBoxSolid(rect, themedBg);
            Widgets.DrawBox(rect);

            float margin = 6f;
            float y = rect.y + margin;
            float x = rect.x + margin;
            float width = rect.width - margin * 2;

            // Style row for weapon
            // Discover possible styles for this weapon using the same logic as apparel
            List<ThingStyleDef> possibleStyles = DiscoverStylesForThingDef(weaponDef);
            Rect styleLabelRect = new Rect(x, y, 80f, 24f);
            Widgets.Label(styleLabelRect, "RimMercenaries_Customization_Style".Translate() + ":");
            Rect styleDropdownRect = new Rect(x + 85f, y, width - 85f, 24f);
            int styleCount = possibleStyles?.Count ?? 0;
            string styleText = selection.selectedWeaponStyle != null
                ? GetStyleDisplayLabel(selection.selectedWeaponStyle)
                : (styleCount == 0 ? "None (no styles)" : "None");
            if (styleCount > 0)
            {
                if (Widgets.ButtonText(styleDropdownRect, styleText))
                {
                    var opts = new List<FloatMenuOption>();
                    opts.Add(new FloatMenuOption("None", () => { selection.selectedWeaponStyle = null; selectionHash = -1; }));
                    foreach (var s in possibleStyles.OrderBy(s => GetStyleDisplayLabel(s)))
                    {
                        var captured = s;
                        opts.Add(new FloatMenuOption(GetStyleDisplayLabel(captured), () => { selection.selectedWeaponStyle = captured; selectionHash = -1; }));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
            }
            else
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(styleDropdownRect, styleText);
                GUI.color = prev;
            }
        }

        private float CalculateWeaponCustomizationPanelHeight()
        {
            float height = 0f;
            height += 30f; // Style row
            height += 6f; // bottom margin
            return height;
        }

        private List<ThingStyleDef> DiscoverStylesForThingDef(ThingDef def)
        {
            var styles = new List<ThingStyleDef>();
            try
            {
                // From ThingStyleDef hints
                foreach (var s in DefDatabase<ThingStyleDef>.AllDefsListForReading)
                {
                    if (s == null) continue;
                    bool applies = false;
                    var t = s.GetType();
                    var appliesTo = t.GetMethod("AppliesTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (appliesTo != null)
                    {
                        var pars = appliesTo.GetParameters();
                        if (pars.Length == 1 && typeof(Def).IsAssignableFrom(pars[0].ParameterType))
                        {
                            try { applies = (bool)appliesTo.Invoke(s, new object[] { def }); } catch { }
                        }
                    }
                    if (!applies)
                    {
                        var fThingDef = t.GetField("thingDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var pThingDef = t.GetProperty("thingDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        ThingDef td = null;
                        if (fThingDef != null) td = fThingDef.GetValue(s) as ThingDef;
                        else if (pThingDef != null) td = pThingDef.GetValue(s, null) as ThingDef;
                        if (td != null && (td == def || string.Equals(td.defName, def.defName)))
                        {
                            applies = true;
                        }
                    }
                    if (applies) styles.Add(s);
                }
            }
            catch { }
            try
            {
                // From StyleCategoryDef mappings
                foreach (var cat in DefDatabase<StyleCategoryDef>.AllDefsListForReading)
                {
                    if (cat == null) continue;
                    var tcat = cat.GetType();
                    var field = tcat.GetField("thingDefStyles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var prop = tcat.GetProperty("thingDefStyles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    System.Collections.IEnumerable entries = null;
                    if (prop != null) { try { entries = prop.GetValue(cat) as System.Collections.IEnumerable; } catch { } }
                    if (entries == null && field != null) { try { entries = field.GetValue(cat) as System.Collections.IEnumerable; } catch { } }
                    if (entries == null) continue;
                    foreach (var entry in entries)
                    {
                        if (entry == null) continue;
                        var te = entry.GetType();
                        ThingDef eThing = null; ThingStyleDef eStyle = null;
                        var fThing = te.GetField("thingDef") ?? te.GetField("ThingDef");
                        var pThing = te.GetProperty("thingDef") ?? te.GetProperty("ThingDef");
                        var fStyle = te.GetField("styleDef") ?? te.GetField("StyleDef");
                        var pStyle = te.GetProperty("styleDef") ?? te.GetProperty("StyleDef");
                        try { if (fThing != null) eThing = fThing.GetValue(entry) as ThingDef; else if (pThing != null) eThing = pThing.GetValue(entry, null) as ThingDef; } catch { }
                        try { if (fStyle != null) eStyle = fStyle.GetValue(entry) as ThingStyleDef; else if (pStyle != null) eStyle = pStyle.GetValue(entry, null) as ThingStyleDef; } catch { }
                        if (eThing == def && eStyle != null) styles.Add(eStyle);
                    }
                }
            }
            catch { }
            return styles.Distinct().ToList();
        }

        private bool ShouldShowWeapon(ThingDef def)
        {
            if (def == null) return false;
            bool isRanged = def.IsRangedWeapon;
            bool isMelee = def.IsMeleeWeapon;
            if (isRanged && !filterRanged) return false;
            if (isMelee && !filterMelee) return false;
            return true;
        }

        private void DrawApparelList(Rect rect)
        {
            Color themedBg = ThemeProvider.GetSectionBackgroundColor();
            Widgets.DrawBoxSolid(rect, themedBg);
            Widgets.DrawBox(rect);
            var header = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 24f);
            Widgets.Label(header, "RimMercenaries_Loadout_Apparel".Translate());

            // Selected info line
            Rect infoRect = new Rect(rect.x + 6f, header.yMax + 2f, rect.width - 12f, 22f);
            string selectedInfo = "RimMercenaries_Loadout_SelectedCount".Translate(selection.selectedApparelDefs.Count);
            Widgets.Label(infoRect, selectedInfo);

            float listTop = infoRect.yMax + 2f;
            float listHeight = Mathf.Max(0f, rect.yMax - listTop - 6f);
            Rect outRect = new Rect(rect.x + 6f, listTop, rect.width - 12f, listHeight);
            var filteredApparel = GetCachedEligibleApparel();

            // Calculate total height including expanded customization
            float totalHeight = 0f;
            foreach (var def in filteredApparel)
            {
                float rowHeight = 28f; // Base row height
                if (showCustomization && expandedApparelDef == def)
                {
                    rowHeight += CalculateCustomizationPanelHeight();
                }
                totalHeight += rowHeight;
            }

            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(totalHeight, outRect.height + 1f));
            Widgets.BeginScrollView(outRect, ref apparelScroll, viewRect);
            try
            {
                float y = 0f;
                foreach (var def in filteredApparel)
                {
                    // Calculate row height - expanded if this item is being customized
                    float baseRowHeight = 28f;
                    bool isExpanded = showCustomization && expandedApparelDef == def;
                    float customizationHeight = isExpanded ? CalculateCustomizationPanelHeight() : 0f;
                    float rowHeight = baseRowHeight + customizationHeight;

                    Rect row = new Rect(0f, y, viewRect.width, baseRowHeight);
                    bool prev = selection.selectedApparelDefs.Contains(def);
                    bool has = prev;

                    // Draw selection highlight
                    if (has)
                    {
                        Widgets.DrawHighlightSelected(row);
                    }
                    else if (Mouse.IsOver(row))
                    {
                        Widgets.DrawHighlight(row);
                    }

                    // Checkbox
                    Rect checkboxRect = new Rect(row.x, row.y + 2f, 20f, 20f);
                    Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref has);

                    // Icon
                    float iconSize = 24f;
                    Rect iconRect = new Rect(row.x + 26f, row.y + 1f, iconSize, iconSize);
                    Texture2D icon = def.uiIcon;
                    if (icon != null)
                    {
                        Color oldColor = GUI.color;
                        GUI.color = def.uiIconColor;
                        GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                        GUI.color = oldColor;
                    }

                    // Text and price
                    Rect textRect = new Rect(row.x + 56f, row.y, row.width - 120f, row.height);
                    Widgets.Label(textRect, def.LabelCap);

                    // Show price (dynamic when customizing, otherwise cached)
                    int itemCost = (isExpanded && currentCustomization != null && expandedApparelDef == def)
                        ? selection.GetItemCost(def, negotiator, currentCustomization)
                        : GetCachedItemCost(def);
                    string priceText = itemCost.ToString() + "s";
                    Vector2 priceSize = Text.CalcSize(priceText);
                    Rect priceRect = new Rect(row.xMax - priceSize.x - 6f, row.y, priceSize.x, row.height);
                    Widgets.Label(priceRect, priceText);

                    // Handle click on entire row
                    bool clickedRow = Widgets.ButtonInvisible(row);
                    if (clickedRow && !(Event.current != null && checkboxRect.Contains(Event.current.mousePosition)))
                    {
                        // Toggle customization instead of direct selection
                        if (expandedApparelDef == def)
                        {
                            showCustomization = !showCustomization;
                        }
                        else
                        {
                            if (def != null)
                            {
                                expandedApparelDef = def;
                                // Seed customization with any previously saved customization
                                currentCustomization = selection.GetApparelCustomization(def)?.Clone() ?? new ApparelCustomizationData(def);
                                showCustomization = true;
                            }
                        }
                    }

                    if (has != prev)
                    {
                        if (has)
                        {
                            if (!selection.selectedApparelDefs.Contains(def)) selection.selectedApparelDefs.Add(def);
                        }
                        else
                        {
                            selection.selectedApparelDefs.Remove(def);
                        }
                        SanitizeSelection();
                        selectionHash = -1; // Force preview refresh
                        InvalidateApparelCaches(); // Apparel selection changed
                        InvalidateCostCache();
                    }

                    // Draw customization panel if expanded - now properly positioned
                    if (isExpanded)
                    {
                        Rect customizationRect = new Rect(0f, y + baseRowHeight, viewRect.width, customizationHeight);
                        DrawApparelCustomization(customizationRect, def);
                    }

                    y += rowHeight;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private bool IsApparelEligible(ThingDef def)
        {
            if (def?.apparel == null) return false;
            if (!CanPawnWear(def)) return false;
            // Disallow if it conflicts with any already selected apparel
            var body = offer.pawn?.RaceProps?.body;
            if (body == null) return false;
            foreach (var sel in selection.selectedApparelDefs)
            {
                if (sel?.apparel == null) continue;
                if (!ApparelUtility.CanWearTogether(def, sel, body))
                {
                    return false;
                }
            }
            return true;
        }

        private void DrawApparelCustomization(Rect rect, ThingDef apparelDef)
        {
            if (currentCustomization == null || apparelDef == null) return;

            Color themedBg = ThemeProvider.GetSectionBackgroundColor();
            themedBg.a = 0.9f; // Slightly more transparent for customization panel
            Widgets.DrawBoxSolid(rect, themedBg);
            Widgets.DrawBox(rect);

            float margin = 8f;
            float y = rect.y + margin;
            float x = rect.x + margin;
            float width = rect.width - margin * 2;

            // Title
            Rect titleRect = new Rect(x, y, width, 24f);
            Widgets.Label(titleRect, "RimMercenaries_Customization_Title".Translate(apparelDef.LabelCap));
            y += 30f;

            // Style selection (Ideology)
            try
            {
                // Determine available styles for this apparel by reflecting ThingStyleDef and StyleCategoryDef (thingDefStyles)
                List<ThingStyleDef> possibleStyles = new List<ThingStyleDef>();
                try
                {
                    var all = DefDatabase<ThingStyleDef>.AllDefsListForReading;
                    foreach (var s in all)
                    {
                        if (s == null) continue;
                        bool applies = false;
                        var t = s.GetType();

                        // Prefer an AppliesTo(Def) method if present
                        var appliesTo = t.GetMethod("AppliesTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (appliesTo != null)
                        {
                            var pars = appliesTo.GetParameters();
                            if (pars.Length == 1 && typeof(Def).IsAssignableFrom(pars[0].ParameterType))
                            {
                                try { applies = (bool)appliesTo.Invoke(s, new object[] { apparelDef }); } catch { }
                            }
                        }

                        // Fallbacks: properties/fields that reference applicable defs
                        if (!applies)
                        {
                            // Direct field/property commonly named "thingDef"
                            try
                            {
                                var fThingDef = t.GetField("thingDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                var pThingDef = t.GetProperty("thingDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                ThingDef td = null;
                                if (fThingDef != null) td = fThingDef.GetValue(s) as ThingDef;
                                else if (pThingDef != null) td = pThingDef.GetValue(s, null) as ThingDef;
                                if (td != null && (td == apparelDef || string.Equals(td.defName, apparelDef.defName)))
                                {
                                    applies = true;
                                }
                            }
                            catch { }
                        }

                        if (!applies)
                        {
                            var propSingle = t.GetProperty("appliesToDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (propSingle != null)
                            {
                                try { applies = object.Equals(propSingle.GetValue(s), apparelDef); } catch { }
                            }
                        }

                        if (!applies)
                        {
                            var fieldSingle = t.GetField("appliesToDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (fieldSingle != null)
                            {
                                try { applies = object.Equals(fieldSingle.GetValue(s), apparelDef); } catch { }
                            }
                        }

                        if (!applies)
                        {
                            var propList = t.GetProperty("appliesTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (propList != null)
                            {
                                try
                                {
                                    var coll = propList.GetValue(s) as System.Collections.IEnumerable;
                                    if (coll != null)
                                    {
                                        foreach (var item in coll) { if (object.Equals(item, apparelDef)) { applies = true; break; } }
                                    }
                                }
                                catch { }
                            }
                        }

                        if (!applies)
                        {
                            var fieldList = t.GetField("appliesTo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (fieldList != null)
                            {
                                try
                                {
                                    var coll = fieldList.GetValue(s) as System.Collections.IEnumerable;
                                    if (coll != null)
                                    {
                                        foreach (var item in coll) { if (object.Equals(item, apparelDef)) { applies = true; break; } }
                                    }
                                }
                                catch { }
                            }
                        }

                        if (applies)
                        {
                            possibleStyles.Add(s);
                        }
                    }
                    // Also scan StyleCategoryDef.thingDefStyles mappings
                    try
                    {
                        var allCats = DefDatabase<StyleCategoryDef>.AllDefsListForReading;
                        foreach (var cat in allCats)
                        {
                            if (cat == null) continue;
                            var tcat = cat.GetType();
                            var field = tcat.GetField("thingDefStyles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            var prop = tcat.GetProperty("thingDefStyles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            System.Collections.IEnumerable entries = null;
                            if (prop != null) { try { entries = prop.GetValue(cat) as System.Collections.IEnumerable; } catch { } }
                            if (entries == null && field != null) { try { entries = field.GetValue(cat) as System.Collections.IEnumerable; } catch { } }
                            if (entries == null) continue;
                            foreach (var entry in entries)
                            {
                                if (entry == null) continue;
                                var te = entry.GetType();
                                ThingDef eThing = null; ThingStyleDef eStyle = null;
                                // Support both camelCase fields and PascalCase properties
                                var fThing = te.GetField("thingDef") ?? te.GetField("ThingDef");
                                var pThing = te.GetProperty("thingDef") ?? te.GetProperty("ThingDef");
                                var fStyle = te.GetField("styleDef") ?? te.GetField("StyleDef");
                                var pStyle = te.GetProperty("styleDef") ?? te.GetProperty("StyleDef");
                                try {
                                    if (fThing != null) eThing = fThing.GetValue(entry) as ThingDef;
                                    else if (pThing != null) eThing = pThing.GetValue(entry, null) as ThingDef;
                                } catch { }
                                try {
                                    if (fStyle != null) eStyle = fStyle.GetValue(entry) as ThingStyleDef;
                                    else if (pStyle != null) eStyle = pStyle.GetValue(entry, null) as ThingStyleDef;
                                } catch { }
                                if (eThing == apparelDef && eStyle != null)
                                {
                                    possibleStyles.Add(eStyle);
                                }
                            }
                        }
                    }
                    catch { }
                }
                catch { }

                // Always show Style row; if no styles, show disabled/"None (no styles)" text
                Rect styleLabelRect = new Rect(x, y, 80f, 24f);
                Widgets.Label(styleLabelRect, "RimMercenaries_Customization_Style".Translate() + ":");

                Rect styleDropdownRect = new Rect(x + 85f, y, width - 85f, 24f);
                int styleCount = possibleStyles?.Count ?? 0;
                string styleText = currentCustomization.styleDef != null
                    ? GetStyleDisplayLabel(currentCustomization.styleDef)
                    : (styleCount == 0 ? "None (no styles)" : "None");

                if (styleCount > 0)
                {
                    if (Widgets.ButtonText(styleDropdownRect, styleText))
                    {
                        var opts = new List<FloatMenuOption>();
                        opts.Add(new FloatMenuOption("None", () => currentCustomization.styleDef = null));
                        foreach (var s in possibleStyles.OrderBy(s => GetStyleDisplayLabel(s)))
                        {
                            var captured = s;
                            opts.Add(new FloatMenuOption(GetStyleDisplayLabel(captured), () => currentCustomization.styleDef = captured));
                        }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                }
                else
                {
                    // Draw a disabled-looking label to indicate no styles exist
                    var prev = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.6f);
                    Widgets.Label(styleDropdownRect, styleText);
                    GUI.color = prev;
                }
                y += 30f;
            }
            catch { }

            // Material selection
            Rect materialLabelRect = new Rect(x, y, 80f, 24f);
            Widgets.Label(materialLabelRect, "RimMercenaries_Customization_Material".Translate() + ":");

            Rect materialDropdownRect = new Rect(x + 85f, y, width - 85f, 24f);
            string materialText = currentCustomization.materialDef?.LabelCap ?? "Default";
            if (Widgets.ButtonText(materialDropdownRect, materialText))
            {
                var materialOptions = new List<FloatMenuOption>();
                materialOptions.Add(new FloatMenuOption("Default", () => currentCustomization.materialDef = GenStuff.DefaultStuffFor(apparelDef)));

                foreach (var stuff in GenStuff.AllowedStuffsFor(apparelDef))
                {
                    materialOptions.Add(new FloatMenuOption(stuff.LabelCap, () => currentCustomization.materialDef = stuff));
                }

                Find.WindowStack.Add(new FloatMenu(materialOptions));
            }
            y += 30f;

            // Quality selection
            Rect qualityLabelRect = new Rect(x, y, 80f, 24f);
            Widgets.Label(qualityLabelRect, "RimMercenaries_Customization_Quality".Translate() + ":");

            Rect qualityDropdownRect = new Rect(x + 85f, y, width - 85f, 24f);
            string qualityText = currentCustomization.quality.ToString();
            if (Widgets.ButtonText(qualityDropdownRect, qualityText))
            {
                var qualityOptions = new List<FloatMenuOption>();
                foreach (QualityCategory quality in Enum.GetValues(typeof(QualityCategory)))
                {
                    qualityOptions.Add(new FloatMenuOption(quality.ToString(), () => currentCustomization.quality = quality));
                }
                Find.WindowStack.Add(new FloatMenu(qualityOptions));
            }
            y += 30f;

            // Hit Points slider
            Rect hpLabelRect = new Rect(x, y, 80f, 24f);
            Widgets.Label(hpLabelRect, "RimMercenaries_Customization_HitPoints".Translate() + ":");

            float maxHP = apparelDef.GetStatValueAbstract(StatDefOf.MaxHitPoints);
            Rect hpSliderRect = new Rect(x + 85f, y, width - 85f, 24f);
            float currentHP = currentCustomization.hitPoints > 0 ? currentCustomization.hitPoints : Mathf.RoundToInt(maxHP * Rand.Range(0.5f, 1f));
            currentHP = Widgets.HorizontalSlider(hpSliderRect, currentHP, 1f, maxHP, false, null, null, null, 1f);
            currentCustomization.hitPoints = Mathf.RoundToInt(currentHP);

            Rect hpValueRect = new Rect(x + width - 50f, y, 45f, 24f);
            Widgets.Label(hpValueRect, currentCustomization.hitPoints.ToString());
            y += 30f;

            // Color selection section
            Rect colorLabelRect = new Rect(x, y, 80f, 24f);
            Widgets.Label(colorLabelRect, "RimMercenaries_Customization_Color".Translate() + ":");
            y += 30f;

            // Get faction colors for reference
            var playerFaction = Faction.OfPlayer;
            Color factionColor = playerFaction?.color ?? Color.gray;
            Color ideologyColor = Color.gray;
            if (playerFaction?.ideos?.PrimaryIdeo?.colorDef != null)
            {
                ideologyColor = playerFaction.ideos.PrimaryIdeo.colorDef.color;
            }
            else
            {
                ideologyColor = factionColor;
            }

            // Color selection - simplified boxes (robust spacing so last box is reachable)
            float boxY = y;
            float boxX = x + 85f;
            float boxSize = 28f;
            float availableColorWidth = width - 85f;
            float spacing = Mathf.Max(6f, (availableColorWidth - (boxSize * 4f)) / 3f);
            bool handledClick = false;

            // Base color box
            Rect baseColorRect = new Rect(boxX, boxY, boxSize, boxSize);
            Widgets.DrawBoxSolid(baseColorRect, currentCustomization.apparelDef.uiIconColor);
            Widgets.DrawBox(baseColorRect);
            if (!handledClick && Widgets.ButtonInvisible(baseColorRect))
            {
                currentCustomization.useCustomColor = false;
                currentCustomization.color = currentCustomization.apparelDef.uiIconColor;
                selectionHash = -1; // Force preview refresh
                handledClick = true;
                if (Event.current != null) Event.current.Use();
            }
            boxX += boxSize + spacing;

            // Faction color box (with heart icon)
            Rect factionColorRect = new Rect(boxX, boxY, boxSize, boxSize);
            Widgets.DrawBoxSolid(factionColorRect, factionColor);
            Widgets.DrawBox(factionColorRect);

            // Draw heart symbol for faction (semi-transparent, centered and full size)
            Color heartColor = Color.white;
            heartColor.a = 0.6f;
            GUI.color = heartColor;
            Rect heartRect = new Rect(boxX, boxY, boxSize, boxSize);
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(heartRect, "♥");
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
            GUI.color = Color.white;

            if (!handledClick && Widgets.ButtonInvisible(factionColorRect))
            {
                currentCustomization.useCustomColor = false;
                currentCustomization.color = factionColor;
                selectionHash = -1; // Force preview refresh
                handledClick = true;
                if (Event.current != null) Event.current.Use();
            }
            boxX += boxSize + spacing;

            // Ideology color box (with star icon)
            Rect ideologyColorRect = new Rect(boxX, boxY, boxSize, boxSize);
            Widgets.DrawBoxSolid(ideologyColorRect, ideologyColor);
            Widgets.DrawBox(ideologyColorRect);

            // Draw star symbol for ideology (semi-transparent, centered and full size)
            Color starColor = Color.white;
            starColor.a = 0.6f;
            GUI.color = starColor;
            Rect starRect = new Rect(boxX, boxY, boxSize, boxSize);
            prevFont = Text.Font;
            prevAnchor = Text.Anchor;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(starRect, "★");
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
            GUI.color = Color.white;

            if (!handledClick && Widgets.ButtonInvisible(ideologyColorRect))
            {
                currentCustomization.useCustomColor = false;
                currentCustomization.color = ideologyColor;
                selectionHash = -1; // Force preview refresh
                handledClick = true;
                if (Event.current != null) Event.current.Use();
            }
            boxX += boxSize + spacing;

            // Custom color box
            Rect customColorRect = new Rect(boxX, boxY, boxSize, boxSize);
            Widgets.DrawBoxSolid(customColorRect, currentCustomization.color);
            Widgets.DrawBox(customColorRect);

            if (!handledClick && Widgets.ButtonInvisible(customColorRect))
            {
                currentCustomization.useCustomColor = true;
                handledClick = true;
                if (Event.current != null) Event.current.Use();
            }

            // Recompute selection after handling clicks to ensure highlight matches the clicked box immediately
            bool eqIdeology = ColorsMatch(currentCustomization.color, ideologyColor);
            bool eqFaction = ColorsMatch(currentCustomization.color, factionColor);
            bool eqBase = ColorsMatch(currentCustomization.color, currentCustomization.apparelDef.uiIconColor);
            string selectedColorTypeNow = eqIdeology
                ? "ideology"
                : (eqFaction
                    ? "faction"
                    : (eqBase
                        ? "base"
                        : (currentCustomization.useCustomColor ? "custom" : "base")));
            // Draw selection highlight after computing the final selection
            if (selectedColorTypeNow == "base")
            {
                GUI.color = Color.yellow;
                Widgets.DrawBox(baseColorRect.ExpandedBy(2f));
                GUI.color = Color.white;
            }
            else if (selectedColorTypeNow == "faction")
            {
                GUI.color = Color.yellow;
                Widgets.DrawBox(factionColorRect.ExpandedBy(2f));
                GUI.color = Color.white;
            }
            else if (selectedColorTypeNow == "ideology")
            {
                GUI.color = Color.yellow;
                Widgets.DrawBox(ideologyColorRect.ExpandedBy(2f));
                GUI.color = Color.white;
            }
            else if (selectedColorTypeNow == "custom")
            {
                GUI.color = Color.yellow;
                Widgets.DrawBox(customColorRect.ExpandedBy(2f));
                GUI.color = Color.white;
            }

            y += 40f;

            // RGB controls - always visible
            float sliderWidth = width - 140f;
            float valueWidth = 50f;

            // Red slider
            Rect redLabelRect = new Rect(x, y, 30f, 24f);
            Widgets.Label(redLabelRect, "R:");
            Rect redSliderRect = new Rect(x + 35f, y, sliderWidth, 24f);
            float oldRed = currentCustomization.color.r;
            currentCustomization.color.r = Widgets.HorizontalSlider(redSliderRect, currentCustomization.color.r, 0f, 1f, false, null, "0", "1", 0.01f);
            if (oldRed != currentCustomization.color.r)
            {
                if (Mouse.IsOver(redSliderRect) && Event.current != null && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag))
                {
                    currentCustomization.useCustomColor = true;
                    selectionHash = -1;
                }
            }
            Rect redValueRect = new Rect(redSliderRect.xMax + 10f, y, valueWidth, 24f);
            Widgets.Label(redValueRect, Mathf.RoundToInt(currentCustomization.color.r * 255).ToString());
            y += 28f;

            // Green slider
            Rect greenLabelRect = new Rect(x, y, 30f, 24f);
            Widgets.Label(greenLabelRect, "G:");
            Rect greenSliderRect = new Rect(x + 35f, y, sliderWidth, 24f);
            float oldGreen = currentCustomization.color.g;
            currentCustomization.color.g = Widgets.HorizontalSlider(greenSliderRect, currentCustomization.color.g, 0f, 1f, false, null, "0", "1", 0.01f);
            if (oldGreen != currentCustomization.color.g)
            {
                if (Mouse.IsOver(greenSliderRect) && Event.current != null && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag))
                {
                    currentCustomization.useCustomColor = true;
                    selectionHash = -1;
                }
            }
            Rect greenValueRect = new Rect(greenSliderRect.xMax + 10f, y, valueWidth, 24f);
            Widgets.Label(greenValueRect, Mathf.RoundToInt(currentCustomization.color.g * 255).ToString());
            y += 28f;

            // Blue slider
            Rect blueLabelRect = new Rect(x, y, 30f, 24f);
            Widgets.Label(blueLabelRect, "B:");
            Rect blueSliderRect = new Rect(x + 35f, y, sliderWidth, 24f);
            float oldBlue = currentCustomization.color.b;
            currentCustomization.color.b = Widgets.HorizontalSlider(blueSliderRect, currentCustomization.color.b, 0f, 1f, false, null, "0", "1", 0.01f);
            if (oldBlue != currentCustomization.color.b)
            {
                if (Mouse.IsOver(blueSliderRect) && Event.current != null && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag))
                {
                    currentCustomization.useCustomColor = true;
                    selectionHash = -1;
                }
            }
            Rect blueValueRect = new Rect(blueSliderRect.xMax + 10f, y, valueWidth, 24f);
            Widgets.Label(blueValueRect, Mathf.RoundToInt(currentCustomization.color.b * 255).ToString());
            y += 28f;

            // Color preview and preset buttons
            Rect previewRect = new Rect(x, y, 60f, 24f);
            Widgets.DrawBoxSolid(previewRect, currentCustomization.color);
            Widgets.DrawBox(previewRect);

            // Preset color buttons
            float presetX = x + 70f;
            Rect redPresetRect = new Rect(presetX, y, 20f, 20f);
            Widgets.DrawBoxSolid(redPresetRect, Color.red);
            Widgets.DrawBox(redPresetRect);
            if (Widgets.ButtonInvisible(redPresetRect))
            {
                currentCustomization.color = Color.red;
                currentCustomization.useCustomColor = true;
                selectionHash = -1;
            }
            presetX += 24f;

            Rect greenPresetRect = new Rect(presetX, y, 20f, 20f);
            Widgets.DrawBoxSolid(greenPresetRect, Color.green);
            Widgets.DrawBox(greenPresetRect);
            if (Widgets.ButtonInvisible(greenPresetRect))
            {
                currentCustomization.color = Color.green;
                currentCustomization.useCustomColor = true;
                selectionHash = -1;
            }
            presetX += 24f;

            Rect bluePresetRect = new Rect(presetX, y, 20f, 20f);
            Widgets.DrawBoxSolid(bluePresetRect, Color.blue);
            Widgets.DrawBox(bluePresetRect);
            if (Widgets.ButtonInvisible(bluePresetRect))
            {
                currentCustomization.color = Color.blue;
                currentCustomization.useCustomColor = true;
                selectionHash = -1;
            }
            presetX += 24f;

            Rect whitePresetRect = new Rect(presetX, y, 20f, 20f);
            Widgets.DrawBoxSolid(whitePresetRect, Color.white);
            Widgets.DrawBox(whitePresetRect);
            if (Widgets.ButtonInvisible(whitePresetRect))
            {
                currentCustomization.color = Color.white;
                currentCustomization.useCustomColor = true;
                selectionHash = -1;
            }
            presetX += 24f;

            Rect blackPresetRect = new Rect(presetX, y, 20f, 20f);
            Widgets.DrawBoxSolid(blackPresetRect, Color.black);
            Widgets.DrawBox(blackPresetRect);
            if (Widgets.ButtonInvisible(blackPresetRect))
            {
                currentCustomization.color = Color.black;
                currentCustomization.useCustomColor = true;
                selectionHash = -1;
            }

            y += 35f;

            // Add button
            Rect addButtonRect = new Rect(x + width - 100f, y, 95f, 30f);
            if (Widgets.ButtonText(addButtonRect, "RimMercenaries_Customization_Add".Translate()))
            {
                AddCustomizedApparelToLoadout();
                showCustomization = false;
                expandedApparelDef = null;
                currentCustomization = null;
            }
        }

        private void AddCustomizedApparelToLoadout()
        {
            if (currentCustomization == null) return;

            // Store the customization data in the selection
            selection.AddApparelCustomization(currentCustomization.apparelDef, currentCustomization);

            // Add to selection if not already there
            if (!selection.selectedApparelDefs.Contains(currentCustomization.apparelDef))
            {
                selection.selectedApparelDefs.Add(currentCustomization.apparelDef);
                selectionHash = -1; // Force preview refresh
            }

            InvalidateCostCache();

            Log.Message($"[RimMercenaries] Stored customization for {currentCustomization.apparelDef.LabelCap}: Material={currentCustomization.materialDef?.LabelCap ?? "Default"}, Quality={currentCustomization.quality}, HP={currentCustomization.hitPoints}, Color={currentCustomization.color.ToString()}");
        }



        private bool ColorsMatch(Color a, Color b, float tolerance = 0.01f)
        {
            return Mathf.Abs(a.r - b.r) < tolerance &&
                   Mathf.Abs(a.g - b.g) < tolerance &&
                   Mathf.Abs(a.b - b.b) < tolerance;
        }

        private float CalculateCustomizationPanelHeight()
        {
            // Match DrawApparelCustomization's actual vertical increments
            float margin = 8f; // Top/bottom content margin used in DrawApparelCustomization
            float height = margin;

            height += 30f; // Title row
            height += 30f; // Style row
            height += 30f; // Material row (label + dropdown)
            height += 30f; // Quality row (label + dropdown)
            height += 30f; // Hit Points row (label + slider + value)
            height += 30f; // Color label row
            height += 40f; // Color boxes row
            height += 28f * 3f; // RGB sliders (3 rows)
            height += 35f; // Preview + preset buttons row
            height += 30f; // Add button row

            height += margin; // Bottom margin
            return height;
        }

        private static string GetStyleDisplayLabel(ThingStyleDef style)
        {
            try
            {
                if (style == null) return "(missing label)";
                // Prefer explicit override label
                if (!string.IsNullOrEmpty(style.overrideLabel))
                {
                    return style.overrideLabel.CapitalizeFirst();
                }
                // Then fallback to def label
                if (!string.IsNullOrEmpty(style.label))
                {
                    return style.label.CapitalizeFirst();
                }
                // Then category label (e.g. Rustic, Morbid, Totemic)
                var cat = style.Category; // property resolves category
                if (cat != null && !string.IsNullOrEmpty(cat.label))
                {
                    return cat.label.CapitalizeFirst();
                }
                // Final fallback: defName
                if (!string.IsNullOrEmpty(style.defName))
                {
                    return style.defName.Replace('_', ' ').CapitalizeFirst();
                }
            }
            catch { }
            return "(missing label)";
        }

        private void DrawPreview(Rect rect)
        {
            Color themedBg = ThemeProvider.GetSectionBackgroundColor();
            Widgets.DrawBoxSolid(rect, themedBg);
            Widgets.DrawBox(rect);

            // Centered square portrait, keep space for lists and bottom buttons
            // Reserve enough space for both the Save/Load preset buttons and the Clear buttons below
            // Save/Load row starts at yMax - 56f (24f tall), Clear row at yMax - 30f (24f tall)
            // Use a slightly larger buffer so the scroll list never overlaps behind buttons
            float reservedBottom = 64f;
            float availableHeight = rect.height - reservedBottom - 12f;
            float portraitSide = Mathf.Min(rect.width - 12f, Mathf.Max(120f, availableHeight * 0.75f));
            float portraitX = rect.x + (rect.width - portraitSide) / 2f;
            Rect portraitRect = new Rect(portraitX, rect.y + 6f, portraitSide, portraitSide);
            try
            {
                var size = new Vector2(portraitRect.width, portraitRect.height);
                Texture tex = PortraitsCache.Get(offer.pawn, size, Rot4.South, default(Vector3), 1f);
                GUI.DrawTexture(portraitRect, tex, ScaleMode.ScaleToFit, true);
            }
            catch
            {
                Rect fallback = new Rect(portraitRect.x + 2f, portraitRect.y + 2f, portraitRect.width - 4f, portraitRect.height - 4f);
                Widgets.ThingIcon(fallback, offer.pawn);
            }

            // Overlay selected weapon icon
            if (selection.selectedWeaponDef != null)
            {
                float iconSize = Mathf.Min(60f, portraitSide * 0.35f);
                Rect weaponIconRect = new Rect(portraitRect.xMax - iconSize - 6f, portraitRect.yMax - iconSize - 6f, iconSize, iconSize);
                Texture2D icon = selection.selectedWeaponDef.uiIcon;
                if (icon != null)
                {
                    Color old = GUI.color;
                    GUI.color = selection.selectedWeaponDef.uiIconColor;
                    GUI.DrawTexture(weaponIconRect, icon, ScaleMode.ScaleToFit);
                    GUI.color = old;
                }
            }

            float y = portraitRect.yMax + 6f;
            Rect selTitle = new Rect(rect.x + 6f, y, rect.width - 12f, 22f);
            Widgets.Label(selTitle, "RimMercenaries_Loadout_SelectedCount".Translate(selection.SelectedItemCount));
            y += 22f;

            // Selected weapon label
            Rect wRow = new Rect(rect.x + 6f, y, rect.width - 12f, 22f);
            string wText = selection.selectedWeaponDef != null ? selection.selectedWeaponDef.LabelCap.ToString() : "None";
            Widgets.Label(wRow, "RimMercenaries_Loadout_Weapon".Translate() + ": " + wText);
            y += 22f;

            // Selected apparel list (scrollable)
            Rect aTitle = new Rect(rect.x + 6f, y, rect.width - 12f, 22f);
            Widgets.Label(aTitle, "RimMercenaries_Loadout_Apparel".Translate());
            y = aTitle.yMax + 2f;
            float listHeight = Mathf.Max(0f, rect.yMax - reservedBottom - y - 4f);
            Rect outRect = new Rect(rect.x + 6f, y, rect.width - 12f, listHeight);
            float rowHeight = 22f;
            int selectedCount = selection.selectedApparelDefs.Count;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(selectedCount * rowHeight, outRect.height + 1f));
            Widgets.BeginScrollView(outRect, ref selectedApparelScroll, viewRect);
            float yy = 0f;
            foreach (var ad in selection.selectedApparelDefs.ToList())
            {
                Rect row = new Rect(0f, yy, viewRect.width, rowHeight);
                Widgets.Label(new Rect(row.x + 4f, row.y, row.width - 24f, row.height), "• " + ad.LabelCap);
                Rect xBtn = new Rect(row.xMax - 18f, row.y + 2f, 18f, 18f);
                if (Widgets.ButtonText(xBtn, "x"))
                {
                    selection.selectedApparelDefs.Remove(ad);
                    selectionHash = -1; // force refresh
                }
                yy += rowHeight;
            }
            Widgets.EndScrollView();

            // Quick clear buttons
            float btnW = (rect.width - 12f) / 2f - 4f;
            Rect clearWeapon = new Rect(rect.x + 6f, rect.yMax - 30f, btnW, 24f);
            Rect clearApparel = new Rect(clearWeapon.xMax + 8f, rect.yMax - 30f, btnW, 24f);
            if (Widgets.ButtonText(clearWeapon, "RimMercenaries_Loadout_ClearWeapon".Translate()))
            {
                selection.selectedWeaponDef = null;
                selectionHash = -1;
                InvalidateApparelCaches();
                InvalidateCostCache();
            }
            if (Widgets.ButtonText(clearApparel, "RimMercenaries_Loadout_ClearApparel".Translate()))
            {
                selection.selectedApparelDefs.Clear();
                selectionHash = -1;
                InvalidateApparelCaches();
                InvalidateCostCache();
            }

            // Preset buttons
            float presetBtnWidth = (rect.width - 12f - 4f) / 2f; // Divide space for 2 buttons
            Rect savePreset = new Rect(rect.x + 6f, rect.yMax - 56f, presetBtnWidth, 24f);
            Rect loadPreset = new Rect(savePreset.xMax + 4f, rect.yMax - 56f, presetBtnWidth, 24f);
            if (Widgets.ButtonText(savePreset, "RimMercenaries_Loadout_SavePreset".Translate()))
            {
                Find.WindowStack.Add(new Dialog_SavePreset(selection));
            }
            if (Widgets.ButtonText(loadPreset, "RimMercenaries_Loadout_LoadPreset".Translate()))
            {
                Find.WindowStack.Add(new Dialog_LoadPreset(this));
            }
        }

        private IEnumerable<ThingDef> Filtered(List<ThingDef> defs)
        {
            if (defs == null) yield break;
            string f = (searchFilter ?? string.Empty).Trim();
            foreach (var d in defs)
            {
                string label = d?.label ?? d?.defName ?? string.Empty;
                if (f.Length == 0 || label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    yield return d;
            }
        }

        private List<ThingDef> GetCachedFilteredWeapons()
        {
            // Check if cache is valid
            if (cachedFilteredWeapons != null && 
                lastSearchFilter == searchFilter && 
                lastFilterRanged == filterRanged && 
                lastFilterMelee == filterMelee)
            {
                return cachedFilteredWeapons;
            }

            // Rebuild cache
            cachedFilteredWeapons = Filtered(availableWeaponDefs).Where(d => ShouldShowWeapon(d)).ToList();
            lastSearchFilter = searchFilter;
            lastFilterRanged = filterRanged;
            lastFilterMelee = filterMelee;

            return cachedFilteredWeapons;
        }

        private List<ThingDef> GetCachedEligibleApparel()
        {
            int currentSelectionHash = ComputeSelectionHash();
            
            // Check if cache is valid (selection hasn't changed and search filter is same)
            if (cachedEligibleApparel != null && 
                lastSearchFilter == searchFilter && 
                lastSelectionHash == currentSelectionHash &&
                apparelEligibilityFrameCache == Time.frameCount)
            {
                return cachedEligibleApparel;
            }

            // Rebuild cache
            cachedEligibleApparel = Filtered(availableApparelDefs).Where(d => IsApparelEligibleCached(d)).ToList();
            lastSearchFilter = searchFilter;
            lastSelectionHash = currentSelectionHash;
            apparelEligibilityFrameCache = Time.frameCount;

            return cachedEligibleApparel;
        }

        private bool IsApparelEligibleCached(ThingDef def)
        {
            // Use frame-based caching for eligibility checks
            if (apparelEligibilityFrameCache == Time.frameCount && apparelEligibilityCache.ContainsKey(def))
            {
                return apparelEligibilityCache[def];
            }

            bool result = IsApparelEligible(def);
            
            // Clear cache if it's from a different frame
            if (apparelEligibilityFrameCache != Time.frameCount)
            {
                apparelEligibilityCache.Clear();
                apparelEligibilityFrameCache = Time.frameCount;
            }
            
            apparelEligibilityCache[def] = result;
            return result;
        }

        private int GetCachedItemCost(ThingDef def)
        {
            if (itemCostCache.ContainsKey(def))
            {
                return itemCostCache[def];
            }

            int cost = selection.GetItemCost(def, negotiator);
            itemCostCache[def] = cost;
            return cost;
        }

        private void InvalidateApparelCaches()
        {
            cachedEligibleApparel = null;
            apparelEligibilityCache.Clear();
            apparelEligibilityFrameCache = -1;
        }

        private void InvalidateCostCache()
        {
            itemCostCache.Clear();
        }

        private void DrawFooter(Rect rect)
        {
            Color themedBg = ThemeProvider.GetSectionBackgroundColor();
            Widgets.DrawBoxSolid(rect, themedBg);
            Widgets.DrawBox(rect);
            var settings = RimMercenariesMod.ActiveSettings;
            int basePrice = offer.price;
            int loadoutCost = selection.CalculateAdditionalCost(negotiator);
            int total = basePrice + loadoutCost;

            float margin = 10f;
            float col = rect.width / 3f;
            Rect baseRect = new Rect(rect.x + margin, rect.y + 8f, col - margin * 2, rect.height - 16f);
            Rect loadoutRect = new Rect(baseRect.xMax + margin, rect.y + 8f, col - margin * 2, rect.height - 16f);
            Rect totalRect = new Rect(loadoutRect.xMax + margin, rect.y + 8f, col - margin * 2, rect.height - 16f);

            Widgets.Label(baseRect, "RimMercenaries_Loadout_BasePrice".Translate() + ": " + basePrice.ToString("N0"));
            Widgets.Label(loadoutRect, "RimMercenaries_Loadout_AdditionalCost".Translate() + ": " + loadoutCost.ToString("N0"));
            Widgets.Label(totalRect, "RimMercenaries_Loadout_Total".Translate() + ": " + total.ToString("N0"));

            float buttonW = 140f;
            Rect confirmRect = new Rect(totalRect.xMax - buttonW, rect.y + rect.height - 36f, buttonW, 30f);
            if (Widgets.ButtonText(confirmRect, "RimMercenaries_Loadout_Confirm".Translate()))
            {
                changesConfirmed = true;
                onConfirm?.Invoke(selection, total);
                Close();
            }

            Rect cancelRect = new Rect(confirmRect.x - (buttonW + margin), confirmRect.y, buttonW, 30f);
            if (Widgets.ButtonText(cancelRect, "RimMercenaries_Loadout_Cancel".Translate()))
            {
                Close();
            }
        }

        public void LoadPresetById(string id)
        {
            var preset = MercenaryLoadoutPresetManager.FindById(id);
            if (preset == null)
            {
                                    string err = "RimMercenaries_Loadout_PresetLoadFailed".Translate(id, "RimMercenaries_Loadout_PresetNotFound".Translate());
                Messages.Message(err, MessageTypeDefOf.RejectInput);
                return;
            }

            string report;
            var result = TryApplyPresetToSelection(preset, out report);
            if (result.success)
            {
                if (result.appliedItems == result.totalItems)
                {
                    string ok = "RimMercenaries_Loadout_PresetLoadedOk".Translate(preset.id, result.appliedItems);
                    Messages.Message(ok, MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    string warn = "RimMercenaries_Loadout_PresetLoadedWithIssues".Translate(preset.id, result.appliedItems, result.totalItems, report ?? "");
                    Messages.Message(warn, MessageTypeDefOf.NeutralEvent);
                }
            }
            else
            {
                string err = "RimMercenaries_Loadout_PresetLoadFailed".Translate(preset.id, report ?? "");
                Messages.Message(err, MessageTypeDefOf.RejectInput);
            }

            selectionHash = -1; // refresh preview
            InvalidateCostCache();
        }

        private (bool success, int appliedItems, int totalItems) TryApplyPresetToSelection(LoadoutPreset preset, out string report)
        {
            // Clear current worn gear to avoid false negatives like "slot already worn" during validation
            try
            {
                ClearPreviewPawnForPresetValidation();
            }
            catch { }

            var issues = new List<string>();
            int total = 0;
            int applied = 0;

            // Prepare new selection data
            ThingDef newWeapon = null;
            var newApparelDefs = new List<ThingDef>();
            var newCustomizations = new Dictionary<ThingDef, ApparelCustomizationData>();

            // Weapon
            if (!string.IsNullOrEmpty(preset.weaponDefName))
            {
                total++;
                var def = DefDatabase<ThingDef>.GetNamed(preset.weaponDefName, false);
                if (def == null)
                {
                    issues.Add($"Weapon not found: {preset.weaponDefName}");
                }
                else if (!def.IsWeapon || def.equipmentType == EquipmentType.None)
                {
                    issues.Add($"Not a usable weapon: {def.label}");
                }
                else if (!IsResearchedOrNoPrereq(def))
                {
                    issues.Add($"Research not finished for: {def.label}");
                }
                else if (IsNonPawnWeapon(def))
                {
                    issues.Add($"Not usable by pawns: {def.label}");
                }
                else if (!CanPawnUseWeapon(def))
                {
                    issues.Add($"Pawn cannot equip: {def.label}");
                }
                else
                {
                    newWeapon = def; applied++;
                }
            }

            // Apparel list
            if (preset.apparels != null)
            {
                var seenDefs = new HashSet<ThingDef>();
                foreach (var ap in preset.apparels)
                {
                    total++;
                    if (ap == null || string.IsNullOrEmpty(ap.defName))
                    {
                        issues.Add("Apparel entry invalid");
                        continue;
                    }
                    var def = DefDatabase<ThingDef>.GetNamed(ap.defName, false);
                    if (def == null || def.apparel == null)
                    {
                        issues.Add($"Apparel not found: {ap.defName}");
                        continue;
                    }
                    if (!IsResearchedOrNoPrereq(def))
                    {
                        issues.Add($"Research not finished for: {def.label}");
                        continue;
                    }
                    if (!CanPawnWear(def))
                    {
                        issues.Add($"Pawn cannot wear: {def.label}");
                        continue;
                    }
                    // Prevent duplicates of the same apparel definition
                    if (!seenDefs.Add(def))
                    {
                        issues.Add($"Duplicate apparel in preset ignored: {def.label}");
                        continue;
                    }
                    // Conflict check with already selected new items
                    bool conflict = false;
                    foreach (var already in newApparelDefs)
                    {
                        if (!ApparelUtility.CanWearTogether(def, already, offer.pawn.RaceProps.body))
                        {
                            issues.Add($"Conflicts: {def.label} with {already.label}");
                            conflict = true; break;
                        }
                    }
                    if (conflict) continue;

                    // Material validation
                    ThingDef chosenStuff = null;
                    if (!string.IsNullOrEmpty(ap.materialDefName))
                    {
                        var stuff = DefDatabase<ThingDef>.GetNamed(ap.materialDefName, false);
                        if (stuff != null && GenStuff.AllowedStuffsFor(def).Contains(stuff))
                        {
                            chosenStuff = stuff;
                        }
                        else
                        {
                            issues.Add($"Material not allowed for {def.label}: {ap.materialDefName}");
                            chosenStuff = GenStuff.DefaultStuffFor(def);
                        }
                    }
                    else
                    {
                        chosenStuff = GenStuff.DefaultStuffFor(def);
                    }

                    // Build customization
                    var cust = new ApparelCustomizationData(def)
                    {
                        materialDef = chosenStuff,
                        quality = ap.quality,
                        hitPoints = ap.hitPoints > 0 ? Mathf.Clamp(ap.hitPoints, 1, Mathf.RoundToInt(def.GetStatValueAbstract(StatDefOf.MaxHitPoints))) : Mathf.RoundToInt(def.GetStatValueAbstract(StatDefOf.MaxHitPoints)),
                        color = new Color(ap.colorR, ap.colorG, ap.colorB, ap.colorA == 0 ? 1f : ap.colorA),
                        useCustomColor = ap.useCustomColor
                    };

                    // Load style if present
                    try
                    {
                        if (!string.IsNullOrEmpty(ap.styleDefName))
                        {
                            cust.styleDef = DefDatabase<ThingStyleDef>.GetNamed(ap.styleDefName, false);
                        }
                    }
                    catch { }

                    // Backward compatibility: if preset has a stored color that differs from the apparel's base color,
                    // but useCustomColor wasn't saved (older presets), treat it as a custom color.
                    // This also preserves Faction/Ideology color selections across saves.
                    try
                    {
                        if (!cust.useCustomColor && !ColorsApproximatelyEqual(cust.color, def.uiIconColor))
                        {
                            cust.useCustomColor = true;
                        }
                    }
                    catch { }

                    newApparelDefs.Add(def);
                    newCustomizations[def] = cust;
                    applied++;
                }
            }

            // Apply
            selection.selectedWeaponDef = newWeapon;
            selection.selectedApparelDefs = newApparelDefs;
            selection.apparelCustomizations = newCustomizations;
            // Ensure no invalid or duplicate items remain after applying preset
            SanitizeSelection();

            report = string.Join("\n", issues);
            return (issues.Count == 0 || applied > 0, applied, total);
        }

        private static bool ColorsApproximatelyEqual(Color a, Color b, float eps = 0.0025f)
        {
            return Mathf.Abs(a.r - b.r) <= eps
                && Mathf.Abs(a.g - b.g) <= eps
                && Mathf.Abs(a.b - b.b) <= eps;
        }

        private void ClearPreviewPawnForPresetValidation()
        {
            EnsurePreviewInitialized();
            if (offer?.pawn == null) return;
            try
            {
                if (offer.pawn.apparel != null)
                {
                    foreach (var ap in offer.pawn.apparel.WornApparel.ToList())
                    {
                        if (ap == null || ap.Destroyed) continue;
                        offer.pawn.apparel.Remove(ap);
                    }
                }
                if (offer.pawn.equipment?.Primary != null)
                {
                    var primary = offer.pawn.equipment.Primary;
                    if (primary != null)
                        offer.pawn.equipment.Remove(primary);
                }
                PortraitsCache.SetDirty(offer.pawn);
            }
            catch { }
        }

        public override void PostClose()
        {
            base.PostClose();

            try
            {
                if (!changesConfirmed)
                {
                    // User cancelled - restore original state
                    // remove preview things
                    foreach (var t in previewCreatedThings.ToList())
                    {
                        if (t is Apparel ap && offer.pawn.apparel?.WornApparel.Contains(ap) == true)
                            offer.pawn.apparel.Remove(ap);
                        else if (t is ThingWithComps twc && offer.pawn.equipment != null && offer.pawn.equipment.Primary == twc)
                            offer.pawn.equipment.Remove(twc);
                        // Only destroy if the item is destroyable
                        if (t.def.destroyable)
                            t.Destroy();
                    }
                    previewCreatedThings.Clear();

                    // re-equip originals
                    if (originalPrimaryWeapon != null)
                    {
                        if (offer.pawn.equipment == null)
                            offer.pawn.equipment = new Pawn_EquipmentTracker(offer.pawn);
                        offer.pawn.equipment.AddEquipment(originalPrimaryWeapon);
                    }
                    if (offer.pawn.apparel != null)
                    {
                        foreach (var ap in originalApparel)
                            offer.pawn.apparel.Wear(ap, false);
                    }
                }
                else
                {
                    // User confirmed - keep the preview state as the final state
                    // Just clean up the preview tracking, don't destroy the items
                    previewCreatedThings.Clear();
                }

                // Always update portrait cache when dialog closes
                PortraitsCache.SetDirty(offer.pawn);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimMercenaries] Error in Dialog_MercenaryLoadout.PostClose: {ex.Message}");
            }
        }
    }


    public class Dialog_LoadPreset : Window
    {
        private readonly Dialog_MercenaryLoadout parentDialog;
        private Vector2 scrollPosition = Vector2.zero;
        private string searchFilter = string.Empty;
        private List<LoadoutPreset> filteredPresets = new List<LoadoutPreset>();

        public override Vector2 InitialSize => new Vector2(500f, 400f);

        public Dialog_LoadPreset(Dialog_MercenaryLoadout parentDialog)
        {
            this.parentDialog = parentDialog;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;

            UpdateFilteredPresets();
        }

        private void UpdateFilteredPresets()
        {
            var allPresets = MercenaryLoadoutPresetManager.GetAllPresets();
            if (allPresets == null || allPresets.Count == 0)
            {
                filteredPresets.Clear();
                return;
            }

            string filter = (searchFilter ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(filter))
            {
                filteredPresets = new List<LoadoutPreset>(allPresets);
            }
            else
            {
                filteredPresets = allPresets.Where(p =>
                    p != null &&
                    p.id != null &&
                    p.id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "RimMercenaries_Loadout_LoadPreset".Translate());
            Text.Font = GameFont.Small;

            // Search field
            Rect searchRect = new Rect(0f, 40f, inRect.width, 24f);
            string newSearch = Widgets.TextField(searchRect, searchFilter ?? string.Empty);
            if (newSearch != searchFilter)
            {
                searchFilter = newSearch;
                UpdateFilteredPresets();
            }

            // Preset list
            Rect listRect = new Rect(0f, 70f, inRect.width, inRect.height - 110f);
            Color themedBg = ThemeProvider.GetSectionBackgroundColor();
            Widgets.DrawBoxSolid(listRect, themedBg);
            Widgets.DrawBox(listRect);

            if (filteredPresets.Count == 0)
            {
                string noPresetsText = string.IsNullOrEmpty(searchFilter)
                    ? "RimMercenaries_Loadout_NoPresets".Translate()
                    : "RimMercenaries_Loadout_NoPresetsFound".Translate(searchFilter);
                Widgets.Label(new Rect(listRect.x + 10f, listRect.y + 10f, listRect.width - 20f, 24f), noPresetsText);
            }
            else
            {
                float rowHeight = 28f;
                Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, filteredPresets.Count * rowHeight);
                Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);

                float y = 0f;
                foreach (var preset in filteredPresets)
                {
                    if (preset == null) continue;

                    Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight);

                    // Highlight on hover
                    if (Mouse.IsOver(rowRect))
                    {
                        Widgets.DrawHighlight(rowRect);
                    }

                    // Preset name
                    Rect labelRect = new Rect(10f, y + 4f, viewRect.width - 140f, 20f);
                    Widgets.Label(labelRect, preset.id);

                    // Load button
                    Rect loadButtonRect = new Rect(viewRect.width - 130f, y + 2f, 60f, 24f);
                    if (Widgets.ButtonText(loadButtonRect, "Load"))
                    {
                        parentDialog.LoadPresetById(preset.id);
                        Close();
                    }

                    // Delete button
                    Rect deleteButtonRect = new Rect(viewRect.width - 60f, y + 2f, 50f, 24f);
                    if (Widgets.ButtonText(deleteButtonRect, "Del"))
                    {
                        // Show confirmation dialog before deleting
                        Find.WindowStack.Add(new Dialog_MessageBox(
                            "RimMercenaries_DeletePresetConfirm".Translate(preset.id),
                            "Yes".Translate(),
                            () => {
                                MercenaryLoadoutPresetManager.DeletePreset(preset.id);
                                UpdateFilteredPresets(); // Refresh the list
                            },
                            "No".Translate(),
                            null
                        ));
                    }

                    y += rowHeight;
                }

                Widgets.EndScrollView();
            }

            // Bottom buttons
            float buttonWidth = 100f;
            Rect cancelRect = new Rect(inRect.width / 2f - buttonWidth - 10f, inRect.height - 30f, buttonWidth, 28f);
            Rect closeRect = new Rect(inRect.width / 2f + 10f, inRect.height - 30f, buttonWidth, 28f);

            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Close();
            }

            if (Widgets.ButtonText(closeRect, "Close"))
            {
                Close();
            }
        }
    }


    public class Dialog_SavePreset : Window
    {
        private readonly MercenaryLoadoutSelection selection;
        private string presetName = string.Empty;
        private string statusMessage = string.Empty;
        private Color statusColor = Color.white;

        public override Vector2 InitialSize => new Vector2(400f, 200f);

        public Dialog_SavePreset(MercenaryLoadoutSelection selection)
        {
            this.selection = selection;
            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "RimMercenaries_Loadout_SavePreset".Translate());
            Text.Font = GameFont.Small;

            // Name input field
            Rect nameLabelRect = new Rect(0f, 40f, 80f, 24f);
            Widgets.Label(nameLabelRect, "RimMercenaries_Loadout_PresetId".Translate() + ":");

            Rect nameRect = new Rect(85f, 40f, inRect.width - 85f, 24f);
            presetName = Widgets.TextField(nameRect, presetName ?? string.Empty);

            // Status message
            if (!string.IsNullOrEmpty(statusMessage))
            {
                Rect statusRect = new Rect(0f, 70f, inRect.width, 24f);
                var prevColor = GUI.color;
                GUI.color = statusColor;
                Widgets.Label(statusRect, statusMessage);
                GUI.color = prevColor;
            }

            // Buttons
            float buttonY = string.IsNullOrEmpty(statusMessage) ? 70f : 100f;
            float buttonWidth = 80f;
            float buttonSpacing = 10f;

            Rect saveRect = new Rect(inRect.width / 2f - buttonWidth - buttonSpacing, buttonY, buttonWidth, 28f);
            Rect cancelRect = new Rect(inRect.width / 2f + buttonSpacing, buttonY, buttonWidth, 28f);

            if (Widgets.ButtonText(saveRect, "Save"))
            {
                string trimmed = (presetName ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    statusMessage = "RimMercenaries_Loadout_PresetIdRequired".Translate();
                    statusColor = Color.red;
                }
                else
                {
                    // Check if preset already exists
                    var existing = MercenaryLoadoutPresetManager.FindById(trimmed);
                    if (existing != null)
                    {
                        // Show confirmation dialog
                        Find.WindowStack.Add(new Dialog_MessageBox(
                            "RimMercenaries_Loadout_OverwritePreset".Translate(trimmed),
                            "Yes".Translate(),
                            () => {
                                SavePreset(trimmed);
                                Close();
                            },
                            "No".Translate(),
                            () => {
                                // Just close the confirmation, keep save dialog open
                            }
                        ));
                    }
                    else
                    {
                        SavePreset(trimmed);
                        Close();
                    }
                }
            }

            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Close();
            }
        }

        private void SavePreset(string name)
        {
            try
            {
                MercenaryLoadoutPresetManager.SavePreset(name, selection);
                string msg = "RimMercenaries_Loadout_PresetSaved".Translate(name);
                Messages.Message(msg, MessageTypeDefOf.PositiveEvent);
                statusMessage = msg;
                statusColor = Color.green;
            }
            catch (Exception ex)
            {
                string err = "RimMercenaries_Loadout_PresetSaveFailed".Translate(name, ex.Message);
                Messages.Message(err, MessageTypeDefOf.RejectInput);
                statusMessage = err;
                statusColor = Color.red;
            }
        }
    }


}

