using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class SocketLogicTests
    {
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
    }
}
