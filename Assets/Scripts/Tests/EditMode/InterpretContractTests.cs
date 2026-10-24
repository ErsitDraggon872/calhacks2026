using NUnit.Framework;

namespace CalHacks.Tests
{
    public class InterpretContractTests
    {
        [Test]
        public void RocketResponseValidAndClamped()
        {
            var rocket = new InterpretResponse
            {
                status = "ok", width_m = 7f,
                components = new[] {
                    new PhysicsPart { type = "support" },
                    new PhysicsPart { type = "THRUST", activation = "button", acceleration = 30,
                        angle_deg = 45, duration_s = 5, anchor_x = -0.5f, anchor_y = 0.5f }
                }
            };
            Assert.That(InterpretContract.Validate(rocket, out var error), Is.True, error);
            Assert.That(rocket.width_m, Is.EqualTo(6f));
            Assert.That(rocket.components[1].type, Is.EqualTo("thrust"));
            Assert.That(rocket.components[1].acceleration, Is.EqualTo(25f));
            Assert.That(rocket.components[1].duration_s, Is.EqualTo(3f));
            Assert.That(rocket.components[1].anchor_x, Is.EqualTo(0f));
        }

        [Test]
        public void RejectedPowerStillSpawnsAsSupport()
        {
            var result = new InterpretResponse { status = "rejected", message = "Try drawing a spring.", components = new[] {new PhysicsPart{type="support"}}};
            Assert.That(InterpretContract.Validate(result, out var error), Is.True, error);
            Assert.That(result.status, Is.EqualTo("ok"));
            Assert.That(result.components.Length, Is.EqualTo(1));
        }

        [Test]
        public void InappropriateDrawingIsStillRejected()
        {
            var response = new InterpretResponse { status = "rejected", message = "Try drawing a tool.",
                components = new[] { new PhysicsPart { type = "support" } } };
            Assert.That(InterpretContract.Validate(response, out _), Is.False);
        }

        [Test]
        public void VehiclesMatchWholeWordsOnly()
        {
            Assert.IsTrue(InventionPhysics.IsVehicleName("rocket car"));
            Assert.IsTrue(InventionPhysics.IsVehicleName("go-kart"));
            Assert.IsTrue(InventionPhysics.IsVehicleName("skateboard"));
            Assert.IsTrue(InventionPhysics.IsVehicleName("cart"));
            Assert.IsTrue(InventionPhysics.IsVehicleName("taxi"));
            Assert.IsTrue(InventionPhysics.IsVehicleName("racecar"));
            Assert.IsTrue(InventionPhysics.IsVehicleName("trolley"));
            Assert.IsFalse(InventionPhysics.IsVehicleName("cartoon bird"));
            Assert.IsFalse(InventionPhysics.IsVehicleName("flying carpet"));
        }

        [Test]
        public void RejectsMissingSupport()
        {
            var result = new InterpretResponse { status = "ok", components = new[] {new PhysicsPart {type="bounce"}}};
            Assert.That(InterpretContract.Validate(result, out _), Is.False);
        }
        [Test]
        public void NewAbilityCombinationAndAttributesAreValidated()
        {
            var bird = new InterpretResponse
            {
                status = "ok", width_m = 5f,
                attributes = new InventionAttributes { mass_kg = 4, carry_capacity_kg = 30,
                    friction = -2, energy_capacity = 4, range_m = 50 },
                components = new[] {
                    new PhysicsPart { type = "support" },
                    new PhysicsPart { type = "LIFT", activation = "button", acceleration = 18, duration_s = 6 },
                    new PhysicsPart { type = "ATTACH", activation = "button" },
                    new PhysicsPart { type = "GLIDE", activation = "always", launch_speed = 2 }
                }
            };
            Assert.IsTrue(InterpretContract.Validate(bird, out var error), error);
            Assert.AreEqual("lift", bird.components[1].type);
            Assert.AreEqual("attach", bird.components[2].type);
            Assert.AreEqual(20f, bird.attributes.carry_capacity_kg);
            Assert.AreEqual(0f, bird.attributes.friction);
            Assert.AreEqual(1f, bird.attributes.energy_capacity);
            Assert.AreEqual(8f, bird.attributes.range_m);
        }

        [Test]
        public void TooManyOrDuplicateAbilitiesFailClosed()
        {
            var extra = new InterpretResponse { status="ok", components = new[] {
                new PhysicsPart{type="support"}, new PhysicsPart{type="lift"},
                new PhysicsPart{type="glide"}, new PhysicsPart{type="attach"},
                new PhysicsPart{type="roll"} } };
            Assert.IsFalse(InterpretContract.Validate(extra, out _));
            var dup = new InterpretResponse { status="ok", components = new[] {
                new PhysicsPart{type="support"}, new PhysicsPart{type="lift"},
                new PhysicsPart{type="LIFT"} } };
            Assert.IsFalse(InterpretContract.Validate(dup, out _));
        }

        [Test]
        public void LegacyThreePartRocketStillPasses()
        {
            var old = new InterpretResponse { status="ok", width_m=2.5f,
                components=new [] { new PhysicsPart{type="support"},
                    new PhysicsPart{type="thrust", activation="button", acceleration=15, duration_s=1.5f} } };
            Assert.IsTrue(InterpretContract.Validate(old, out var error), error);
            Assert.IsNotNull(old.attributes);
        }

    }
}
