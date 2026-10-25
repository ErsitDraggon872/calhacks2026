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

        // The level's gap: ledges at y 0, the bridge span 4.9 m centred on x 12.3.
        [Test]
        public void LedgeBuildStandsOnBsLedgeAgainstTheDrop()
        {
            foreach (var width in new[] { 0.5f, 2f, 3f })
            {
                var left = InventionPlacement.LedgeBottomCentre(new Vector3(9.5f, 0f, -0.3f), GapAnchor, 4.9f, width, out var away);
                Assert.AreEqual(-1f, away);
                Assert.AreEqual(12.3f - 2.45f, left.x + width * 0.5f, 1e-4f, $"width {width}");
                Assert.AreEqual(0f, left.y, 1e-4f);
                Assert.AreEqual(0f, left.z, 1e-4f, "on the path's middle, not B's depth");

                var right = InventionPlacement.LedgeBottomCentre(new Vector3(15f, 0f, 0.4f), GapAnchor, 4.9f, width, out away);
                Assert.AreEqual(1f, away);
                Assert.AreEqual(12.3f + 2.45f, right.x - width * 0.5f, 1e-4f, $"width {width}");
            }
        }

        [Test]
        public void LedgeBuildFitsTheStartPathWhereverBStands()
        {
            // The start path is z -1..1, between its invisible front edge and the raised terrace: a 2 m deep build
            // centred on B (anywhere but z 0) poked into one of them and was pushed out in front of the edge.
            var front = Box(new Vector3(-6f, 0f, -1.4f), new Vector3(10f, 4f, -1f));
            var terrace = Box(new Vector3(-6f, -1f, 1f), new Vector3(10f, 0.8f, 3f));
            foreach (var z in new[] { -0.6f, -0.08f, 0f, 0.5f })
            {
                var bottom = InventionPlacement.LedgeBottomCentre(new Vector3(9.3f, 0.05f, z), GapAnchor, 4.9f, 3f, out var away);
                var build = InventionPlacement.BuildBounds(bottom, new Vector2(3f, 1.64f), 2f);
                Assert.IsTrue(InventionPlacement.ClearSpot(build, new[] { front, terrace }, away, out var shift), $"B at z {z}");
                Assert.AreEqual(Vector3.zero, shift, $"B at z {z}");
            }
        }

        static readonly Vector3 GapAnchor = new Vector3(12.3f, 0f, 0f);

        [Test]
        public void DropEdgeFindsWhereTheLedgeEnds()
        {
            // Ground from x 10 to 40; B at 15.
            bool Ground(float x) => x >= 10f && x <= 40f;
            Assert.AreEqual(10f, InventionPlacement.DropEdge(15f, -1f, 8f, Ground).Value, InventionPlacement.EdgeStep);
            Assert.GreaterOrEqual(InventionPlacement.DropEdge(15f, -1f, 8f, Ground).Value, 10f, "never past the edge");
            Assert.IsNull(InventionPlacement.DropEdge(15f, 1f, 8f, Ground), "ground all the way");
            Assert.IsNull(InventionPlacement.DropEdge(15f, -1f, 5f, Ground), "reach ends exactly on the edge");
        }

        [Test]
        public void ClearSpotSkipsSpotsWithNothingToStandOn()
        {
            // Poking into the ledge: the nearest clear spot is toward the camera, but the floor ends at z -2 (the front
            // edge), so a spot in front of that is no spot at all.
            var build = InventionPlacement.BuildBounds(new Vector3(26f, 0f, -0.3f), new Vector2(2f, 1f), 2f);
            bool OnFloor(Bounds b) => b.center.z >= -2f;
            Assert.IsTrue(InventionPlacement.ClearSpot(build, new[] { Floor, Ledge }, 1f, out var shift, OnFloor));
            Assert.GreaterOrEqual(build.center.z + shift.z, -2f);
            Assert.IsFalse(InventionPlacement.ClearSpot(build, new[] { Floor, Ledge }, 1f, out shift, b => false));
        }

        [Test]
        public void BuildPulledBackToTheEdgeStandsOnTheLedge()
        {
            // B at 11 facing the drop at x 10: the usual spot hangs out over it.
            bool Ground(float x) => x >= 10f;
            var feet = new Vector3(11f, 0f, 0f);
            var width = 3f;
            var bottom = InventionPlacement.BottomCentre(feet, -1f, Radius, width);
            var edge = InventionPlacement.DropEdge(feet.x, -1f, Mathf.Abs(bottom.x - feet.x) + width * 0.5f, Ground);
            Assert.IsNotNull(edge);
            var x = edge.Value + width * 0.5f;
            Assert.IsTrue(Ground(x - width * 0.5f) && Ground(x + width * 0.5f), "whole build over ground");
        }

        // The level's high-ledge slot: 3 x 1.2 m against the ledge's left face (x 24), at the ledge's depth.
        static readonly Vector3 SlotBottom = new Vector3(22.5f, 0f, 1.75f);
        static readonly Vector2 SlotBox = BuildSlot.DefaultBox;
        static readonly (int w, int h)[] Drawings = { (400, 100), (100, 400), (300, 250), (1, 1), (1000, 10), (250, 300) };

        [Test]
        public void EveryFittedSlotBuildLandsInsideTheGhost()
        {
            foreach (var align in new[] { 1f, -1f, 0f })
            foreach (var (w, h) in Drawings)
            {
                var size = InventionPlacement.FitInside(w, h, SlotBox);
                var p = InventionPlacement.SlotBottomCentre(SlotBottom, SlotBox, size, align);
                var msg = $"align {align}, drawing {w}x{h}";
                Assert.GreaterOrEqual(p.x - size.x * 0.5f, SlotBottom.x - SlotBox.x * 0.5f - 1e-4f, msg);
                Assert.LessOrEqual(p.x + size.x * 0.5f, SlotBottom.x + SlotBox.x * 0.5f + 1e-4f, msg);
                Assert.AreEqual(SlotBottom.y, p.y, 1e-4f, msg);
                Assert.AreEqual(SlotBottom.z, p.z, 1e-4f, msg);
                Assert.LessOrEqual(size.y, SlotBox.y + 1e-4f, msg);
            }
        }

        [Test]
        public void SlotBuildTouchesTheSideItIsPushedTo()
        {
            foreach (var (w, h) in Drawings)
            {
                var size = InventionPlacement.FitInside(w, h, SlotBox);
                var right = InventionPlacement.SlotBottomCentre(SlotBottom, SlotBox, size, 1f);
                Assert.AreEqual(SlotBottom.x + SlotBox.x * 0.5f, right.x + size.x * 0.5f, 1e-4f, $"drawing {w}x{h}");
                var left = InventionPlacement.SlotBottomCentre(SlotBottom, SlotBox, size, -1f);
                Assert.AreEqual(SlotBottom.x - SlotBox.x * 0.5f, left.x - size.x * 0.5f, 1e-4f, $"drawing {w}x{h}");
            }
        }

        [Test]
        public void AlmostAnyDrawingMakesAStepOntoTheHighLedge()
        {
            // Measured with B's CharacterController: from the floor it lands on steps up to 1.6 m, and from a step of
            // 0.7 m or more it reaches the 2.3 m ledge. Anything up to 4:1 wide (or any tall shape) fits that window.
            foreach (var (w, h) in new[] { (400, 100), (100, 400), (300, 250), (1, 1), (250, 300), (1, 20) })
            {
                var size = InventionPlacement.FitInside(w, h, SlotBox);
                Assert.That(size.y, Is.InRange(0.7f, 1.6f), $"drawing {w}x{h}");
            }
        }

        static readonly Bounds Floor = Box(new Vector3(14.6f, -1f, -2f), new Vector3(40f, 0f, 3f));
        static readonly Bounds Ledge = Box(new Vector3(24f, 0f, 0.5f), new Vector3(30f, 2.3f, 3f));
        static readonly Bounds FrontEdge = Box(new Vector3(14.6f, 0f, -2.4f), new Vector3(40f, 8f, -2f));

        static Bounds Box(Vector3 min, Vector3 max)
        {
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        [Test]
        public void RestingOnTheFloorIsNotAnOverlap()
        {
            var build = InventionPlacement.BuildBounds(new Vector3(20f, 0f, 1f), new Vector2(3f, 2.5f), 2f);
            Assert.IsFalse(InventionPlacement.Overlaps(build, Floor));
            Assert.IsTrue(InventionPlacement.ClearSpot(build, new[] { Floor, Ledge, FrontEdge }, 1f, out var shift));
            Assert.AreEqual(Vector3.zero, shift);
        }

        [Test]
        public void BuildInsideTheLedgeMovesOutOfIt()
        {
            var obstacles = new[] { Floor, Ledge, FrontEdge };
            // B at the back of the hub, right of the ledge, facing it: today's spot is inside the ledge.
            foreach (var z in new[] { 0.5f, 1.5f, 2.5f })
            {
                var feet = new Vector3(31f, 0.05f, z);
                var size = new Vector2(3f, 2.5f);
                var bottom = InventionPlacement.BottomCentre(feet, -1f, Radius, size.x);
                var build = InventionPlacement.BuildBounds(bottom, size, 2f);
                Assert.IsTrue(InventionPlacement.Overlaps(build, Ledge), $"z {z}: setup");
                Assert.IsTrue(InventionPlacement.ClearSpot(build, obstacles, -1f, out var shift), $"z {z}");
                var moved = new Bounds(build.center + shift, build.size);
                foreach (var obstacle in obstacles) Assert.IsFalse(InventionPlacement.Overlaps(moved, obstacle), $"z {z}");
                Assert.LessOrEqual(shift.z, 0f, $"z {z}: only toward the camera");
                Assert.LessOrEqual(shift.x, 0f, $"z {z}: only further from B");
            }
        }

        [Test]
        public void ClearSpotPrefersTheSmallestMove()
        {
            // Just poking into the ledge's front face: a small step toward the camera clears it.
            var build = InventionPlacement.BuildBounds(new Vector3(26f, 0f, -0.3f), new Vector2(2f, 1f), 2f);
            Assert.IsTrue(InventionPlacement.ClearSpot(build, new[] { Floor, Ledge, FrontEdge }, 1f, out var shift));
            Assert.AreEqual(0f, shift.x, 1e-4f);
            Assert.AreEqual(-0.25f, shift.z, 1e-4f);
        }

        [Test]
        public void NoRoomMeansNoBuild()
        {
            var build = InventionPlacement.BuildBounds(new Vector3(0f, 0f, 0f), new Vector2(1f, 1f), 1f);
            var wall = Box(new Vector3(-20f, -1f, -20f), new Vector3(20f, 5f, 20f));
            Assert.IsFalse(InventionPlacement.ClearSpot(build, new[] { wall }, 1f, out var shift));
            Assert.AreEqual(Vector3.zero, shift);
        }

        [Test]
        public void LiftsOnlyAPlayerTheBuildAppearedAround()
        {
            var step = Box(new Vector3(21f, 0f, 0.5f), new Vector3(24f, 1.2f, 3f));
            var inside = new Bounds(new Vector3(23f, 1.05f, 2.5f), new Vector3(0.8f, 2f, 0.8f));
            Assert.AreEqual(1.2f, InventionPlacement.LiftTo(inside, new[] { step }).Value, 1e-4f);
            var beside = new Bounds(new Vector3(19f, 1.05f, 2.5f), new Vector3(0.8f, 2f, 0.8f));
            Assert.IsNull(InventionPlacement.LiftTo(beside, new[] { step }));
            var onTop = new Bounds(new Vector3(23f, 2.25f, 2.5f), new Vector3(0.8f, 2f, 0.8f));
            Assert.IsNull(InventionPlacement.LiftTo(onTop, new[] { step }));
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
