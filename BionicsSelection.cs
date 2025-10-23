using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimMercenaries
{
    public static class BionicsSelectionUtility
    {
        private static readonly Dictionary<BodyPartRecord, string> PathCache = new Dictionary<BodyPartRecord, string>();

        public static string BuildPath(BodyPartRecord record)
        {
            if (record == null) return string.Empty;
            if (PathCache.TryGetValue(record, out var cached))
            {
                return cached;
            }

            var segments = new List<string>();
            var cursor = record;
            while (cursor != null)
            {
                string seg = cursor.def?.defName;
                if (string.IsNullOrEmpty(seg))
                {
                    seg = cursor.def?.label ?? "Unknown";
                }
                segments.Add(seg);
                cursor = cursor.parent;
            }
            segments.Reverse();
            var path = string.Join("/", segments);
            PathCache[record] = path;
            return path;
        }

        public static int GetIndexForPart(Pawn pawn, BodyPartRecord record)
        {
            if (pawn?.RaceProps?.body == null || record == null) return 0;
            string targetPath = BuildPath(record);
            int index = 0;
            foreach (var part in pawn.RaceProps.body.AllParts)
            {
                if (string.Equals(BuildPath(part), targetPath, StringComparison.Ordinal))
                {
                    if (part == record)
                    {
                        return index;
                    }
                    index++;
                }
            }
            return 0;
        }

        public static BodyPartRecord FindPartByPath(Pawn pawn, string path, int index)
        {
            if (pawn?.RaceProps?.body == null || string.IsNullOrEmpty(path)) return null;
            int current = 0;
            foreach (var part in pawn.RaceProps.body.AllParts)
            {
                if (string.Equals(BuildPath(part), path, StringComparison.Ordinal))
                {
                    if (current == index)
                    {
                        return part;
                    }
                    current++;
                }
            }
            return null;
        }

        public static IEnumerable<BodyPartRecord> EnumerateBodyParts(Pawn pawn)
        {
            if (pawn?.RaceProps?.body == null) yield break;
            foreach (var part in pawn.RaceProps.body.AllParts)
            {
                yield return part;
            }
        }

        public static void ClearCache()
        {
            PathCache.Clear();
        }
    }
}

