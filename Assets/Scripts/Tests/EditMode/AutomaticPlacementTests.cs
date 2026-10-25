using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class AutomaticPlacementTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject root;
        RunManager run;
        GameConfig config;
        Material material;
        PlayerController player;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Automatic placement test");
            var host = new GameObject("Inactive run");
            host.transform.SetParent(root.transform);
            host.SetActive(false);
            run = host.AddComponent<RunManager>();
            config = ScriptableObject.CreateInstance<GameConfig>();
            config.inventionSize = Vector2.one;
            config.inventionDepth = 1f;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var explorer = new GameObject("Explorer");
            explorer.transform.SetParent(root.transform);
            explorer.SetActive(false);
            player = explorer.AddComponent<PlayerController>();
            typeof(PlayerController).GetField("controller", Private).SetValue(player, explorer.GetComponent<CharacterController>());
            Set("config", config);
            Set("player", player);
            Set("inventionMaterial", material);
            Set("inventionRoot", root.transform);
            Floor();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(material);
            InventionCard.Hide();
        }

        void Set(string name, object value) => typeof(RunManager).GetField(name, Private).SetValue(run, value);
        object Call(string name) => typeof(RunManager).GetMethod(name, Private).Invoke(run, null);
        int Waiting => (int)(typeof(RunManager).GetProperty("PendingBuildCount")?.GetValue(run) ?? 0);

        Texture2D Drawing()
        {
            var paper = new Texture2D(2, 2);
            paper.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black });
            paper.Apply();
            return paper;
        }

        void Floor()
        {
            var floor = new GameObject("Terrain");
            floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(50f, 1f, 50f);
            Physics.SyncTransforms();
        }

        GameObject Blocker()
        {
            var wall = new GameObject("Blocked terrain");
            wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(0f, 2f, 0f);
            wall.AddComponent<BoxCollider>().size = new Vector3(30f, 4f, 30f);
            Physics.SyncTransforms();
            return wall;
        }

        [Test]
        public void NewDrawingsOverlapExistingInventionsButStillCollideWithTerrain()
        {
            var first = run.SpawnInvention(Drawing(), InventionSpec.Plain());
            var second = run.SpawnInvention(Drawing(), InventionSpec.Plain());
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreEqual(first.transform.position, second.transform.position);
            foreach (var a in first.GetComponentsInChildren<Collider>())
            foreach (var b in second.GetComponentsInChildren<Collider>())
                Assert.IsTrue(Physics.GetIgnoreCollision(a, b), "Drawings must not jam one another");
            var terrain = root.transform.Find("Terrain").GetComponent<Collider>();
            Assert.IsFalse(Physics.GetIgnoreCollision(first.GetComponentInChildren<Collider>(), terrain));
        }

        [Test]
        public void BlockedDrawingKeepsItsTextureAndAutomaticallyBuildsWhenSpaceClears()
        {
            var wall = Blocker();
            var paper = Drawing();
            Assert.IsNull(run.SpawnInvention(paper, InventionSpec.Plain()));
            Assert.IsTrue(paper != null, "A blocked placement must retain the drawing");
            Assert.AreEqual(1, Waiting);
            Assert.IsFalse(run.PlacementPreview(out _));
            Object.DestroyImmediate(wall);
            Physics.SyncTransforms();
            Assert.IsTrue(run.PlacementPreview(out var preview));
            Call("TryPendingPlacement");
            Assert.AreEqual(0, Waiting);
            Assert.AreEqual(1, run.InventionCount);
            var placed = root.GetComponentInChildren<Invention>();
            Assert.AreEqual(preview.center, placed.transform.position + Vector3.up * placed.Size.y * .5f);
            Call("TryPendingPlacement");
            Assert.AreEqual(1, run.InventionCount, "Retry must not duplicate a drawing");
        }

        [Test]
        public void BuildLocksItsSpotSoTheGhostAndBuildStayPutWhileTheExplorerWalksOff()
        {
            Assert.IsTrue(run.PlacementPreview(out var before));
            var spot = Call("LockSpot");
            Assert.IsNotNull(spot);
            Set("lockedSpot", spot);
            player.transform.position += Vector3.right * 5f;
            Physics.SyncTransforms();
            Assert.IsTrue(run.PlacementPreview(out var after));
            Assert.AreEqual(before.center, after.center, "The ghost must not follow the explorer once BUILD is pressed");
            var place = typeof(RunManager).GetMethod("PlaceInvention", Private);
            var placed = (Invention)place.Invoke(run,
                new object[] { Drawing(), InventionSpec.Plain(), null, true, null, null, spot });
            Assert.IsNotNull(placed);
            Assert.AreEqual(before.center, placed.transform.position + Vector3.up * placed.Size.y * .5f);
        }

        [Test]
        public void BuildFindsNoSpotWhereTerrainLeavesNoRoom()
        {
            Blocker();
            Assert.IsNull(Call("LockSpot"));
        }

        [Test]
        public void DeferredRemoteBuildCompletesOnceAndCanBeCancelledBeforeSpawning()
        {
            var wall = Blocker();
            var calls = 0;
            var valid = true;
            run.SpawnInvention(Drawing(), InventionSpec.Plain(), _ => calls++, () => valid);
            Assert.AreEqual(0, calls);
            valid = false;
            Call("TryPendingPlacement");
            Assert.AreEqual(0, Waiting);
            Assert.AreEqual(0, calls);
            run.SpawnInvention(Drawing(), InventionSpec.Plain(), _ => calls++);
            Object.DestroyImmediate(wall);
            Physics.SyncTransforms();
            Call("TryPendingPlacement");
            Call("TryPendingPlacement");
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void RoomChangeDropsWaitingDrawingsEvenWithoutDigitalCapture()
        {
            Blocker();
            var paper = Drawing();
            run.SpawnInvention(paper, InventionSpec.Plain());
            run.CancelBoardBuild();
            Assert.AreEqual(0, Waiting);
            Assert.IsTrue(paper == null);
        }

        [Test]
        public void ResetDropsBlockedDrawingAndDoesNotBuildItLater()
        {
            Blocker();
            var paper = Drawing();
            run.SpawnInvention(paper, InventionSpec.Plain());
            run.ResetRun();
            Assert.AreEqual(0, Waiting);
            Assert.IsTrue(paper == null);
            Call("TryPendingPlacement");
            Assert.AreEqual(0, run.InventionCount);
        }

        [Test]
        public void PendingDrawingsKeepTheirOrderAndDoNotRequireConfirmation()
        {
            var wall = Blocker();
            run.SpawnInvention(Drawing(), InventionSpec.FromLabel("cat"));
            run.SpawnInvention(Drawing(), InventionSpec.FromLabel("bridge"));
            Assert.AreEqual(2, Waiting);
            Object.DestroyImmediate(wall);
            Physics.SyncTransforms();
            Call("TryPendingPlacement");
            Assert.AreEqual(1, Waiting);
            Assert.AreEqual("cat", root.GetComponentInChildren<Invention>().Spec.Name);
            Assert.IsFalse(InventionCard.Visible, "Automatic placement must not open a blocking confirmation");
            Call("TryPendingPlacement");
            Assert.AreEqual(0, Waiting);
            Assert.AreEqual(2, run.InventionCount);
        }

        [TestCase("bird")]
        [TestCase("frog")]
        [TestCase("snake")]
        [TestCase("turtle")]
        public void CreatureChecksItsWholeShapeAgainstWallsAndCeilings(string name)
        {
            var creature = Invention.Create(Drawing(), Vector3.zero, new Vector2(2f, 2f), 1f, material, root.transform);
            creature.Apply(InventionSpec.FromLabel(name));
            var motion = creature.GetComponent<InventionMotion>();
            Assert.IsNotNull(motion);
            var wall = new GameObject("High wall missed by a centre ray");
            wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(1.25f, 1.85f, 0f);
            wall.AddComponent<BoxCollider>().size = new Vector3(.1f, .2f, 2f);
            Physics.SyncTransforms();
            var constrain = typeof(InventionMotion).GetMethod("ConstrainMotion");
            Assert.IsNotNull(constrain, "Kinematic creatures need a silhouette sweep before MovePosition");
            var stopped = (Vector3)constrain.Invoke(motion, new object[] { new Vector3(2f, 0f, 0f) });
            Assert.LessOrEqual(stopped.x, .21f, "The leading edge must stop at the wall");
            Object.DestroyImmediate(wall);
            var ceiling = new GameObject("Ceiling");
            ceiling.transform.SetParent(root.transform);
            ceiling.transform.position = new Vector3(0f, 2.25f, 0f);
            ceiling.AddComponent<BoxCollider>().size = new Vector3(10f, .1f, 4f);
            Physics.SyncTransforms();
            stopped = (Vector3)constrain.Invoke(motion, new object[] { new Vector3(0f, 2f, 0f) });
            Assert.LessOrEqual(stopped.y, .21f);
        }
    }
}
