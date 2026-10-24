using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class InventionPlacementTests
    {
        const float Radius = 0.4f;
        static readonly Vector3 Feet = new Vector3(20f, 1.3f, 1.5f);

        [Test]
        public void FacingRightPlacesToTheRight()
        {
            var p = InventionPlacement.BottomCentre(Feet, 1f, Radius, 3f);
            Assert.AreEqual(Feet.x + Radius + InventionPlacement.Gap + 1.5f, p.x, 1e-4f);
        }

        [Test]
        public void FacingLeftPlacesToTheLeft()
        {
            var p = InventionPlacement.BottomCentre(Feet, -1f, Radius, 3f);
            Assert.AreEqual(Feet.x - Radius - InventionPlacement.Gap - 1.5f, p.x, 1e-4f);
        }

        [Test]
        public void StandsAtFeetHeightAndDepth()
        {
            foreach (var facing in new[] { 1f, -1f })
            {
                var p = InventionPlacement.BottomCentre(Feet, facing, Radius, 2.2f);
                Assert.AreEqual(Feet.y, p.y, 1e-4f);
                Assert.AreEqual(Feet.z, p.z, 1e-4f);
            }
        }

        [Test]
        public void NeverOverlapsThePlayer()
        {
            foreach (var facing in new[] { 1f, -1f, 0f })
            foreach (var width in new[] { 0.1f, 1f, 3f, 6f })
            {
                var p = InventionPlacement.BottomCentre(Feet, facing, Radius, width);
                var nearEdge = Mathf.Abs(p.x - Feet.x) - width * 0.5f;
                Assert.GreaterOrEqual(nearEdge, Radius + InventionPlacement.Gap - 1e-4f, $"facing {facing}, width {width}");
            }
        }

        [Test]
        public void FitKeepsAspectInsideTheBox()
        {
            var box = new Vector2(3f, 2.5f);
            var wide = InventionPlacement.FitInside(400, 100, box);
            Assert.AreEqual(3f, wide.x, 1e-4f);
            Assert.AreEqual(0.75f, wide.y, 1e-4f);
            var tall = InventionPlacement.FitInside(100, 400, box);
            Assert.AreEqual(2.5f, tall.y, 1e-4f);
            Assert.AreEqual(0.625f, tall.x, 1e-4f);
            // Drawn size doesn't matter, only shape.
            Assert.AreEqual(InventionPlacement.FitInside(50, 40, box), InventionPlacement.FitInside(500, 400, box));
        }
    }
}
