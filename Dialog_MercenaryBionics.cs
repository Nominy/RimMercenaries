using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMercenaries
{
    public class Dialog_MercenaryBionics : Window
    {
        private readonly Pawn pawn;
        private readonly MercenaryLoadoutSelection selection;
        private readonly Action OnSelectionChanged;

        private Vector2 bodyScroll = Vector2.zero;
        private Vector2 optionScroll = Vector2.zero;
        private string searchBuffer = string.Empty;
        private BodyPartRecord selectedPart;
        private string statusMessage = string.Empty;
        private Color statusColor = Color.white;

        private readonly List<BodyPartRecord> bodyParts;
        private List<BionicOption> filteredOptions = new List<BionicOption>();

        public override Vector2 InitialSize => new Vector2(720f, 640f);

        public Dialog_MercenaryBionics(Pawn pawn, MercenaryLoadoutSelection selection, Action onSelectionChanged = null)
        {
            this.pawn = pawn;
            this.selection = selection ?? new MercenaryLoadoutSelection();
            this.OnSelectionChanged = onSelectionChanged;

            BionicsCatalog.EnsureFresh();
            bodyParts = BionicsSelectionUtility.EnumerateBodyParts(pawn).Where(p => p?.def != null).ToList();
            selectedPart = bodyParts.FirstOrDefault();

            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;

            RefreshFilteredOptions();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Color background = ThemeProvider.GetWindowBackgroundColor();
            Widgets.DrawBoxSolid(inRect, background);

            float margin = 10f;
            Rect titleRect = new Rect(inRect.x + margin, inRect.y + margin, inRect.width - margin * 2, 30f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "RimMercenaries_BionicsDialog_Title".Translate(pawn?.LabelShortCap ?? "Pawn"));
            Text.Font = GameFont.Small;

            Rect searchRect = new Rect(inRect.x + margin, titleRect.yMax + 6f, inRect.width - margin * 2, 24f);
            string newSearch = Widgets.TextField(searchRect, searchBuffer ?? string.Empty);
            if (newSearch != searchBuffer)
            {
                searchBuffer = newSearch;
                RefreshFilteredOptions();
            }

            float paneMargin = 6f;
            float leftWidth = (inRect.width - margin * 2 - paneMargin) * 0.45f;
            float rightWidth = (inRect.width - margin * 2 - paneMargin) - leftWidth;
            float top = searchRect.yMax + 6f;
            float height = inRect.height - top - 70f;

            Rect leftRect = new Rect(inRect.x + margin, top, leftWidth, height);
            Rect rightRect = new Rect(leftRect.xMax + paneMargin, top, rightWidth, height);

            DrawBodyPartTree(leftRect);
            DrawOptionsList(rightRect);

            DrawFooter(new Rect(inRect.x + margin, inRect.yMax - 60f, inRect.width - margin * 2, 50f));
        }

        private void DrawBodyPartTree(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ThemeProvider.GetSectionBackgroundColor());
            Widgets.DrawBox(rect);

            Rect labelRect = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 24f);
            Widgets.Label(labelRect, "RimMercenaries_BionicsDialog_Parts".Translate());

            Rect outRect = new Rect(rect.x + 6f, labelRect.yMax + 4f, rect.width - 12f, rect.height - 40f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, bodyParts.Count * 24f + 10f);
            Widgets.BeginScrollView(outRect, ref bodyScroll, viewRect);

            float y = 0f;
            foreach (var part in bodyParts)
            {
                bool isSelected = selectedPart == part;
                Rect row = new Rect(0f, y, viewRect.width, 24f);

                if (isSelected)
                {
                    Widgets.DrawHighlightSelected(row);
                }
                else if (Mouse.IsOver(row))
                {
                    Widgets.DrawHighlight(row);
                }

                string label = part.LabelCap + " (" + part.def.LabelCap + ")";
                Widgets.Label(row, label);

                if (Widgets.ButtonInvisible(row))
                {
                    selectedPart = part;
                    RefreshFilteredOptions();
                }

                y += 24f;
            }

            Widgets.EndScrollView();
        }

        private void DrawOptionsList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ThemeProvider.GetSectionBackgroundColor());
            Widgets.DrawBox(rect);

            Rect headerRect = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 24f);
            Widgets.Label(headerRect, "RimMercenaries_BionicsDialog_Options".Translate(selectedPart?.LabelCap ?? ""));

            Rect outRect = new Rect(rect.x + 6f, headerRect.yMax + 4f, rect.width - 12f, rect.height - 40f);
            float rowHeight = 54f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, filteredOptions.Count * (rowHeight + 4f));
            Widgets.BeginScrollView(outRect, ref optionScroll, viewRect);

            float y = 0f;
            foreach (var option in filteredOptions)
            {
                Rect row = new Rect(0f, y, viewRect.width, rowHeight);
                DrawOptionRow(row, option);
                y += rowHeight + 4f;
            }

            Widgets.EndScrollView();
        }

        private void DrawOptionRow(Rect row, BionicOption option)
        {
            Widgets.DrawBoxSolid(row, new Color(0.15f, 0.15f, 0.15f, 0.35f));
            Widgets.DrawBox(row);

            Rect labelRect = new Rect(row.x + 6f, row.y + 4f, row.width - 12f, 20f);
            Widgets.Label(labelRect, option.Hediff.LabelCap);

            Rect detailRect = new Rect(row.x + 6f, labelRect.yMax, row.width - 12f, 20f);
            string detail = option.ImplantThing != null ? option.ImplantThing.LabelCap : "RimMercenaries_BionicsDialog_NoImplant".Translate();
            var settings = RimMercenariesMod.ActiveSettings;
            int displayValue = (settings != null && settings.bionicsPricingMode == BionicsPricingMode.Static)
                ? Mathf.Max(0, settings.bionicsStaticPrice)
                : Mathf.Max(0, Mathf.RoundToInt(option.CalculatedMarketValue));
            detail += " | " + "RimMercenaries_BionicsDialog_Value".Translate(displayValue.ToString("N0"));
            Widgets.Label(detailRect, detail);

            Rect buttonRect = new Rect(row.xMax - 120f, row.y + (row.height - 28f) / 2f, 110f, 28f);
            bool alreadySelected = selection.selectedBionics.Any(b => b.hediffDefName == option.Hediff.defName && b.bodyPartPath == BionicsSelectionUtility.BuildPath(selectedPart) && b.bodyPartIndex == BionicsSelectionUtility.GetIndexForPart(pawn, selectedPart));
            string buttonLabel = alreadySelected ? "RimMercenaries_BionicsDialog_Remove".Translate() : "RimMercenaries_BionicsDialog_Apply".Translate();
            if (Widgets.ButtonText(buttonRect, buttonLabel))
            {
                if (alreadySelected)
                {
                    RemoveBionic(option);
                }
                else
                {
                    ApplyBionic(option);
                }
            }
        }

        private void DrawFooter(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ThemeProvider.GetSectionBackgroundColor());
            Widgets.DrawBox(rect);

            if (!string.IsNullOrEmpty(statusMessage))
            {
                var prev = GUI.color;
                GUI.color = statusColor;
                Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 200f, 34f), statusMessage);
                GUI.color = prev;
            }

            float buttonWidth = 140f;
            Rect acceptRect = new Rect(rect.xMax - buttonWidth, rect.y + 10f, buttonWidth, 30f);
            if (Widgets.ButtonText(acceptRect, "Accept".Translate()))
            {
                OnSelectionChanged?.Invoke();
                Close();
            }

            Rect cancelRect = new Rect(acceptRect.x - buttonWidth - 10f, rect.y + 10f, buttonWidth, 30f);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }

        private void ApplyBionic(BionicOption option)
        {
            if (selectedPart == null || option == null)
            {
                ShowStatus("RimMercenaries_BionicsDialog_NoPart".Translate(), Color.red);
                return;
            }

            string path = BionicsSelectionUtility.BuildPath(selectedPart);
            int index = BionicsSelectionUtility.GetIndexForPart(pawn, selectedPart);

            selection.selectedBionics.RemoveAll(b => b.bodyPartPath == path && b.bodyPartIndex == index);
            selection.selectedBionics.Add(new SelectedBionic
            {
                bodyPartPath = path,
                bodyPartIndex = index,
                hediffDefName = option.Hediff.defName,
                recipeDefName = option.Recipe?.defName,
                implantThingDefName = option.ImplantThing?.defName
            });

            ShowStatus("RimMercenaries_BionicsDialog_Applied".Translate(option.Hediff.LabelCap, selectedPart.LabelCap), Color.green);
            OnSelectionChanged?.Invoke();
        }

        private void RemoveBionic(BionicOption option)
        {
            if (selectedPart == null) return;
            string path = BionicsSelectionUtility.BuildPath(selectedPart);
            int index = BionicsSelectionUtility.GetIndexForPart(pawn, selectedPart);
            int removed = selection.selectedBionics.RemoveAll(b => b.bodyPartPath == path && b.bodyPartIndex == index && b.hediffDefName == option?.Hediff?.defName);
            if (removed > 0)
            {
                ShowStatus("RimMercenaries_BionicsDialog_Removed".Translate(option?.Hediff?.LabelCap ?? ""), Color.yellow);
                OnSelectionChanged?.Invoke();
            }
        }

        private void RefreshFilteredOptions()
        {
            filteredOptions.Clear();
            if (selectedPart == null)
            {
                statusMessage = string.Empty;
                return;
            }

            var options = BionicsCatalog.GetOptionsForPart(selectedPart).ToList();
            if (!string.IsNullOrEmpty(searchBuffer))
            {
                options = options.Where(o => o.Hediff.LabelCap.ToString().IndexOf(searchBuffer, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             (o.ImplantThing != null && o.ImplantThing.LabelCap.ToString().IndexOf(searchBuffer, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            }

            filteredOptions = options.OrderBy(o => o.Hediff.label).ToList();
        }

        private void ShowStatus(string message, Color color)
        {
            statusMessage = message;
            statusColor = color;
        }
    }
}

