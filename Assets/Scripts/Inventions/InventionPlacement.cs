using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Where a free build lands until the Approve/placement step exists: just in front of B, on the side B faces,
    /// standing at B's feet and depth, never overlapping B. Pure, so EditMode tests cover it.
    /// </summary>
    public static class InventionPlacement
    {
        /// <summary>Clear space in metres between B's collider and the invention's near edge.</summary>
        public const float Gap = 0.4f;

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
        /// Bottom-centre of the build ghost: the largest invention, <paramref name="box"/>. Every drawing is fitted
        /// inside the box and placed with its near edge at the same spot, so whatever is drawn lands inside the ghost.
        /// </summary>
        public static Vector3 GhostBottomCentre(Vector3 feet, float facingSign, float playerRadius, Vector2 box) =>
            BottomCentre(feet, facingSign, playerRadius, box.x);

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
    }
}
