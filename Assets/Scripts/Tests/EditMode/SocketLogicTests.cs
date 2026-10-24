using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class SocketLogicTests
    {
        static readonly Trait[] Cave = { Trait.Glows };
        static readonly Trait[] Gatekeeper = { Trait.Edible, Trait.Hostile };

        [Test]
        public void SolvingTraitSolvesWhateverItsName()
        {
            var torch = InventionSpec.Parse(new InterpretResponse { status = "ok", name = "torch", traits = new[] { "glows", "hot" } });
            Assert.AreEqual(LabelOutcome.Solve, SocketLogic.Resolve(torch, Cave, new[] { "light" }));
        }

        [Test]
        public void AnyFoodFeedsTheGatekeeper()
        {
            var apple = InventionSpec.Parse(new InterpretResponse { status = "ok", name = "apple", material = "plant" });
            Assert.AreEqual(LabelOutcome.Solve, SocketLogic.Resolve(apple, Gatekeeper, new[] { "fish" }));
            var shark = InventionSpec.Parse(new InterpretResponse { status = "ok", name = "shark", traits = new[] { "hostile" } });
            Assert.AreEqual(LabelOutcome.Solve, SocketLogic.Resolve(shark, Gatekeeper, new[] { "fish" }));
            Assert.AreEqual(LabelOutcome.Decor, SocketLogic.Resolve(InventionSpec.FromLabel("bridge"), Gatekeeper, new[] { "fish" }));
        }

        [Test]
        public void SpecFallsBackToTheLabel()
        {
            Assert.AreEqual(LabelOutcome.Solve, SocketLogic.Resolve(InventionSpec.FromLabel("food"), Cave, new[] { "food" }));
            Assert.AreEqual(LabelOutcome.CatReaction, SocketLogic.Resolve(InventionSpec.FromLabel("cat"), Cave, new[] { "light" }));
        }

        [Test]
        public void RejectedSpecNeverSolves()
        {
            var lamp = InventionSpec.Parse(new InterpretResponse { status = "rejected", name = "light", traits = new[] { "glows" } });
            Assert.AreEqual(LabelOutcome.Decor, SocketLogic.Resolve(lamp, Cave, new[] { "light" }));
        }

        [Test]
        public void AcceptedLabelSolves()
        {
            Assert.AreEqual(LabelOutcome.Solve, SocketLogic.Resolve("fish", new[] { "fish" }));
        }

        [Test]
        public void LabelMatchingIgnoresCaseAndWhitespace()
        {
            Assert.AreEqual(LabelOutcome.Solve, SocketLogic.Resolve("  Light ", new[] { "light" }));
        }

        [Test]
        public void CatPlaysReactionWhenNotAccepted()
        {
            Assert.AreEqual(LabelOutcome.CatReaction, SocketLogic.Resolve("cat", new[] { "fish" }));
        }

        [Test]
        public void CatSolvesWhenAccepted()
        {
            Assert.AreEqual(LabelOutcome.Solve, SocketLogic.Resolve("cat", new[] { "cat" }));
        }

        [TestCase("unknown")]
        [TestCase("bridge")]
        [TestCase("")]
        [TestCase(null)]
        public void WrongOrMissingLabelIsDecor(string label)
        {
            Assert.AreEqual(LabelOutcome.Decor, SocketLogic.Resolve(label, new[] { "fish" }));
        }

        [Test]
        public void UnknownNeverSolvesEvenIfListed()
        {
            Assert.AreEqual(LabelOutcome.Decor, SocketLogic.Resolve(null, new[] { "unknown" }));
        }

        [Test]
        public void EmptyAcceptedListNeverSolves()
        {
            Assert.AreEqual(LabelOutcome.Decor, SocketLogic.Resolve("bridge", new string[0]));
            Assert.AreEqual(LabelOutcome.Decor, SocketLogic.Resolve("bridge", null));
        }

        [Test]
        public void NearestPicksClosestWithinRange()
        {
            var points = new[] { new Vector3(4, 0, 0), new Vector3(2, 0, 0), new Vector3(-10, 0, 0) };
            Assert.AreEqual(1, SocketLogic.FindNearest(points, null, Vector3.zero, 5f));
        }

        [Test]
        public void NearestReturnsMinusOneWhenNothingInRange()
        {
            var points = new[] { new Vector3(6, 0, 0), new Vector3(0, 0, -7) };
            Assert.AreEqual(-1, SocketLogic.FindNearest(points, null, Vector3.zero, 5f));
        }

        [Test]
        public void NearestSkipsSolvedSockets()
        {
            var points = new[] { new Vector3(1, 0, 0), new Vector3(3, 0, 0) };
            Assert.AreEqual(1, SocketLogic.FindNearest(points, new[] { true, false }, Vector3.zero, 5f));
        }

        [Test]
        public void NearestIgnoresHeight()
        {
            var points = new[] { new Vector3(1, 50, 0) };
            Assert.AreEqual(0, SocketLogic.FindNearest(points, null, Vector3.zero, 2f));
        }

        [Test]
        public void SocketOwnRangeIsRespected()
        {
            // The gatekeeper (2.5 m) is nearer, but out of its own range; the default-range socket still catches B.
            var points = new[] { new Vector3(3, 0, 0), new Vector3(4, 0, 0) };
            var ranges = new[] { 2.5f, 0f };
            Assert.AreEqual(1, SocketLogic.FindNearest(points, null, ranges, Vector3.zero, 5f, 1.5f));
            Assert.AreEqual(0, SocketLogic.FindNearest(points, null, ranges, new Vector3(1, 0, 0), 5f, 1.5f));
        }

        [Test]
        public void SocketBelowALedgeIsSkipped()
        {
            var points = new[] { new Vector3(1, 0, 0) };
            Assert.AreEqual(-1, SocketLogic.FindNearest(points, null, null, new Vector3(0, 2.3f, 0), 5f, 1.5f));
            Assert.AreEqual(0, SocketLogic.FindNearest(points, null, null, new Vector3(0, 1f, 0), 5f, 1.5f));
        }
    }
}
