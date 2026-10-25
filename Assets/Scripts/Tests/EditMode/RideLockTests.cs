using NUnit.Framework;

namespace CalHacks.Tests
{
    public class RideLockTests
    {
        [Test]
        public void ParkedRideStartsWithE() =>
            Assert.AreEqual("E go", InventionPhysics.RideKey(parked: true, toggle: false, hold: false, active: false));

        [Test]
        public void ToggleRideStopsAndGoes()
        {
            Assert.AreEqual("E stop", InventionPhysics.RideKey(parked: false, toggle: true, hold: false, active: true));
            Assert.AreEqual("E go", InventionPhysics.RideKey(parked: false, toggle: true, hold: false, active: false));
        }

        [Test]
        public void HoldRideIsHeld() =>
            Assert.AreEqual("hold E", InventionPhysics.RideKey(parked: false, toggle: false, hold: true, active: true));

        // A one-shot rocket already flying (or an animal) has nothing left for E; only Space gets off.
        [Test]
        public void MovingOneShotRideHasNoKey() =>
            Assert.IsNull(InventionPhysics.RideKey(parked: false, toggle: false, hold: false, active: true));
    }
}
