using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// A free-built drawing in the world: flat to look at, thick to stand on. The visual is a camera-facing
    /// <see cref="Cutout"/>; a "Solid" child gets one BoxCollider per silhouette box, extruded along Z and centred on
    /// the drawing's depth. Every invention is a Support for now (no labels yet).
    /// </summary>
    public class Invention : MonoBehaviour
    {
        public Cutout Cutout { get; private set; }
        public Vector2 Size { get; private set; }
        public int BoxCount { get; private set; }

        /// <summary>
        /// Builds an invention standing on <paramref name="bottomCentre"/>. The drawing is fitted inside
        /// <paramref name="size"/> keeping its aspect, and its colliders are <paramref name="depth"/> metres deep.
        /// Takes ownership of <paramref name="paper"/> (a <see cref="PaperTexture.MakePaper"/> result, so it's readable).
        /// </summary>
        public static Invention Create(Texture2D paper, Vector2 size, Vector3 bottomCentre, float depth, Material mat,
            Transform parent, float cell = 0.15f)
        {
            var go = new GameObject("Invention");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(bottomCentre, Quaternion.identity);

            var invention = go.AddComponent<Invention>();
            invention.Size = InventionPlacement.FitInside(paper.width, paper.height, size);
            // The paper alpha is what's drawn, so what you see is what you stand on.
            var rects = SilhouetteBoxes.Build(paper.GetPixels32(), paper.width, paper.height, invention.Size, cell);
            invention.Cutout = Cutout.Create(paper, true, mat, bottomCentre, 0f, invention.Size, go.transform);
            invention.BoxCount = AddColliders(go.transform, rects, Mathf.Max(0.1f, depth));
            Debug.Log($"[Invention] {invention.Size.x:0.00} x {invention.Size.y:0.00} m, {invention.BoxCount} boxes, depth {depth:0.0} m");
            return invention;
        }

        static int AddColliders(Transform root, List<Rect> rects, float depth)
        {
            var solid = new GameObject("Solid");
            solid.transform.SetParent(root, false);
            foreach (var r in rects)
            {
                var box = solid.AddComponent<BoxCollider>();
                box.center = new Vector3(r.center.x, r.center.y, 0f);
                box.size = new Vector3(r.width, r.height, depth);
            }
            return rects.Count;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            foreach (var box in GetComponentsInChildren<BoxCollider>())
            {
                Gizmos.matrix = box.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
