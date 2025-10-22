using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace RimMercenaries
{
	public static class ReflectionExtensions
	{
		public static ThingComp GetCompByReflectedType(this ThingWithComps thing, string typeName)
		{
			if (thing == null || string.IsNullOrEmpty(typeName)) return null;
			try
			{
				var type = AccessTools.TypeByName(typeName);
				if (type == null)
				{
					// Fallback: scan assemblies
					type = AppDomain.CurrentDomain.GetAssemblies()
						.Select(a => a.GetType(typeName, false))
						.FirstOrDefault(t => t != null);
				}

				if (type == null) return null;
				if (thing.AllComps == null) return null;
				foreach (var comp in thing.AllComps)
				{
					if (comp != null && type.IsInstanceOfType(comp))
						return comp;
				}
			}
			catch { }
			return null;
		}
	}
}


