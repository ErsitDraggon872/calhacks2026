using NUnit.Framework;

namespace CalHacks.Tests
{
    public class RunStateTests
    {
        [Test]
        public void CanCastInitially()
        {
            Assert.AreEqual(CastBlock.None, new RunState().CanCast());
        }

        [Test]
        public void CastIgnoredWhileInFlight()
        {
            var s = new RunState();
            s.BeginRequest();
            Assert.AreEqual(CastBlock.InFlight, s.CanCast());
        }

        [Test]
        public void CastBlockedUntilCooldownEnds()
        {
            var s = new RunState();
            s.BeginRequest();
            s.StartCooldown(3f);
            s.EndRequest();
            Assert.AreEqual(CastBlock.Cooldown, s.CanCast());
            Assert.AreEqual(0f, s.CooldownFraction, 1e-4f);
            s.Tick(1.5f);
            Assert.AreEqual(CastBlock.Cooldown, s.CanCast());
            Assert.AreEqual(0.5f, s.CooldownFraction, 1e-4f);
            s.Tick(1.6f);
            Assert.AreEqual(CastBlock.None, s.CanCast());
            Assert.AreEqual(1f, s.CooldownFraction, 1e-4f);
        }

        [Test]
        public void CooldownRunsAfterFinish()
        {
            var s = new RunState();
            s.StartCooldown(1f);
            s.Finish();
            s.Tick(2f);
            Assert.AreEqual(0f, s.CooldownRemaining);
        }

        [Test]
        public void FailedCaptureStaysUnlocked()
        {
            var s = new RunState();
            s.BeginRequest();
            s.EndRequest();
            Assert.AreEqual(CastBlock.None, s.CanCast());
        }

        [Test]
        public void ResetIncrementsRunIdAndClearsLocks()
        {
            var s = new RunState();
            var before = s.RunId;
            s.BeginRequest();
            s.StartCooldown(10f);
            s.Tick(5f);
            s.Finish();
            var after = s.Reset();
            Assert.AreEqual(before + 1, after);
            Assert.AreEqual(CastBlock.None, s.CanCast());
            Assert.AreEqual(0f, s.Elapsed);
            Assert.AreEqual(0f, s.CooldownRemaining);
            Assert.IsFalse(s.Finished);
        }

        [Test]
        public void StaleRunIdIsDiscarded()
        {
            var s = new RunState();
            var requestRun = s.RunId;
            Assert.IsTrue(s.ShouldApply(requestRun, requestRun));
            s.Reset();
            Assert.IsFalse(s.ShouldApply(requestRun, requestRun));
            Assert.IsFalse(s.IsCurrent(requestRun));
        }

        [Test]
        public void MismatchedEchoIsDiscarded()
        {
            var s = new RunState();
            Assert.IsFalse(s.ShouldApply(s.RunId, s.RunId + 1));
        }

        [Test]
        public void TimerStopsWhenFinished()
        {
            var s = new RunState();
            s.Tick(2f);
            s.Finish();
            s.Tick(3f);
            Assert.AreEqual(2f, s.Elapsed);
            Assert.AreEqual(CastBlock.Finished, s.CanCast());
        }
    }
}
