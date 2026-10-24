using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class InventionSpecTests
    {
        // The contract's example response (docs/interpret-contract.md), as the backend would send it.
        const string ExampleJson = @"{
  ""status"": ""ok"", ""capture_id"": ""cam-3"", ""run_id"": 7,
  ""name"": ""flaming rubber skateboard"",
  ""message"": ""A bouncy skateboard on fire, with a booster on the back. Press E to fire."",
  ""adjectives"": [""flaming"", ""rubber""],
  ""material"": ""rubber"", ""weight"": ""light"", ""body"": ""static"",
  ""traits"": [""hot"", ""glows"", ""rideable""],
  ""width_m"": 2.4, ""ink_cost"": 45,
  ""components"": [
    { ""type"": ""support"", ""activation"": ""always"", ""anchor_x"": 0.5, ""anchor_y"": 0.3,
      ""launch_speed"": 0, ""angle_deg"": 0, ""acceleration"": 0, ""duration_s"": 0, ""emit_kind"": """", ""range_m"": 0 },
    { ""type"": ""thrust"", ""activation"": ""button"", ""anchor_x"": 0.1, ""anchor_y"": 0.4,
      ""launch_speed"": 0, ""angle_deg"": 0, ""acceleration"": 12, ""duration_s"": 2.5, ""emit_kind"": """", ""range_m"": 0 }
  ],
  ""provider"": ""bitdeer"", ""latency_ms"": 2140
}";

        static LegacyInterpretResponse Reply(string material = "wood", params string[] traits) => new LegacyInterpretResponse
        {
            status = "ok",
            name = "thing",
            material = material,
            traits = traits,
            components = new InterpretComponent[0],
        };

        static InterpretComponent Part(string type) => new InterpretComponent { type = type };

        [Test]
        public void ContractExampleParses()
        {
            var spec = InventionSpec.Parse(JsonUtility.FromJson<LegacyInterpretResponse>(ExampleJson));
            Assert.AreEqual("flaming rubber skateboard", spec.Name);
            Assert.AreEqual(MaterialKind.Rubber, spec.Material);
            Assert.AreEqual(WeightClass.Light, spec.Weight);
            Assert.IsTrue(spec.Has(Trait.Hot) && spec.Has(Trait.Glows) && spec.Has(Trait.Rideable));
            Assert.IsTrue(spec.Has(Trait.Bouncy), "rubber implies bouncy");
            Assert.AreEqual(new[] { PartType.Support, PartType.Thrust }, spec.Parts.Select(p => p.Type).ToArray());
            Assert.AreEqual(12f, spec.Parts[1].Acceleration);
            Assert.AreEqual(Activation.Button, spec.Parts[1].Activation);
            Assert.AreEqual(2.4f, spec.WidthM, 1e-4f);
            Assert.AreEqual(45, spec.InkCost);
            Assert.AreEqual(new[] { "flaming", "rubber" }, spec.Adjectives.ToArray());
            Assert.IsEmpty(spec.Dropped);
        }

        [Test]
        public void LabelOnlyReplyIsParsedWithoutAnEmbeddedInvention()
        {
            // The old /label response carries only a label. The separate
            // /interpret endpoint provides the physics DTO, not LabelResponse.
            var label = JsonUtility.FromJson<LabelResponse>(@"{""status"":""done"",""label"":""fish"",""run_id"":1}");
            Assert.IsNotNull(label);
            Assert.AreEqual("done", label.status);
            Assert.AreEqual("fish", label.label);
            Assert.AreEqual(1, label.run_id);
        }

        [Test]
        public void UnknownStringsAreDroppedAndCaseIgnored()
        {
            var r = Reply("Lava Rock", "GLOWS", "lighter-than-air", "sparkly", "42");
            var spec = InventionSpec.Parse(r);
            Assert.AreEqual(MaterialKind.Wood, spec.Material, "unknown material falls back to wood");
            Assert.IsTrue(spec.Has(Trait.Glows));
            Assert.IsTrue(spec.Has(Trait.LighterThanAir));
            CollectionAssert.AreEquivalent(new[] { "Lava Rock", "sparkly", "42" }, spec.Dropped);
        }

        [Test]
        public void MaterialImpliesTraits()
        {
            var ice = InventionSpec.Parse(Reply("ice"));
            Assert.IsTrue(ice.Has(Trait.Cold) && ice.Has(Trait.Slippery) && ice.Has(Trait.Fragile));
            Assert.IsTrue(InventionSpec.Parse(Reply("wood")).Has(Trait.Flammable));
        }

        [Test]
        public void HotWinsOverCold()
        {
            var spec = InventionSpec.Parse(Reply("ice", "hot"));
            Assert.IsTrue(spec.Has(Trait.Hot));
            Assert.IsFalse(spec.Has(Trait.Cold));
        }

        [Test]
        public void HeavyThingsDontFloat()
        {
            var r = Reply("wood", "lighter_than_air");
            r.weight = "heavy";
            var spec = InventionSpec.Parse(r);
            Assert.IsFalse(spec.Has(Trait.Floats));
            Assert.IsFalse(spec.Has(Trait.LighterThanAir));
        }

        [Test]
        public void TraitsAreCappedAtEight()
        {
            var spec = InventionSpec.Parse(Reply("stone", "glows", "hot", "wet", "sharp", "noisy", "smelly", "soft", "sticky", "alive", "edible"));
            Assert.AreEqual(InventionSpec.MaxTraits, spec.Traits.Count);
            Assert.IsFalse(spec.Has(Trait.Edible));
        }

        [Test]
        public void NumbersAreClampedToTheContract()
        {
            var r = Reply();
            r.width_m = 10f;
            r.ink_cost = 500;
            r.components = new[]
            {
                new InterpretComponent { type = "bounce", launch_speed = 20f, angle_deg = 90f, activation = "always" },
                new InterpretComponent { type = "thrust", acceleration = 1f, duration_s = 9f },
            };
            var spec = InventionSpec.Parse(r);
            Assert.AreEqual(InventionSpec.MaxWidth, spec.WidthM);
            Assert.AreEqual(InventionSpec.MaxInk, spec.InkCost);
            var bounce = spec.Parts[0];
            Assert.AreEqual(14f, bounce.LaunchSpeed);
            Assert.AreEqual(60f, bounce.AngleDeg);
            Assert.AreEqual(Activation.Contact, bounce.Activation, "a bounce can't be 'always'");
            Assert.AreEqual(2f, spec.Parts[1].Acceleration);
            Assert.AreEqual(3f, spec.Parts[1].DurationS);
        }

        [Test]
        public void OnePartPerTypeAndNeverNone()
        {
            var r = Reply();
            r.components = new[] { Part("bounce"), Part("bounce"), Part("teleport") };
            var spec = InventionSpec.Parse(r);
            Assert.AreEqual(1, spec.Parts.Count);
            Assert.AreEqual(PartType.Bounce, spec.Parts[0].Type);

            Assert.AreEqual(PartType.Support, InventionSpec.Parse(Reply()).Parts.Single().Type);
        }

        [Test]
        public void EmitLightImpliesGlows()
        {
            var r = Reply("stone");
            r.components = new[] { new InterpretComponent { type = "emit", emit_kind = "light", range_m = 3f } };
            Assert.IsTrue(InventionSpec.Parse(r).Has(Trait.Glows));
        }

        [Test]
        public void MissingOrErrorReplyIsAPlainSupport()
        {
            var empty = InventionSpec.Parse(new LegacyInterpretResponse());
            Assert.AreEqual(PartType.Support, empty.Parts.Single().Type);
            Assert.IsFalse(empty.Rejected);
            var error = InventionSpec.Parse(new LegacyInterpretResponse { status = "error", name = "lamp", traits = new[] { "glows" } });
            Assert.IsFalse(error.Has(Trait.Glows), "an error reply's attributes aren't trusted");
        }

        [Test]
        public void LabelTableMapsToAttributes()
        {
            Assert.IsTrue(InventionSpec.FromLabel("Light").Has(Trait.Glows));
            Assert.IsTrue(InventionSpec.FromLabel("fish").Has(Trait.Edible));
            Assert.IsTrue(InventionSpec.FromLabel("cat").Has(Trait.Alive));
            var unknown = InventionSpec.FromLabel("teapot");
            Assert.AreEqual("teapot", unknown.Name);
            Assert.AreEqual(MaterialKind.Wood, unknown.Material);
            Assert.IsFalse(unknown.Has(Trait.Glows) || unknown.Has(Trait.Edible));
        }

        [Test]
        public void SnakeMatchesTheContract()
        {
            Assert.AreEqual("lighter_than_air", InventionSpec.Snake(Trait.LighterThanAir));
            Assert.AreEqual("glows", InventionSpec.Snake(Trait.Glows));
        }

        [Test]
        public void SurfaceFollowsTraits()
        {
            Assert.AreEqual(float.PositiveInfinity, InventionSurface.For(InventionSpec.FromLabel("bridge")).Grip);
            Assert.Less(InventionSurface.For(InventionSpec.Parse(Reply("ice"))).Grip, 10f);
            Assert.Greater(InventionSurface.For(InventionSpec.Parse(Reply("rubber"))).Bounce, 0f);
            Assert.Less(InventionSurface.For(InventionSpec.Parse(Reply("jelly"))).SpeedScale, 1f);
        }

        // ---- Feasibility --------------------------------------------------------------------------------

        [Test]
        public void SupportBuildsAsIs()
        {
            var f = InventionFeasibility.Check(InventionSpec.FromLabel("bridge"), true);
            Assert.AreEqual(Verdict.Ok, f.Verdict);
        }

        [Test]
        public void BackendRejectionIsRejectedWithItsReason()
        {
            var r = Reply();
            r.status = "rejected";
            r.message = "A black hole would eat the level.";
            var f = InventionFeasibility.Check(InventionSpec.Parse(r), true);
            Assert.AreEqual(Verdict.Rejected, f.Verdict);
            Assert.AreEqual("A black hole would eat the level.", f.Message);
        }

        [Test]
        public void NoRoomIsRejected()
        {
            var f = InventionFeasibility.Check(InventionSpec.FromLabel("bridge"), false);
            Assert.AreEqual(Verdict.Rejected, f.Verdict);
            Assert.AreEqual(InventionFeasibility.NoRoomLine, f.Message);
        }

        [Test]
        public void UnbuiltPartsDowngradeToSolid()
        {
            var r = Reply();
            r.name = "rocket";
            r.components = new[] { Part("support"), new InterpretComponent { type = "thrust", acceleration = 10f, duration_s = 1f } };
            var f = InventionFeasibility.Check(InventionSpec.Parse(r), true);
            Assert.AreEqual(Verdict.Downgraded, f.Verdict);
            Assert.IsTrue(f.Builds);
            Assert.AreEqual(new[] { PartType.Thrust }, f.Unsupported.ToArray());
            StringAssert.Contains("rocket", f.Message);
        }
    }
}
