using System;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Composable side-view 3D physics for an AI paper cutout. All abilities use the same
    /// compound BoxColliders and CharacterController player; no object-specific prefabs.
    /// Gameplay mass/force units are simplified for a short cooperative puzzle demo.
    /// </summary>
    public sealed class InventionPhysics : MonoBehaviour
    {
        public const float DemoPlayerMassKg = 6f;
        PhysicsPart bounce, thrust, lift, glide, roll, attach, tether, hinge, pushPull;
        InventionAttributes traits;
        Invention invention;
        Rigidbody body;
        PhysicsMaterial surfaceMaterial;
        PlayerController rider;
        Vector3 tetherWorldAnchor;
        LineRenderer ropeLine;
        float poweredUntil, nextUseAt, nextBounceAt, overloadSince = -1f;
        bool active, everActivated, held;

        public bool MovingPlatform => body != null;
        public bool HasButton => thrust != null || lift != null || glide != null || roll != null ||
                                 attach != null || tether != null || pushPull != null ||
                                 (bounce != null && bounce.activation != "contact") || hinge != null;

        static Vector3 Direction(int angle)
        {
            float a = angle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
        }

        public void Configure(Invention created, InterpretResponse response)
        {
            invention = created;
            traits = response.attributes ?? new InventionAttributes();
            traits.Normalize();
            foreach (var part in response.components)
            {
                switch (part.type)
                {
                    case "bounce": bounce = part; break;
                    case "thrust": thrust = part; break;
                    case "lift": lift = part; break;
                    case "glide": glide = part; break;
                    case "roll": roll = part; break;
                    case "attach": attach = part; break;
                    case "tether": tether = part; break;
                    case "hinge": hinge = part; break;
                    case "push_pull": pushPull = part; break;
                }
            }

            // Surface friction/restitution are attributes rather than separate components.
            surfaceMaterial = new PhysicsMaterial("ThinkInk temporary surface")
            {
                dynamicFriction = traits.friction,
                staticFriction = traits.friction,
                bounciness = traits.elasticity
            };
            foreach (var box in GetComponentsInChildren<BoxCollider>()) box.sharedMaterial = surfaceMaterial;

            bool dynamicShape = thrust != null || lift != null || glide != null || roll != null ||
                                tether != null || hinge != null;
            if (!dynamicShape) return;
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = traits.mass_kg;
            body.linearDamping = traits.drag * 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            if (hinge == null && traits.stability > .45f)
                body.constraints |= RigidbodyConstraints.FreezeRotationZ;

            // Inventions are parked until E/contact. Otherwise a tiny bird or rocket
            // would fall into the gap before Player B can board it.
            body.isKinematic = true;
            body.useGravity = false;
            if (tether != null) SetupRope();
            if (hinge != null) SetupHinge();
        }

        void OnDestroy()
        {
            if (surfaceMaterial != null) Destroy(surfaceMaterial);
            if (ropeLine != null && ropeLine.material != null) Destroy(ropeLine.material);
        }

        Vector3 LocalAnchor(PhysicsPart part)
        {
            return new Vector3((part.anchor_x - .5f) * invention.Size.x,
                part.anchor_y * invention.Size.y, 0f);
        }

        void SetupHinge()
        {
            Vector3 local = LocalAnchor(hinge);
            var joint = gameObject.AddComponent<HingeJoint>();
            joint.axis = Vector3.forward;
            joint.anchor = local;
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = transform.TransformPoint(local);
            joint.useLimits = true;
            joint.limits = new JointLimits { min = -75f, max = 75f };
        }

        void SetupRope()
        {
            // Fixed overhead anchor (for the demo): no imaginary wall hook is spawned.
            float length = traits.range_m;
            tetherWorldAnchor = transform.TransformPoint(LocalAnchor(tether)) + Vector3.up * length;
            var joint = gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = LocalAnchor(tether);
            joint.connectedAnchor = tetherWorldAnchor;
            joint.xMotion = ConfigurableJointMotion.Limited;
            joint.yMotion = ConfigurableJointMotion.Limited;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.linearLimit = new SoftJointLimit { limit = length };
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Free;

            ropeLine = gameObject.AddComponent<LineRenderer>();
            ropeLine.positionCount = 2;
            ropeLine.startWidth = ropeLine.endWidth = .035f;
            ropeLine.useWorldSpace = true;
            ropeLine.startColor = ropeLine.endColor = Color.black;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) ropeLine.material = new Material(shader);
        }

        void LateUpdate()
        {
            if (ropeLine != null)
            {
                ropeLine.SetPosition(0, tetherWorldAnchor);
                ropeLine.SetPosition(1, transform.TransformPoint(LocalAnchor(tether)));
            }
        }

        public void SetHeld(bool isHeld) { held = isHeld; }

        bool ToggleMode => (thrust != null && thrust.activation == "toggle") ||
                           (lift != null && lift.activation == "toggle") ||
                           (roll != null && roll.activation == "toggle");
        bool HoldMode => (thrust != null && thrust.activation == "hold") ||
                         (lift != null && lift.activation == "hold") ||
                         (roll != null && roll.activation == "hold");

        public bool TryInteract(PlayerController player)
        {
            if (player == null || player.InputLocked || !HasButton) return false;
            if (player.IsRiding(this))
            {
                if (ToggleMode)
                {
                    active = !active;
                    if (active) BeginMotion();
                    else if (body != null) body.useGravity = true;
                    return true;
                }
                if (HoldMode) { BeginMotion(); return true; }
                // E is also the dismount key; Space still jumps off.
                player.Disembark(this);
                rider = null;
                return true;
            }
            if (Time.time < nextUseAt) return false;
            nextUseAt = Time.time + traits.cooldown_s;

            if (bounce != null && bounce.activation == "button") Bounce(player);
            if (pushPull != null) PushPull(player);

            if (body != null || attach != null)
            {
                // ATTACH is explicit for baskets/claws; existing rocket/skateboard
                // handling is preserved so old inventions still ride on pressing E.
                if (attach != null || thrust != null || lift != null || glide != null || roll != null || tether != null)
                {
                    rider = player;
                    player.BoardInvention(this, invention, 120f);
                }
                BeginMotion();
            }
            return true;
        }

        public void PlayerContact(PlayerController player, ControllerColliderHit hit)
        {
            if (player == null || hit == null) return;
            bool landing = hit.normal.y > .45f && hit.moveDirection.y < -.05f;
            if (landing && bounce != null && bounce.activation == "contact" && Time.time >= nextBounceAt)
                Bounce(player);
            if (landing && (thrust?.activation == "contact" || lift?.activation == "contact" ||
                            roll?.activation == "contact")) BeginMotion();

            if (landing && traits.load_capacity_kg < DemoPlayerMassKg && traits.durability < .9f)
            {
                if (overloadSince < 0) overloadSince = Time.time;
                if (Time.time - overloadSince > Mathf.Lerp(.12f, 1.1f, traits.durability))
                {
                    // Brittle bridge/platform fails visibly under too much load.
                    Debug.Log("[ThinkInk] Overloaded invention collapsed");
                    Destroy(gameObject);
                }
            }
            else if (landing) overloadSince = -1f;

            if (hinge != null && body != null && !body.isKinematic)
                body.AddTorque(Vector3.forward * player.FacingSign * (1f - traits.stability) * 3f,
                    ForceMode.Acceleration);
        }

        void BeginMotion()
        {
            if (body == null) return;
            if (active && Time.time < poweredUntil && !ToggleMode && !HoldMode) return;
            active = true;
            everActivated = true;
            body.isKinematic = false;
            body.useGravity = true;
            body.WakeUp();
            float duration = Mathf.Max(thrust != null ? thrust.duration_s : 0f,
                Mathf.Max(lift != null ? lift.duration_s : 0f, roll != null ? roll.duration_s : 0f));
            if (duration <= 0f) duration = traits.duration_s;
            // Preserve old rocket fuel duration; batteries alter newer powered modes.
            float energyFactor = thrust != null ? 1f : Mathf.Lerp(.4f, 1f, traits.energy_capacity);
            poweredUntil = Time.time + duration * energyFactor;
            // Keep existing rocket feel: boosted rocket temporarily overcomes gravity.
            if (thrust != null && lift == null) body.useGravity = false;
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic) return;
            bool powered = active && Time.time < poweredUntil && (!HoldMode || held);
            if (active && !powered)
            {
                active = false;
                body.useGravity = true;
            }
            if (rider == null || !rider.IsRiding(this)) rider = null;

            // CharacterController carries a *virtual* mass, rather than a real
            // Rigidbody. This is what makes a tiny bird sink with the explorer.
            if (rider != null && body.useGravity)
                body.AddForce(Vector3.down * DemoPlayerMassKg * 9.81f, ForceMode.Force);

            if (powered && thrust != null)
                body.AddForce(Direction(thrust.angle_deg) * thrust.acceleration * traits.force,
                    ForceMode.Acceleration);
            if ((powered || lift?.activation == "always") && lift != null)
            {
                // Sustainable upward force approximately cancels the object's
                // own weight and can lift up to carry_capacity_kg of extra load.
                // The extra lift.acceleration provides a small flapping margin.
                float newtons = (body.mass + traits.carry_capacity_kg) * 9.81f +
                                body.mass * lift.acceleration * .08f * traits.force;
                body.AddForce(Vector3.up * newtons, ForceMode.Force);
            }
            if (powered && roll != null)
            {
                float sign = (roll.angle_deg >= 90 || roll.angle_deg <= -90) ? -1f : 1f;
                body.AddForce(Vector3.right * sign * roll.acceleration * traits.force,
                    ForceMode.Acceleration);
            }
            if (glide != null && body.linearVelocity.y < -glide.launch_speed)
            {
                var v = body.linearVelocity;
                v.y = Mathf.MoveTowards(v.y, -glide.launch_speed, 20f * Time.fixedDeltaTime);
                body.linearVelocity = v;
            }
            // Normalized speed acts as a bounded design parameter for the prototype.
            float maxSpeed = Mathf.Lerp(8f, 28f, traits.speed);
            if (body.linearVelocity.magnitude > maxSpeed)
                body.linearVelocity = body.linearVelocity.normalized * maxSpeed;
        }

        void Bounce(PlayerController player)
        {
            float radians = bounce.angle_deg * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(radians), Mathf.Cos(radians), 0f);
            player.Launch(direction * bounce.launch_speed * Mathf.Lerp(.6f, 1f, traits.force));
            nextBounceAt = Time.time + Mathf.Max(.3f, traits.cooldown_s);
        }

        void PushPull(PlayerController player)
        {
            var point = transform.TransformPoint(LocalAnchor(pushPull));
            if ((player.transform.position - point).magnitude > traits.range_m + 1f) return;
            float impulse = pushPull.acceleration * traits.force * .45f;
            player.Launch(Direction(pushPull.angle_deg) * impulse);
        }
    }
}
