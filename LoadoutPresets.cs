using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMercenaries
{
	public class LoadoutApparelPreset : IExposable
	{
		public string defName;
		public string materialDefName;
		public QualityCategory quality = QualityCategory.Normal;
		public int hitPoints = -1;
		public float colorR = 1f, colorG = 1f, colorB = 1f, colorA = 1f;
		public bool useCustomColor = false;
		public string styleDefName = null;

		public void ExposeData()
		{
			Scribe_Values.Look(ref defName, "defName");
			Scribe_Values.Look(ref materialDefName, "materialDefName");
			Scribe_Values.Look(ref quality, "quality", QualityCategory.Normal);
			Scribe_Values.Look(ref hitPoints, "hitPoints", -1);
			Scribe_Values.Look(ref colorR, "colorR", 1f);
			Scribe_Values.Look(ref colorG, "colorG", 1f);
			Scribe_Values.Look(ref colorB, "colorB", 1f);
			Scribe_Values.Look(ref colorA, "colorA", 1f);
			Scribe_Values.Look(ref useCustomColor, "useCustomColor", false);
			Scribe_Values.Look(ref styleDefName, "styleDefName");
		}
	}

	public class LoadoutPreset : IExposable
	{
		public string id;
		public string weaponDefName;
		public List<LoadoutApparelPreset> apparels = new List<LoadoutApparelPreset>();

		public void ExposeData()
		{
			Scribe_Values.Look(ref id, "id");
			Scribe_Values.Look(ref weaponDefName, "weaponDefName");
			Scribe_Collections.Look(ref apparels, "apparels", LookMode.Deep);
		}
	}

	public static class MercenaryLoadoutPresetManager
	{
		private static List<LoadoutPreset> cachedPresets;

		private class LoadoutPresetStore : IExposable
		{
			public List<LoadoutPreset> presets = new List<LoadoutPreset>();
			public void ExposeData()
			{
				Scribe_Collections.Look(ref presets, "presets", LookMode.Deep);
			}
		}

		private static string ConfigFilePath
		{
			get
			{
				try
				{
					string baseFolder = GenFilePaths.ConfigFolderPath;
					if (string.IsNullOrEmpty(baseFolder))
					{
						baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Low", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
					}
					Directory.CreateDirectory(baseFolder);
					return Path.Combine(baseFolder, "RimMercenaries_LoadoutPresets.xml");
				}
				catch
				{
					string fallbackFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RimWorld", "Config");
					Directory.CreateDirectory(fallbackFolder);
					return Path.Combine(fallbackFolder, "RimMercenaries_LoadoutPresets.xml");
				}
			}
		}

		// Legacy per-file folder support (for migration/fallback)
		private static string LegacyFolderPath
		{
			get
			{
				try
				{
					string baseFolder = GenFilePaths.SaveDataFolderPath;
					if (string.IsNullOrEmpty(baseFolder))
						baseFolder = GenFilePaths.ConfigFolderPath;
					string folder = Path.Combine(baseFolder, "RimMercenaries", "LoadoutPresets");
					Directory.CreateDirectory(folder);
					return folder;
				}
				catch
				{
					string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RimWorld", "RimMercenaries", "LoadoutPresets");
					Directory.CreateDirectory(fallback);
					return fallback;
				}
			}
		}

		private static string SanitizeFileName(string name)
		{
			var invalid = Path.GetInvalidFileNameChars();
			var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
			return new string(chars);
		}

		private static void EnsureLoaded()
		{
			if (cachedPresets != null) return;
			cachedPresets = new List<LoadoutPreset>();
			try
			{
				// Preferred: single file in config folder
				if (File.Exists(ConfigFilePath))
				{
					LoadoutPresetStore store = null;
					try
					{
						Scribe.loader.InitLoading(ConfigFilePath);
						Scribe_Deep.Look(ref store, "store");
						Scribe.loader.FinalizeLoading();
					}
					catch (Exception ex)
					{
						Log.Warning($"[RimMercenaries] Failed to load presets from {ConfigFilePath}: {ex.Message}");
						try { Scribe.loader.FinalizeLoading(); } catch { }
					}
					if (store?.presets != null)
					{
						cachedPresets.AddRange(store.presets.Where(p => p != null && !string.IsNullOrEmpty(p.id)));
					}
				}
				else
				{
					// Fallback: legacy folder with per-preset files
					if (Directory.Exists(LegacyFolderPath))
					{
						foreach (var file in Directory.GetFiles(LegacyFolderPath, "*.xml"))
						{
							try
							{
								LoadoutPreset preset = null;
								Scribe.loader.InitLoading(file);
								Scribe_Deep.Look(ref preset, "preset");
								Scribe.loader.FinalizeLoading();
								if (preset != null && !string.IsNullOrEmpty(preset.id))
								{
									cachedPresets.Add(preset);
								}
							}
							catch (Exception ex)
							{
								Log.Warning($"[RimMercenaries] Failed to load preset file {file}: {ex.Message}");
								try { Scribe.loader.FinalizeLoading(); } catch { }
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error($"[RimMercenaries] Error loading presets: {ex.Message}");
			}
		}

		public static List<LoadoutPreset> GetAllPresets()
		{
			EnsureLoaded();
			return cachedPresets.OrderBy(p => p.id).ToList();
		}

		public static LoadoutPreset FindById(string id)
		{
			EnsureLoaded();
			return cachedPresets.FirstOrDefault(p => string.Equals(p.id, id, StringComparison.OrdinalIgnoreCase));
		}

		public static void DeletePreset(string id)
		{
			EnsureLoaded();
			var existing = FindById(id);
			if (existing != null)
			{
				cachedPresets.Remove(existing);
			}
			SaveAll();
		}

		public static void SavePreset(string id, MercenaryLoadoutSelection selection)
		{
			if (selection == null) return;
			EnsureLoaded();

			var preset = BuildPresetFromSelection(id, selection);

			var existing = FindById(id);
			if (existing != null)
			{
				cachedPresets.Remove(existing);
			}
			cachedPresets.Add(preset);

			SaveAll();
		}

		private static void SaveAll()
		{
			try
			{
				var store = new LoadoutPresetStore { presets = cachedPresets.OrderBy(p => p.id).ToList() };
				string file = ConfigFilePath;
				Directory.CreateDirectory(Path.GetDirectoryName(file));
				Scribe.saver.InitSaving(file, "RimMercenariesLoadoutPresets");
				Scribe_Deep.Look(ref store, "store");
				Scribe.saver.FinalizeSaving();
			}
			catch (Exception ex)
			{
				Log.Error($"[RimMercenaries] Failed to save presets: {ex.Message}");
				try { Scribe.saver.FinalizeSaving(); } catch { }
			}
		}

		private static LoadoutPreset BuildPresetFromSelection(string id, MercenaryLoadoutSelection selection)
		{
			var preset = new LoadoutPreset();
			preset.id = id;
			preset.weaponDefName = selection.selectedWeaponDef?.defName;
			preset.apparels = new List<LoadoutApparelPreset>();

			if (selection.selectedApparelDefs != null)
			{
				var seen = new HashSet<ThingDef>();
				foreach (var def in selection.selectedApparelDefs)
				{
					if (def == null || def.apparel == null) continue;
					if (!seen.Add(def)) continue;

					var ap = new LoadoutApparelPreset();
					ap.defName = def.defName;

					ApparelCustomizationData cust = null;
					selection.apparelCustomizations?.TryGetValue(def, out cust);
					ThingDef stuff = cust?.materialDef ?? GenStuff.DefaultStuffFor(def);
					ap.materialDefName = stuff?.defName;

					ap.quality = cust?.quality ?? QualityCategory.Normal;
					ap.hitPoints = cust?.hitPoints ?? -1;

					Color color = cust?.color ?? def.uiIconColor;
					ap.colorR = color.r;
					ap.colorG = color.g;
					ap.colorB = color.b;
					ap.colorA = color.a == 0 ? 1f : color.a;
					ap.useCustomColor = cust?.useCustomColor ?? false;
					ap.styleDefName = cust?.styleDef?.defName;

					preset.apparels.Add(ap);
				}
			}

			return preset;
		}
	}
}


