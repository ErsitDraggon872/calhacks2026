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
        bool active, everActivated, held, warnedWeakLift;
        float poweredStartedAt;
        Vector3 spawnPosition;
        Quaternion spawnRotation;

        public bool MovingPlatform => body != null ||
            (GetComponent<InventionMotion>()?.CarriesPlayer ?? false);
        public bool HasButton => thrust != null || lift != null || glide != null || roll != null ||
                                 attach != null || tether != null || pushPull != null ||
                                 (bounce != null && bounce.activation != "contact") || hinge != null;

        public static bool IsVehicleName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            // Whole-word match avoids matching 'car' inside 'cartoon' or 'carpet'.
            var words = System.Text.RegularExpressions.Regex.Split(name.ToLowerInvariant(), @"[^a-z0-9]+",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            foreach (var word in words)
            {
                switch (word)
                {
                    case "car": case "cart": case "truck": case "bus": case "taxi":
                    case "racecar": case "suv": case "trolley": case "tractor": case "wheelbarrow":
                    case "van": case "minivan": case "jeep": case "buggy": case "vehicle": case "sedan":
                    case "skateboard": case "scooter": case "bike": case "bicycle":
                    case "motorcycle": case "kart": case "train": case "wagon":
                        return true;
                }
            }
            return false;
        }

        static Vector3 Direction(int angle)
        {
            float a = angle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
        }

        public void Configure(Invention created, InterpretResponse response, string creatorIntent = "")
        {
            invention = created;
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
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

            // A recognizable car/cart must DRIVE, even when vision returns a generic
            // support or an engine-only interpretation. ROLL is its ground motor;
            // THRUST is reserved for visibly separate rockets/boosters.
            if (roll == null && lift == null && glide == null && tether == null &&
                hinge == null && response.components.Length < 4 &&
                (IsVehicleName(response.name) || IsVehicleName(creatorIntent)))
            {
                roll = new PhysicsPart
                {
                    type = "roll", activation = "button", angle_deg = 0,
                    acceleration = 12f, duration_s = 10f, anchor_x = .5f, anchor_y = .1f
                };
                var extended = new PhysicsPart[response.components.Length + 1];
                Array.Copy(response.components, extended, response.components.Length);
                extended[extended.Length - 1] = roll;
                response.components = extended;
                Debug.Log("[ThinkInk Vehicle] Added ROLL to recognized ground vehicle: " + response.name);
            }
            Debug.Log("[ThinkInk AI] " + response.name + ": " +
                string.Join(", ", Array.ConvertAll(response.components, p => p.type)));

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
            // A car is a real weighted Rigidbody, NOT a floating kinematic preview.
            // Mass changes how forces affect it; gravity (not mass) determines fall rate.
            bool groundVehicle = roll != null && lift == null && glide == null;
            body.mass = groundVehicle ? Mathf.Max(8f, traits.mass_kg) : traits.mass_kg;
            body.linearDamping = groundVehicle ? Mathf.Min(0.12f, traits.drag * 0.3f) : traits.drag * 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            if (groundVehicle || (hinge == null && traits.stability > .45f))
                body.constraints |= RigidbodyConstraints.FreezeRotationZ;
            if (groundVehicle)
            {
                // Cars need weight while DRIVING, not while their freshly spawned
                // sketch waits for Player B.  Otherwise a car spawned next to the
                // cliff can disappear before the player reaches it.  BeginMotion()
                // releases the Rigidbody with gravity enabled on the first E.
                body.centerOfMass = new Vector3(0f, Mathf.Max(0.06f, created.Size.y * .22f), 0f);
                body.isKinematic = true;
                body.useGravity = false;
                Debug.Log("[ThinkInk Vehicle] Parked on spawn; E starts weighted driving, gravity ON after launch.");
            }
            else
            {
                // Flying inventions are parked until activated so players can board.
                body.isKinematic = true;
                body.useGravity = false;
            }
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

        /// <summary>
        /// Explicitly requested self-driving vehicles use the same ROLL motor as E.
        /// Ordinary cars still wait for E, preserving player control.
        /// </summary>
        public bool StartAutonomousDrive()
        {
            if (roll == null || body == null || !body.isKinematic) return false;
            BeginMotion();
            Debug.Log("[ThinkInk Life] Self-driving ROLL activated.");
            return true;
        }

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
                // CharacterController marks the player as riding on ordinary contact.
                // The FIRST E press must START a parked vehicle, not dismount it.
                if (!everActivated && body != null &&
                    (roll != null || thrust != null || lift != null || glide != null))
                {
                    if (Time.time < nextUseAt) return false;
                    nextUseAt = Time.time + traits.cooldown_s;
                    rider = player;
                    player.BoardInvention(this, invention, 120f);
                    BeginMotion();
                    Debug.Log("[ThinkInk Vehicle] First E started " + (roll != null ? "ROLL" : "powered invention"));
                    return true;
                }
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
                Debug.Log("[ThinkInk Input] E activated " + (roll != null ? "ROLL" : "invention"));
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
                    Subtitles.ShowNow("Too much weight! Draw a sturdier bridge.", 3.5f);
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
            poweredStartedAt = Time.time;
            warnedWeakLift = false;
            body.isKinematic = false;
            body.useGravity = true;
            body.WakeUp();
            float duration = Mathf.Max(thrust != null ? thrust.duration_s : 0f,
                Mathf.Max(lift != null ? lift.duration_s : 0f, roll != null ? roll.duration_s : 0f));
            if (duration <= 0f) duration = traits.duration_s;
            // A normal car is not a short-fuse rocket: let the engine run long
            // enough for the explorer to cross a level.
            if (roll != null && thrust == null && lift == null)
                duration = Mathf.Max(duration, 15f);
            // Preserve old rocket fuel duration; batteries alter newer powered modes.
            float energyFactor = (thrust != null || (roll != null && lift == null)) ? 1f :
                Mathf.Lerp(.4f, 1f, traits.energy_capacity);
            poweredUntil = Time.time + duration * energyFactor;
            // Keep existing rocket feel: boosted rocket temporarily overcomes gravity.
            if (thrust != null && lift == null && roll == null) body.useGravity = false;
            // Cars keep gravity even with a rocket attached, so they can drive off edges.
            if (roll != null && lift == null) body.useGravity = true;
        }

        // Falling off a cliff is still possible.  But the invention isn't lost:
        // restore its original spot so the creator can retry without another AI call.
        void ResetAfterFall()
        {
            if (rider != null && rider.IsRiding(this)) rider.Disembark(this);
            rider = null;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            Physics.SyncTransforms();
            active = false;
            held = false;
            everActivated = false;
            nextUseAt = Time.time + 0.4f;
            Debug.Log("[ThinkInk] Invention fell off the level; restored for another try.");
            Subtitles.ShowNow("Your invention fell! Try driving it differently or redraw it.", 3f);
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic) return;
            // Don't delete moving inventions when they fall under the map.
            if (transform.position.y < spawnPosition.y - 5f)
            {
                ResetAfterFall();
                return;
            }
            bool powered = active && Time.time < poweredUntil && (!HoldMode || held);
            if (active && !powered)
            {
                active = false;
                body.useGravity = true;
                if (thrust != null && rider != null && rider.IsRiding(this))
                    Subtitles.ShowNow("Out of fuel! Try a bigger booster or jump off.", 3.5f);
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
                if (powered)
                {
                    // Small, consistent horizontal movement makes bird drawings
                    // useful for the Great Gap, without granting unlimited flight.
                    float sign = Mathf.Cos(traits.direction_deg * Mathf.Deg2Rad) < -0.2f ? -1f : 1f;
                    body.AddForce(Vector3.right * sign * Mathf.Lerp(1.5f, 3.2f, traits.speed),
                        ForceMode.Acceleration);
                    if (rider != null && !warnedWeakLift &&
                        traits.carry_capacity_kg < DemoPlayerMassKg && Time.time - poweredStartedAt > 0.75f)
                    {
                        warnedWeakLift = true;
                        Subtitles.ShowNow("The wings can't carry you! Try drawing a bigger bird.", 4f);
                    }
                }
            }
            if (powered && roll != null)
            {
                float sign = Mathf.Cos(roll.angle_deg * Mathf.Deg2Rad) < 0f ? -1f : 1f;
                float targetSpeed = sign * Mathf.Lerp(4.5f, 9f, traits.speed);
                float acceleration = Mathf.Max(5f, roll.acceleration * Mathf.Max(.5f, traits.force));
                // Irregular box colliders can have enormous friction/briefly lose
                // ground contacts. Target horizontal velocity gives dependable
                // motion while physics still handles gravity and collisions.
                var velocity = body.linearVelocity;
                velocity.x = Mathf.MoveTowards(velocity.x, targetSpeed,
                    acceleration * Time.fixedDeltaTime);
                body.linearVelocity = velocity;
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
