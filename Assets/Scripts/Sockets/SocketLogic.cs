using System.Collections.Generic;
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

        /// <summary>Index of the nearest unsolved point within range on the XZ plane, or -1.</summary>
        public static int FindNearest(IReadOnlyList<Vector3> points, IReadOnlyList<bool> solved, Vector3 from, float range)
        {
            var best = -1;
            var bestSqr = range * range;
            for (var i = 0; i < points.Count; i++)
            {
                if (solved != null && solved[i]) continue;
                var dx = points[i].x - from.x;
                var dz = points[i].z - from.z;
                var sqr = dx * dx + dz * dz;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }
            return best;
        }
    }
}
