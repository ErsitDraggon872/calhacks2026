using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using InventionRow = SpacetimeDB.Types.Invention;

namespace CalHacks.Tests
{
    public class BrowserBuildQueueTests
    {
        GameObject root;
        ThinkInkSession session;
        DigitalBoardFrameSource source;
        bool wasRunningInBackground;
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        public void SetUp()
        {
            wasRunningInBackground = Application.runInBackground;
            root = new GameObject("Board test root");
            var child = new GameObject("Session");
            child.transform.SetParent(root.transform);
            session = child.AddComponent<ThinkInkSession>();
            session.enabled = false;
            typeof(ThinkInkSession).GetProperty("Room").SetValue(session,
                new SpacetimeDB.Types.Room { RoomId = 42, RoomCode = "TEST" });
            SetReady(true);
            source = root.AddComponent<DigitalBoardFrameSource>();
            source.enabled = false;
            typeof(DigitalBoardFrameSource).GetField("session", Private).SetValue(source, session);
            Call("OnRoomChanged");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Application.runInBackground = wasRunningInBackground;
        }

        void SetReady(bool ready) => typeof(ThinkInkSession).GetProperty("RoomReady").SetValue(session, ready);
        void Call(string name, params object[] args) => typeof(DigitalBoardFrameSource).GetMethod(name, Private).Invoke(source, args);
        InventionRow Row(string id) => new InventionRow { RoomId = 42, CaptureId = id, Status = "pending", ImagePngBase64 = "" };
        object[] Captures()
        {
            var field = typeof(DigitalBoardFrameSource).GetField("pending", Private);
            Assert.IsNotNull(field, "BUILD requires a queue rather than one overwritten pin");
            return ((IEnumerable)field.GetValue(source)).Cast<object>().ToArray();
        }

        [Test]
        public void ConsecutiveBuildsKeepBothSnapshotsAfterClearAndIgnoreDuplicates()
        {
            Call("OnStroke", new SpacetimeDB.Types.DrawingStroke { RoomId = 42, CaptureId = "a", Points = new() { .2f, .5f, .8f, .5f } });
            Call("OnInvention", Row("a"));
            Call("OnCleared");
            Call("OnInvention", Row("b"));
            Call("OnInvention", Row("a"));
            var captures = Captures();
            Assert.AreEqual(2, captures.Length);
            Assert.AreEqual("a", captures[0].GetType().GetField("Id").GetValue(captures[0]));
            var pixels = (Color32[])captures[0].GetType().GetField("Pixels").GetValue(captures[0]);
            Assert.IsTrue(pixels.Any(p => p.r < 128), "CLEAR must retain the submitted drawing");
        }

        [Test]
        public void HistoricalAndOtherRoomRowsNeverBuild()
        {
            SetReady(false);
            Call("OnInvention", Row("old"));
            SetReady(true);
            Call("OnInvention", Row("old"));
            var other = Row("foreign"); other.RoomId = 7;
            Call("OnInvention", other);
            Assert.IsEmpty(Captures());
        }

        [Test]
        public void ResetDropsQueuedFramesAndDoesNotReplaySeenRows()
        {
            Call("OnInvention", Row("a"));
            Call("OnRunReset");
            Call("OnInvention", Row("a"));
            Assert.IsEmpty(Captures());
        }

        [Test]
        public void RoomChangeDropsQueuedFrames()
        {
            Call("OnInvention", Row("a"));
            typeof(ThinkInkSession).GetProperty("Room").SetValue(session,
                new SpacetimeDB.Types.Room { RoomId = 43, RoomCode = "NEXT" });
            Call("OnRoomChanged");
            Assert.IsEmpty(Captures());
        }
    }
}
