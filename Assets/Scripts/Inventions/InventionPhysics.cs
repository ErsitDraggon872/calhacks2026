using System.Linq;
using UnityEngine;

namespace CalHacks
{
    /// <summary>Andy's composable paper-invention abilities, adapted to the shared identity/trait spec.</summary>
    public sealed class InventionPhysics : MonoBehaviour
    {
        public const float DemoPlayerMassKg = 6f;
        InventionPart bounce, thrust, lift, glide, roll, attach;
        InventionAttributes attributes;
        Invention invention;
        Rigidbody body;
        PlayerController rider;
        float poweredUntil, nextUseAt, nextBounceAt;
        bool active, held, highlighted;
        MaterialPropertyBlock highlightBlock;
        Vector3 spawnPosition;
        Quaternion spawnRotation;

        // Andy's vehicle fallback uses whole words, so "carpet" and "cartoon" remain ordinary shapes.
        public static bool IsVehicleName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var words = System.Text.RegularExpressions.Regex.Split(name.ToLowerInvariant(), @"[^a-z0-9]+");
            return words.Any(word => word == "car" || word == "cart" || word == "truck" || word == "bus" ||
                word == "taxi" || word == "van" || word == "jeep" || word == "buggy" || word == "vehicle" ||
                word == "sedan" || word == "skateboard" || word == "scooter" || word == "bike" ||
                word == "bicycle" || word == "motorcycle" || word == "kart" || word == "train" || word == "wagon");
        }

        public bool MovingPlatform => body != null || (GetComponent<InventionMotion>()?.CarriesPlayer ?? false);        public bool HasButton => thrust != null || lift != null || glide != null || roll != null ||
            attach != null || (bounce != null && bounce.Activation != Activation.Contact);
        /// <summary>E boards it (a vehicle, flyer, roller or something to hold on to), rather than just setting it off.</summary>
        public bool Rideable => body != null || attach != null;
        bool HoldMode => new[] { thrust, lift, roll }.Any(p => p?.Activation == Activation.Hold);
        bool ToggleMode => new[] { thrust, lift, roll }.Any(p => p?.Activation == Activation.Toggle);

        /// <summary>What E does for B while riding this, or null when only Space (jump off) does anything.</summary>
        public string RideAction => RideKey(body != null && body.isKinematic, ToggleMode, HoldMode, active);

        public static string RideKey(bool parked, bool toggle, bool hold, bool active) =>
            parked ? "E go" : toggle ? (active ? "E stop" : "E go") : hold ? "hold E" : null;

        public void Configure(Invention created, InventionSpec spec)
        {
            invention = created;
            attributes = spec.Attributes;
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            bounce = spec.Parts.FirstOrDefault(p => p.Type == PartType.Bounce);
            thrust = spec.Parts.FirstOrDefault(p => p.Type == PartType.Thrust);
            lift = spec.Parts.FirstOrDefault(p => p.Type == PartType.Lift);
            glide = spec.Parts.FirstOrDefault(p => p.Type == PartType.Glide);
            roll = spec.Parts.FirstOrDefault(p => p.Type == PartType.Roll);
            attach = spec.Parts.FirstOrDefault(p => p.Type == PartType.Attach);
            if (thrust == null && lift == null && glide == null && roll == null) return;
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = attributes.mass_kg;
            body.linearDamping = attributes.drag * 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            // Park until the player can board; a rocket must not fall away before E.
            body.isKinematic = true;
            body.useGravity = false;
        }

        public void SetHeld(bool value) => held = value;

        /// <summary>Pulses a warm tint over the drawing while it's what E would use.</summary>
        public void SetHighlighted(bool value)
        {
            if (highlighted == value) return;
            highlighted = value;
            if (!value)
            {
                foreach (var r in GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(null);
            }
        }

        /// <summary>World-space bounds of what's drawn (for the E badge over it).</summary>
        public Bounds VisualBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        void Update()
        {
            if (!highlighted) return;
            highlightBlock ??= new MaterialPropertyBlock();
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            // Orange is the "you can use this" colour (the draw-here outline, the build ghost).
            highlightBlock.SetColor("_BaseColor", Color.Lerp(new Color(1f, 0.85f, 0.6f), new Color(1f, 0.62f, 0.15f), pulse));
            foreach (var r in GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(highlightBlock);
        }

        public bool TryInteract(PlayerController player)
        {
            if (player == null || player.InputLocked || !HasButton) return false;
            // On a ride boarded with E, E never gets B off; only Space does. A ride with nothing left to do ignores E.
            // Merely standing on it (support contact) isn't boarding: E below boards and locks B on.
            if (player.IsRiding(this) && player.RideLocked)
            {
                if (body != null && body.isKinematic)
                {
                    rider = player;
                    BeginMotion();
                }
                else if (ToggleMode)
                {
                    if (active) { active = false; if (body != null) body.useGravity = true; }
                    else BeginMotion();
                }
                else if (HoldMode) BeginMotion();
                else return false;
                return true;
            }
            if (Time.time < nextUseAt) return false;
            nextUseAt = Time.time + attributes.cooldown_s;
            if (bounce != null && bounce.Activation != Activation.Contact) Bounce(player);
            if (body != null || attach != null)
            {
                rider = player;
                player.BoardInvention(this, invention);
                BeginMotion();
            }
            return true;
        }

        public void PlayerContact(PlayerController player, ControllerColliderHit hit)
        {
            if (player == null || hit == null || hit.normal.y <= .45f || hit.moveDirection.y >= -.05f) return;
            // Contact boarding carries the same virtual load as pressing E.
            if (body != null) rider = player;
            if (bounce != null && bounce.Activation == Activation.Contact && Time.time >= nextBounceAt)
                Bounce(player);
            if (thrust?.Activation == Activation.Contact || lift?.Activation == Activation.Contact ||
                roll?.Activation == Activation.Contact || lift?.Activation == Activation.Always ||
                roll?.Activation == Activation.Always) BeginMotion();
        }

        void BeginMotion()
        {
            if (body == null || (active && Time.time < poweredUntil && !ToggleMode && !HoldMode)) return;
            if (!active) Juice.Play(JuiceSound.Whoosh, thrust != null ? 1f : 0.5f);
            active = true;
            body.isKinematic = false;
            body.useGravity = thrust == null || lift != null;
            body.WakeUp();
            var duration = Mathf.Max(thrust?.DurationS ?? 0f, Mathf.Max(lift?.DurationS ?? 0f, roll?.DurationS ?? 0f));
            if (duration <= 0f) duration = attributes.duration_s;
            if (roll != null && thrust == null && lift == null) duration = Mathf.Max(duration, 15f);
            var energyFactor = thrust != null || (roll != null && lift == null)
                ? 1f : Mathf.Lerp(.4f, 1f, attributes.energy_capacity);
            poweredUntil = Time.time + duration * energyFactor;
        }

        // Andy's recovery: a ride that falls off the level parks back where it was built, so the
        // Explorer can retry without another drawing or AI call.
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
            nextUseAt = Time.time + .4f;
            Subtitles.ShowNow("Your invention fell! It's back where you built it. Try again or redraw it.", 3f);
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic) return;
            if (transform.position.y < spawnPosition.y - 5f)
            {
                ResetAfterFall();
                return;
            }
            var powered = active && Time.time < poweredUntil && (!HoldMode || held);
            if (active && !powered)
            {
                active = false;
                body.useGravity = true;
                if (thrust != null && Time.time >= poweredUntil) Juice.Play(JuiceSound.Sputter);
                if (thrust != null && rider != null && rider.IsRiding(this))
                    Subtitles.ShowNow("Out of fuel! Try a bigger booster or jump off.", 3.5f);
            }
            if (rider != null && !rider.IsRiding(this)) rider = null;
            if (rider != null && body.useGravity)
                body.AddForce(Vector3.down * DemoPlayerMassKg * 9.81f, ForceMode.Force);
            if (powered && thrust != null)
            {
                var angle = thrust.AngleDeg * Mathf.Deg2Rad;
                body.AddForce(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) *
                    thrust.Acceleration * attributes.force, ForceMode.Acceleration);
            }
            if (lift != null && (powered || lift.Activation == Activation.Always))
            {
                // Anything that flies carries the rider, however small the AI judged its capacity.
                var carried = rider != null ? Mathf.Max(attributes.carry_capacity_kg, DemoPlayerMassKg) : attributes.carry_capacity_kg;
                var force = (body.mass + carried) * 9.81f +
                    body.mass * lift.Acceleration * .08f * attributes.force;
                body.AddForce(Vector3.up * force, ForceMode.Force);
                if (powered)
                {
                    var direction = Mathf.Cos(attributes.direction_deg * Mathf.Deg2Rad) < -.2f ? -1f : 1f;
                    body.AddForce(Vector3.right * direction * Mathf.Lerp(1.5f, 3.2f, attributes.speed), ForceMode.Acceleration);
                }
            }
            if (roll != null && (powered || roll.Activation == Activation.Always))
            {
                var direction = Mathf.Cos(roll.AngleDeg * Mathf.Deg2Rad) < 0f ? -1f : 1f;
                var velocity = body.linearVelocity;
                var speed = direction * Mathf.Lerp(4.5f, 9f, attributes.speed);
                var acceleration = Mathf.Max(5f, roll.Acceleration * Mathf.Max(.5f, attributes.force));
                velocity.x = Mathf.MoveTowards(velocity.x, speed, acceleration * Time.fixedDeltaTime);
                body.linearVelocity = velocity;
            }
            var gravityStep = body.useGravity
                ? Physics.gravity.y * (1f + (rider != null ? DemoPlayerMassKg / body.mass : 0f)) * Time.fixedDeltaTime : 0f;
            if (glide != null && body.linearVelocity.y + gravityStep < -glide.LaunchSpeed)
            {
                // Cancel this step's gravity and virtual rider load once terminal speed is reached.
                // A fixed acceleration correction cannot hold a light glider carrying the 6 kg rider.
                if (body.useGravity)
                    body.AddForce(-Physics.gravity * (body.mass + (rider != null ? DemoPlayerMassKg : 0f)), ForceMode.Force);
                var velocity = body.linearVelocity;
                velocity.y = -glide.LaunchSpeed;
                body.linearVelocity = velocity;
            }
            var maxSpeed = Mathf.Lerp(8f, 28f, attributes.speed);
            body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, maxSpeed);
        }

        void Bounce(PlayerController player)
        {
            var angle = bounce.AngleDeg * Mathf.Deg2Rad;
            player.Launch(new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) *
                bounce.LaunchSpeed * Mathf.Lerp(.6f, 1f, attributes.force));
            nextBounceAt = Time.time + Mathf.Max(.3f, attributes.cooldown_s);
            Juice.Play(JuiceSound.Boing);
        }

        void OnDestroy()
        {
            if (rider != null) rider.Disembark(this);
        }
    }
}
