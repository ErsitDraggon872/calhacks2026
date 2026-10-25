using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CalHacks
{
    public enum LabelOutcome
    {
        Solve,
        CatReaction,
        Decor,
    }

    /// <summary>Pure label and socket-selection rules, kept free of scene state for EditMode tests.</summary>
    public static class SocketLogic
    {
        public const string Unknown = "unknown";
        public const string Cat = "cat";
        /// <summary>The backend's name for a drawing it couldn't read.</summary>
        public const string MysteryShape = "mystery shape";
        static readonly string[] BridgeWords = { "bridge", "plank", "board", "log" };

        public static string Normalize(string label) =>
            string.IsNullOrWhiteSpace(label) ? Unknown : label.Trim().ToLowerInvariant();

        public static LabelOutcome Resolve(string label, IReadOnlyList<string> acceptedLabels)
        {
            var normalized = Normalize(label);
            if (acceptedLabels != null)
            {
                foreach (var accepted in acceptedLabels)
                {
                    if (Normalize(accepted) == normalized && normalized != Unknown) return LabelOutcome.Solve;
                }
            }
            return normalized == Cat ? LabelOutcome.CatReaction : LabelOutcome.Decor;
        }

        /// <summary>
        /// Whether a drawing called <paramref name="label"/> spans the gap: a bridge (or plank, board, log), or something
        /// nobody could name. Anything else (a rocket, a cat) is built as itself instead.
        /// </summary>
        public static bool IsBridge(string label)
        {
            var name = Normalize(label);
            if (name == Unknown || name == MysteryShape) return true;
            var words = System.Text.RegularExpressions.Regex.Split(name, @"[^a-z0-9]+");
            return words.Any(word => BridgeWords.Contains(word));
        }

        /// <summary>
        /// Attributes first: any of <paramref name="solvingTraits"/> solves (a torch lights the cave without being called
        /// "light"). Otherwise the name is matched against <paramref name="acceptedLabels"/> as before. A drawing the
        /// backend rejected never solves.
        /// </summary>
        public static LabelOutcome Resolve(InventionSpec spec, IReadOnlyList<Trait> solvingTraits, IReadOnlyList<string> acceptedLabels)
        {
            if (spec == null) return Resolve(Unknown, acceptedLabels);
            if (spec.Rejected) return LabelOutcome.Decor;
            if (solvingTraits != null && solvingTraits.Any(spec.Has)) return LabelOutcome.Solve;
            return Resolve(spec.Name, acceptedLabels);
        }

        /// <summary>Index of the nearest unsolved point within range on the XZ plane, or -1.</summary>
        public static int FindNearest(IReadOnlyList<Vector3> points, IReadOnlyList<bool> solved, Vector3 from, float range) =>
            FindNearest(points, solved, null, from, range, float.PositiveInfinity);

        /// <summary>
        /// Index of the nearest unsolved point on the XZ plane, or -1. Each point must be within its own range
        /// (<paramref name="ranges"/>, where 0 or a missing list means <paramref name="range"/>) and at most
        /// <paramref name="maxRise"/> above or below <paramref name="from"/>, so a socket on the floor doesn't take a
        /// build made on a ledge above it.
        /// </summary>
        public static int FindNearest(IReadOnlyList<Vector3> points, IReadOnlyList<bool> solved, IReadOnlyList<float> ranges,
            Vector3 from, float range, float maxRise)
        {
            var best = -1;
            var bestSqr = float.PositiveInfinity;
            for (var i = 0; i < points.Count; i++)
            {
                if (solved != null && solved[i]) continue;
                if (Mathf.Abs(points[i].y - from.y) > maxRise) continue;
                var reach = ranges != null && ranges[i] > 0f ? Mathf.Min(ranges[i], range) : range;
                var dx = points[i].x - from.x;
                var dz = points[i].z - from.z;
                var sqr = dx * dx + dz * dz;
                if (sqr <= reach * reach && sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }
            return best;
        }
    }
}
