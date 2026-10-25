using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Lightweight, deterministic 'bring the sketch to life' motion. This is not an
    /// animal AI or articulated animation system: it moves the existing cutout and
    /// compound colliders together. The AI/body/physics contract is unchanged.
    /// </summary>
    public sealed class InventionMotion : MonoBehaviour
    {
        public enum Kind { Static, Wander, Follow, Hop, Fly, RollMove, Oscillate }

        Invention invention;
        InventionPhysics physicsBehavior;
        Rigidbody rb;
        BoxCollider[] shape;
        Transform visual;
        Cutout facingCutout;
        PlayerController player;
        Kind kind;
        Vector3 home;
        Quaternion originalVisualRotation;
        float speed;
        float patrolRange;
        float direction = 1f;
        float phase;
        float createdAt;
        float carryCapacity;
        bool warnedCarry;
        bool squashed;
        float nextSquishAt;
        const float SquishCooldownSeconds = .4f;
        const float SpawnSquashGraceSeconds = .9f;
        const float MinSquashFallSpeed = 2.7f;

        public bool CarriesPlayer => rb != null;
        public Kind Behavior => kind;

        /// <summary>Only living creatures can be stomped, not bridges or vehicles.</summary>
        public static bool CanSquash(Kind behavior) =>
            behavior == Kind.Wander || behavior == Kind.Follow ||
            behavior == Kind.Hop || behavior == Kind.Fly;

        /// <summary>Shared by the CharacterController collision and EditMode tests.</summary>
        public static bool IsSquashLanding(Kind behavior, float fallVelocity,
            float contactNormalY, float movementY, float aliveSeconds) =>
            CanSquash(behavior) && fallVelocity < -MinSquashFallSpeed &&
            contactNormalY > .55f && movementY < -.04f &&
            aliveSeconds >= SpawnSquashGraceSeconds;

        /// <summary>A stomp only kills glass or ice creatures; the rest squish and live.</summary>
        public static bool BreaksWhenStomped(InventionSpec spec) =>
            spec != null && spec.Has(Trait.Fragile);

        /// <summary>Called only for a downward CharacterController contact. True bounces the player.</summary>
        public bool TrySquash(float fallVelocity, ControllerColliderHit hit)
        {
            if (squashed || hit == null || Time.time < nextSquishAt ||
                !IsSquashLanding(kind, fallVelocity, hit.normal.y,
                    hit.moveDirection.y, Time.time - createdAt)) return false;

            if (!BreaksWhenStomped(invention.Spec))
            {
                nextSquishAt = Time.time + SquishCooldownSeconds;
                if (facingCutout != null) facingCutout.PlaySquish();
                return true;
            }

            squashed = true;
            // World-space effect survives destroying this invention.
            var center = visual != null ? visual.position :
                transform.position + Vector3.up * Mathf.Max(.2f, invention.Size.y * .4f);
            SpawnPaperPoof(center);
            Debug.Log("[ThinkInk Life] " + gameObject.name + " squashed — paper poof!");
            // Disable collisions immediately, so a second callback this frame
            // cannot squash the same sketch a second time.
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            Destroy(gameObject);
            return true;
        }

        /// <summary>The paper-confetti burst a drawing dies in (a stomp, the gatekeeper's sword). Survives the drawing.</summary>
        public static void SpawnPaperPoof(Vector3 position)
        {
            var effect = new GameObject("ThinkInk paper confetti poof");
            effect.transform.position = position;
            var particles = effect.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = .5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.09f, .19f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(.98f, .90f, .55f, 1f), new Color(.52f, .85f, .98f, 1f));
            main.gravityModifier = .55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = .12f;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // Use the game's available sprite shader instead of relying on the
            // built-in default particle shader, which may be pink in URP.
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var material = new Material(shader);
                renderer.sharedMaterial = material;
                Destroy(material, 2f); // Effect auto-destroys at end of burst.
            }
            particles.Emit(22);
            particles.Play();
        }

        // Cars normally wait for E. The Creator must explicitly request autonomy.
        public static bool WantsAutonomousDrive(string intent) => HasPhrase(intent,
            "drives itself", "drive itself", "drive automatically", "self-driving",
            "self driving", "roll on its own", "drives on its own", "drive on its own",
            "moves on its own", "move on its own");

        static bool HasWord(string source, string word) =>
            Regex.IsMatch(source ?? "", @"\b" + Regex.Escape(word) + @"\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static bool HasAny(string text, params string[] words)
        {
            foreach (var word in words)
                if (HasWord(text, word)) return true;
            return false;
        }

        static bool HasPhrase(string text, params string[] phrases)
        {
            foreach (var phrase in phrases)
                if ((text ?? "").IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static string ToWire(Kind value)
        {
            switch (value)
            {
                case Kind.RollMove: return "roll_move";
                default: return value.ToString().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Explicit creator intent beats species heuristics. Optional response field
        /// is also honored, but the existing Bitdeer JSON works without it.
        /// </summary>
        public static Kind Choose(string name, string message, string intent, string declared = null)
        {
            var description = (intent ?? "").Trim();
            if (HasPhrase(description, "stay still", "remain still", "don't move", "do not move", "stationary") ||
                HasAny(description, "static")) return Kind.Static;
            if (HasPhrase(description, "follow me", "follow the player", "follows me", "follows the player")) return Kind.Follow;
            if (HasPhrase(description, "hop around", "jump around", "keep hopping", "keep jumping") ||
                HasAny(description, "hop", "hopping")) return Kind.Hop;
            if (WantsAutonomousDrive(description)) return Kind.RollMove;
            if (HasPhrase(description, "spin around", "spin in place", "rotate in place", "wiggle in place", "wave its arms")) return Kind.Oscillate;
            if (HasPhrase(description, "fly around", "float around", "flies around", "flutter around") ||
                HasAny(description, "fly", "flying", "flap", "float")) return Kind.Fly;
            if (HasPhrase(description, "walk around", "move around", "roam around", "wander around", "walk by itself") ||
                HasAny(description, "wander", "patrol")) return Kind.Wander;

            switch ((declared ?? "").Trim().ToLowerInvariant())
            {
                case "static": return Kind.Static;
                case "wander": return Kind.Wander;
                case "follow": return Kind.Follow;
                case "hop": return Kind.Hop;
                case "fly": return Kind.Fly;
                case "roll_move": return Kind.RollMove;
                case "oscillate": return Kind.Oscillate;
            }

            // Use the player's optional identification when the sketch is rough
            // and the vision model responds with a generic name.
            var identity = (name ?? "") + " " + (intent ?? "");
            if (HasAny(identity, "frog", "rabbit", "bunny", "kangaroo", "grasshopper", "toad")) return Kind.Hop;
            if (HasAny(identity, "bird", "eagle", "sparrow", "bee", "butterfly", "dragonfly", "bat", "pigeon", "owl", "helicopter", "balloon", "airplane", "plane", "glider", "kite", "flying", "winged"))
                return Kind.Fly;
            if (HasAny(identity, "cat", "dog", "puppy", "kitten", "turtle", "crab", "giraffe", "horse", "cow", "pig", "elephant", "dinosaur", "robot", "duck", "fox", "bear", "snail", "snake"))
                return Kind.Wander;
            if (InventionPhysics.IsVehicleName(identity)) return Kind.RollMove;
            if (HasAny(identity, "fan", "windmill", "propeller", "pendulum", "clock")) return Kind.Oscillate;

            // A weak visual description can still help when the object name is
            // unknown, but do not treat an arbitrary 'move across gap' as motion.
            if (HasPhrase(message, "wanders around", "walks around", "roams around")) return Kind.Wander;
            if (HasPhrase(message, "flies around", "flutters around")) return Kind.Fly;
            return Kind.Static;
        }

        /// <summary>The motion for a free build on main, which has no Creator intent text: name and message only.</summary>
        public static Kind For(InventionSpec spec) => spec == null ? Kind.Static : Choose(spec.Name, spec.Message, "");

        public void Configure(Invention created, InventionPhysics behavior, Kind chosen)
        {
            invention = created;
            physicsBehavior = behavior;
            kind = chosen;
            home = transform.position;
            createdAt = Time.time;
            player = FindFirstObjectByType<PlayerController>();
            facingCutout = created.Cutout;
            visual = facingCutout != null ? facingCutout.transform : null;
            if (visual != null) originalVisualRotation = visual.localRotation;

            var attrs = created.Spec.Attributes;
            carryCapacity = attrs.carry_capacity_kg;
            speed = Mathf.Lerp(.65f, 1.65f, attrs.speed);
            patrolRange = Mathf.Clamp(attrs.range_m, 1.2f, 2.8f);
            phase = Mathf.Abs(home.x * 1.37f) % (2f * Mathf.PI);

            if (kind == Kind.RollMove)
            {
                // The vehicle motor lives in InventionPhysics. Only read its
                // velocity to flip the artwork; never replace its Rigidbody.
                rb = GetComponent<Rigidbody>();
            }
            if (kind == Kind.Wander || kind == Kind.Follow || kind == Kind.Hop || kind == Kind.Fly)
            {
                rb = GetComponent<Rigidbody>();
                if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
                // Autonomous motion moves the whole shape using a kinematic body.
                // Existing powered physics takes over when E changes it to dynamic.
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.constraints |= RigidbodyConstraints.FreezePositionZ |
                                  RigidbodyConstraints.FreezeRotationX |
                                  RigidbodyConstraints.FreezeRotationY |
                                  RigidbodyConstraints.FreezeRotationZ;
            }

            Debug.Log("[ThinkInk Life] " + created.Spec.Name + " → " + ToWire(kind));
        }

        void FixedUpdate()
        {
            if (kind == Kind.RollMove)
            {
                if (rb != null && !rb.isKinematic && Mathf.Abs(rb.linearVelocity.x) > .12f)
                    FaceDirection(rb.linearVelocity.x);
                return;
            }
            if (kind == Kind.Static || kind == Kind.Oscillate) return;
            if (rb == null || !rb.isKinematic) return; // Powered Unity physics owns the body after E.
            if (kind == Kind.Fly) { Fly(); return; }
            WalkOrHop();
        }

        void FaceDirection(float horizontal)
        {
            if (facingCutout != null) facingCutout.FaceHorizontal(horizontal);
        }

        void Update()
        {
            if (kind != Kind.Oscillate || visual == null) return;
            // Whole-silhouette animation only: avoid moving actual collision pieces.
            float degrees = Mathf.Sin(Time.time * 3f + phase) * 7f;
            visual.localRotation = originalVisualRotation * Quaternion.Euler(0f, 0f, degrees);
        }

        bool TryGround(float x, float z, float currentBottomY, out float groundY)
        {
            groundY = 0f;
            var origin = new Vector3(x, currentBottomY + 2.8f, z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 4.2f, Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            float highest = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < .65f) continue;
                if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (hit.collider.GetComponentInParent<Invention>() != null) continue;
                if (Mathf.Abs(hit.point.y - currentBottomY) > .6f) continue;
                highest = Mathf.Max(highest, hit.point.y);
            }
            if (float.IsNegativeInfinity(highest)) return false;
            groundY = highest;
            return true;
        }

        bool BlockedAhead(float sign)
        {
            var origin = transform.position + Vector3.up * Mathf.Clamp(invention.Size.y * .45f, .3f, .9f);
            var hits = Physics.RaycastAll(origin, Vector3.right * sign,
                invention.Size.x * .45f + .22f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (hit.collider.GetComponentInParent<Invention>() != null) continue;
                if (Mathf.Abs(hit.normal.y) < .4f) return true;
            }
            return false;
        }

        void WalkOrHop()
        {
            var p = rb.position;
            float nextDirection = direction;
            if (kind == Kind.Follow && player != null && !player.InputLocked)
            {
                var dx = player.transform.position.x - p.x;
                if (Mathf.Abs(dx) > .95f && Mathf.Abs(dx) < 8f)
                    nextDirection = Mathf.Sign(dx);
                else nextDirection = 0f;
            }
            else if (Mathf.Abs(p.x - home.x) >= patrolRange)
                nextDirection = -Mathf.Sign(p.x - home.x);

            if (nextDirection != 0f)
            {
                float desiredX = p.x + nextDirection * speed * Time.fixedDeltaTime;
                float floor;
                float probeX = desiredX + nextDirection * (invention.Size.x * .36f + .2f);
                if (!TryGround(probeX, p.z, home.y, out floor) || BlockedAhead(nextDirection))
                {
                    if (kind != Kind.Follow)
                    {
                        direction = -nextDirection;
                        FaceDirection(direction); // Turn around at the cliff/wall.
                    }
                    nextDirection = 0f;
                }
                else
                {
                    direction = nextDirection;
                    FaceDirection(direction); // Also turn when FOLLOW changes sides.
                    p.x = desiredX;
                    // Follow terrain gently rather than hovering after small floor changes.
                    p.y = Mathf.MoveTowards(p.y, floor + .02f, 1.5f * Time.fixedDeltaTime);
                }
            }

            if (kind == Kind.Hop)
            {
                // Calculate each hop relative to ground. Never accumulate height
                // frame-to-frame (that would make frogs float into the sky).
                float floor;
                if (TryGround(p.x, p.z, home.y, out floor))
                {
                    float cycle = Mathf.Repeat(Time.fixedTime * 1.2f + phase, 1.5f);
                    float hopHeight = cycle < 1f ? Mathf.Sin(cycle * Mathf.PI) * .65f : 0f;
                    p.y = floor + .02f + hopHeight;
                }
            }
            var safe = ConstrainMotion(p);
            if (Mathf.Abs(safe.x - p.x) > .0001f && kind != Kind.Follow)
            {
                direction = -direction;
                FaceDirection(direction);
            }
            rb.MovePosition(safe);
        }

        void Fly()
        {
            var p = rb.position;
            var x = p.x + direction * speed * .65f * Time.fixedDeltaTime;
            if (Mathf.Abs(x - home.x) > patrolRange)
            {
                direction = -direction;
                x = Mathf.Clamp(x, home.x - patrolRange, home.x + patrolRange);
            }
            FaceDirection(direction); // Birds turn visually at the end of each patrol.
            // A small bird can fly alone but should not magically lift a rider.
            bool carrying = player != null && physicsBehavior != null && player.IsRiding(physicsBehavior);
            bool overloaded = carrying && carryCapacity < InventionPhysics.DemoPlayerMassKg;
            if (overloaded && !warnedCarry)
            {
                warnedCarry = true;
                Subtitles.ShowNow("Too heavy for these wings! Draw a bigger bird.", 3f);
            }
            // Stable cruise near takeoff altitude; weak lift gradually loses height.
            float targetY = overloaded ? home.y - 1.5f :
                home.y + .75f + Mathf.Sin(Time.fixedTime * 2.2f + phase) * .16f;
            var desired = new Vector3(x, Mathf.MoveTowards(p.y, targetY,
                1.8f * Time.fixedDeltaTime), home.z);
            var safe = ConstrainMotion(desired);
            if (Mathf.Abs(safe.x - desired.x) > .0001f)
            {
                direction = -direction;
                FaceDirection(direction);
            }
            rb.MovePosition(safe);
        }

        /// <summary>
        /// Kinematic MovePosition does not stop at static walls. Sweep each ink collider before moving, checking X
        /// and Y separately so a bird can travel sideways under a ceiling. Other drawings and the rider do not block.
        /// </summary>
        public Vector3 ConstrainMotion(Vector3 target)
        {
            var origin = rb != null ? rb.position : transform.position;
            shape ??= GetComponentsInChildren<BoxCollider>();
            var offset = Vector3.zero;
            for (var axis = 0; axis < 3; axis++)
            {
                var delta = target[axis] - origin[axis];
                if (Mathf.Abs(delta) < .00001f) continue;
                var heading = Vector3.zero;
                heading[axis] = Mathf.Sign(delta);
                var distance = Mathf.Abs(delta);
                const float skin = .01f;
                foreach (var box in shape)
                {
                    if (box == null || !box.enabled) continue;
                    var bounds = box.bounds;
                    var extents = Vector3.Max(bounds.extents - Vector3.one * skin, Vector3.one * .001f);
                    foreach (var hit in Physics.BoxCastAll(bounds.center + offset, extents, heading,
                                 Quaternion.identity, distance + skin, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.collider.GetComponentInParent<Invention>() != null ||
                            hit.collider.GetComponentInParent<PlayerController>() != null) continue;
                        if (Vector3.Dot(hit.normal, heading) >= -.001f) continue;
                        distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - skin));
                    }
                }
                offset[axis] = Mathf.Sign(delta) * distance;
            }
            return origin + offset;
        }

        void OnDestroy()
        {
            if (visual != null && kind == Kind.Oscillate)
                visual.localRotation = originalVisualRotation;
        }
    }
}
