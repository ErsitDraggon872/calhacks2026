using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Where a free build lands. Near an obstacle with a <see cref="BuildSlot"/> it snaps into the slot; anywhere else
    /// it stands just in front of B, on the side B faces, at B's feet and depth, then moves out of level geometry
    /// (<see cref="ClearSpot"/>). Pure, so EditMode tests cover it.
    /// </summary>
    public static class InventionPlacement
    {
        /// <summary>Clear space in metres between B's collider and the invention's near edge.</summary>
        public const float Gap = 0.4f;

        /// <summary>How far a blocked free build may move (in each of depth and X) looking for room, in metres.</summary>
        public const float MaxShift = 4f;

        /// <summary>Step between the spots <see cref="ClearSpot"/> tries, in metres.</summary>
        public const float ShiftStep = 0.25f;

        /// <summary>Overlap smaller than this doesn't count, so a build resting on the floor or touching a wall fits.</summary>
        public const float Touch = 0.02f;

        /// <summary>
        /// Bottom-centre of an invention <paramref name="inventionWidth"/> wide, placed beside a player whose feet are at
        /// <paramref name="feet"/>, facing <paramref name="facingSign"/> (+1 right, -1 left) with collider <paramref name="playerRadius"/>.
        /// </summary>
        public static Vector3 BottomCentre(Vector3 feet, float facingSign, float playerRadius, float inventionWidth)
        {
            var sign = facingSign < 0f ? -1f : 1f;
            var offset = Mathf.Max(0f, playerRadius) + Gap + Mathf.Max(0f, inventionWidth) * 0.5f;
            return new Vector3(feet.x + sign * offset, feet.y, feet.z);
        }

        /// <summary>
        /// Bottom-centre of a drawing <paramref name="size"/> fitted into a slot whose box (<paramref name="box"/>, the
        /// ghost) stands on <paramref name="slotBottom"/>. It stands on the slot's floor, pushed to the side
        /// <paramref name="align"/> names (+1 right edge, -1 left edge, 0 centred), so a narrow drawing still touches the
        /// obstacle the slot is against.
        /// </summary>
        public static Vector3 SlotBottomCentre(Vector3 slotBottom, Vector2 box, Vector2 size, float align)
        {
            var slack = Mathf.Max(0f, box.x - size.x) * 0.5f;
            return slotBottom + Vector3.right * (Mathf.Clamp(align, -1f, 1f) * slack);
        }

        /// <summary>
        /// Bottom-centre of an invention <paramref name="width"/> wide standing on the ledge B is on, beside a drop
        /// <paramref name="dropWidth"/> wide whose middle, at ledge height and the path's depth, is <paramref name="drop"/>:
        /// its far edge at the drop's edge, centred on the path (which is barely deeper than a build, so centred on B it
        /// pokes into the level's edges and gets pushed out of B's reach), so B can ride it across instead of it falling
        /// in. <paramref name="away"/> is the way from the drop onto that ledge (+1 right, -1 left).
        /// </summary>
        public static Vector3 LedgeBottomCentre(Vector3 feet, Vector3 drop, float dropWidth, float width, out float away)
        {
            away = feet.x < drop.x ? -1f : 1f;
            var edge = drop.x + away * Mathf.Max(0f, dropWidth) * 0.5f;
            return new Vector3(edge + away * Mathf.Max(0f, width) * 0.5f, drop.y, drop.z);
        }

        /// <summary>Step between the ground probes <see cref="DropEdge"/> makes, in metres.</summary>
        public const float EdgeStep = 0.1f;

        /// <summary>
        /// The last X with ground under it going from <paramref name="fromX"/> (B's feet) toward <paramref name="sign"/>
        /// for up to <paramref name="reach"/> metres: where the ledge B stands on ends, or null if the ground runs the whole
        /// way. A build standing in front of B is pulled back to it so it never hangs out over a drop.
        /// </summary>
        public static float? DropEdge(float fromX, float sign, float reach, System.Func<float, bool> hasGround)
        {
            sign = sign < 0f ? -1f : 1f;
            var steps = Mathf.CeilToInt(Mathf.Max(0f, reach) / EdgeStep);
            for (var i = 0; i <= steps; i++)
            {
                if (!hasGround(fromX + sign * Mathf.Min(i * EdgeStep, reach))) return fromX + sign * Mathf.Max(0, i - 1) * EdgeStep;
            }
            return null;
        }

        /// <summary>
        /// Largest size with the drawing's aspect (<paramref name="width"/> / <paramref name="height"/> pixels) that fits
        /// inside <paramref name="box"/>. How big it was drawn doesn't matter, only its shape.
        /// </summary>
        public static Vector2 FitInside(int width, int height, Vector2 box)
        {
            var aspect = height > 0 && width > 0 ? (float)width / height : 1f;
            var w = box.x;
            var h = w / aspect;
            if (h > box.y)
            {
                h = box.y;
                w = h * aspect;
            }
            return new Vector2(w, h);
        }

        /// <summary>World bounds of an invention <paramref name="size"/> standing on <paramref name="bottomCentre"/>, <paramref name="depth"/> deep.</summary>
        public static Bounds BuildBounds(Vector3 bottomCentre, Vector2 size, float depth) =>
            new Bounds(bottomCentre + Vector3.up * (size.y * 0.5f), new Vector3(size.x, size.y, depth));

        /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> overlap by more than <see cref="Touch"/> on every axis.</summary>
        public static bool Overlaps(Bounds a, Bounds b)
        {
            for (var i = 0; i < 3; i++)
            {
                if (Mathf.Min(a.max[i], b.max[i]) - Mathf.Max(a.min[i], b.min[i]) <= Touch) return false;
            }
            return true;
        }

        /// <summary>
        /// The smallest move of <paramref name="build"/> that clears every box in <paramref name="obstacles"/>: toward the
        /// camera (−Z, the open front of the level) or further from B (along <paramref name="facingSign"/>), never back
        /// toward B or up. Tries the nearest spots first, preferring depth on a tie. With <paramref name="standable"/>, a
        /// spot it rejects (nothing to stand on: out over a drop, or past the level's front edge) doesn't count either.
        /// False if nothing within <see cref="MaxShift"/> is clear.
        /// </summary>
        public static bool ClearSpot(Bounds build, IList<Bounds> obstacles, float facingSign, out Vector3 shift,
            System.Func<Bounds, bool> standable = null)
        {
            var sign = facingSign < 0f ? -1f : 1f;
            var steps = Mathf.RoundToInt(MaxShift / ShiftStep);
            for (var total = 0; total <= steps * 2; total++)
            {
                for (var x = Mathf.Max(0, total - steps); x <= Mathf.Min(total, steps); x++)
                {
                    shift = new Vector3(sign * x * ShiftStep, 0f, -(total - x) * ShiftStep);
                    var moved = new Bounds(build.center + shift, build.size);
                    if (!AnyOverlap(moved, obstacles) && (standable == null || standable(moved))) return true;
                }
            }
            shift = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Depth (Z) centre for <paramref name="build"/> on a path between a wall in front and one behind (the start path
        /// is exactly a build deep): B's depth (<paramref name="feetZ"/>) when it fits there, else moved just enough to sit
        /// between the nearest boxes in front of and behind B that it would overlap. Boxes spanning B's depth (walls
        /// along X) are left to <see cref="ClearSpot"/>.
        /// </summary>
        public static float FitLane(Bounds build, float feetZ, IList<Bounds> obstacles)
        {
            var front = float.NegativeInfinity;
            var back = float.PositiveInfinity;
            foreach (var obstacle in obstacles)
            {
                if (!OverlapsOn(build, obstacle, 0) || !OverlapsOn(build, obstacle, 1)) continue;
                if (obstacle.max.z <= feetZ) front = Mathf.Max(front, obstacle.max.z);
                else if (obstacle.min.z >= feetZ) back = Mathf.Min(back, obstacle.min.z);
            }
            var half = build.extents.z;
            // Too narrow: centred is the least overlap, and ClearSpot reports it blocked.
            if (back - front < half * 2f) return (front + back) * 0.5f;
            return Mathf.Clamp(build.center.z, front + half, back - half);
        }

        static bool OverlapsOn(Bounds a, Bounds b, int axis) =>
            Mathf.Min(a.max[axis], b.max[axis]) - Mathf.Max(a.min[axis], b.min[axis]) > Touch;

        /// <summary>
        /// Height to lift a player with bounds <paramref name="player"/> to when a build appears around them: the top of
        /// the highest build box they overlap, or null if they overlap none.
        /// </summary>
        public static float? LiftTo(Bounds player, IEnumerable<Bounds> boxes)
        {
            float? top = null;
            foreach (var box in boxes)
            {
                if (Overlaps(player, box) && (top == null || box.max.y > top)) top = box.max.y;
            }
            return top;
        }

        static bool AnyOverlap(Bounds build, IList<Bounds> obstacles)
        {
            foreach (var obstacle in obstacles)
            {
                if (Overlaps(build, obstacle)) return true;
            }
            return false;
        }
    }
}
