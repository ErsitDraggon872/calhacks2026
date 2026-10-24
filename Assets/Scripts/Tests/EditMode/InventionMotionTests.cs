using NUnit.Framework;

namespace CalHacks.Tests
{
    public class InventionMotionTests
    {
        [TestCase("cat", "", "", InventionMotion.Kind.Wander)]
        [TestCase("dog", "", "follow me", InventionMotion.Kind.Follow)]
        [TestCase("frog", "", "", InventionMotion.Kind.Hop)]
        [TestCase("bird", "", "", InventionMotion.Kind.Fly)]
        [TestCase("balloon", "", "", InventionMotion.Kind.Fly)]
        [TestCase("car", "", "", InventionMotion.Kind.RollMove)]
        [TestCase("fan", "", "", InventionMotion.Kind.Oscillate)]
        [TestCase("bridge", "", "", InventionMotion.Kind.Static)]
        [TestCase("catapult", "", "", InventionMotion.Kind.Static)]
        [TestCase("robot", "", "don't move", InventionMotion.Kind.Static)]
        [TestCase("mystery shape", "", "A dog that follows me", InventionMotion.Kind.Follow)]
        [TestCase("giraffe", "", "", InventionMotion.Kind.Wander)]
        [TestCase("car", "", "Drive automatically", InventionMotion.Kind.RollMove)]
        [TestCase("rocket", "", "", InventionMotion.Kind.Static)]
        [TestCase("car", "", "", InventionMotion.Kind.RollMove)]
        [TestCase("flying carpet", "", "", InventionMotion.Kind.Fly)]
        public void ClassifiesMotionWithoutChangingThePhysicsContract(string name, string message,
            string creatorIntent, InventionMotion.Kind expected)
        {
            Assert.That(InventionMotion.Choose(name, message, creatorIntent), Is.EqualTo(expected));
        }

        [Test]
        public void ExplicitMotionFieldIsOptional()
        {
            Assert.That(InventionMotion.Choose("shape", "", "", "hop"),
                Is.EqualTo(InventionMotion.Kind.Hop));
            Assert.That(InventionMotion.Choose("shape", "", "", null),
                Is.EqualTo(InventionMotion.Kind.Static));
            Assert.That(InventionMotion.Choose("shape", "", "a cat that follows me", "static"),
                Is.EqualTo(InventionMotion.Kind.Follow));
        }

        [Test]
        public void CarsStayPlayerControlledUnlessExplicitlyAutonomous()
        {
            Assert.IsFalse(InventionMotion.WantsAutonomousDrive("Drive the car when I press E"));
            Assert.IsFalse(InventionMotion.WantsAutonomousDrive("A fast red car"));
            Assert.IsTrue(InventionMotion.WantsAutonomousDrive("A car that drives on its own"));
        }

        [Test]
        public void OnlyLivingCreaturesCanBeSquashed()
        {
            Assert.IsTrue(InventionMotion.CanSquash(InventionMotion.Kind.Wander));
            Assert.IsTrue(InventionMotion.CanSquash(InventionMotion.Kind.Follow));
            Assert.IsTrue(InventionMotion.CanSquash(InventionMotion.Kind.Hop));
            Assert.IsTrue(InventionMotion.CanSquash(InventionMotion.Kind.Fly));
            Assert.IsFalse(InventionMotion.CanSquash(InventionMotion.Kind.RollMove));
            Assert.IsFalse(InventionMotion.CanSquash(InventionMotion.Kind.Static));
            Assert.IsFalse(InventionMotion.CanSquash(InventionMotion.Kind.Oscillate));
        }

        [Test]
        public void LandingRequiresRealDownwardStompNotWalkOrJustSpawn()
        {
            var cat = InventionMotion.Kind.Wander;
            Assert.IsTrue(InventionMotion.IsSquashLanding(cat, -5f, .9f, -.2f, 2f));
            Assert.IsFalse(InventionMotion.IsSquashLanding(cat, -2f, .9f, -.2f, 2f));
            Assert.IsFalse(InventionMotion.IsSquashLanding(cat, -5f, .9f, -.2f, .2f));
            Assert.IsFalse(InventionMotion.IsSquashLanding(cat, -5f, .1f, -.2f, 2f));
            Assert.IsFalse(InventionMotion.IsSquashLanding(cat, -5f, .9f, .1f, 2f));
            Assert.IsFalse(InventionMotion.IsSquashLanding(InventionMotion.Kind.RollMove,
                -5f, .9f, -.2f, 2f));
        }

        [Test]
        public void CutoutFlipKeepsCollidersAndMirrorsOnlyArtwork()
        {
            var go = new UnityEngine.GameObject("facing test");
            try
            {
                var cutout = go.AddComponent<Cutout>();
                go.transform.localScale = new UnityEngine.Vector3(2f, 3f, 1f);
                cutout.FaceHorizontal(-1f);
                Assert.That(cutout.FacingSign, Is.EqualTo(-1f));
                Assert.That(go.transform.localScale.x, Is.EqualTo(-2f));
                cutout.FaceHorizontal(1f);
                Assert.That(cutout.FacingSign, Is.EqualTo(1f));
                Assert.That(go.transform.localScale.x, Is.EqualTo(2f));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void SafeGroundedCreaturePlacementIsRequested()
        {
            Assert.IsTrue(InventionMotion.IsGroundCreature(new InterpretResponse {name = "cat"}, ""));
            Assert.IsTrue(InventionMotion.IsGroundCreature(new InterpretResponse {name = "frog"}, ""));
            Assert.IsFalse(InventionMotion.IsGroundCreature(new InterpretResponse {name = "balloon"}, ""));
        }
    }
}
