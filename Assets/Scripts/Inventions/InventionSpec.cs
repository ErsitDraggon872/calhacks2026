using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CalHacks
{
    // The attribute vocabulary of docs/interpret-contract.md. Names match the contract's strings with the underscores
    // dropped ("lighter_than_air" is LighterThanAir).
    public enum MaterialKind { Wood, Stone, Metal, Rubber, Cloth, Paper, Glass, Ice, Jelly, Plant, Flesh, Cloud }
    public enum WeightClass { Feather, Light, Medium, Heavy }
    public enum BodyKind { Static, Dynamic }
    public enum PartType { Support, Bounce, Thrust, Emit, Explode }
    public enum Activation { Always, Contact, Button }
    public enum EmitKind { None, Light, Fire, Water, Wind }

    public enum Trait
    {
        Glows, Hot, Cold, Wet, Flammable, Fireproof, Waterproof,
        Floats, LighterThanAir, Bouncy, Slippery, Sticky, Soft, Fragile, Sharp, Conductive, Magnetic,
        Alive, Edible, Hostile, Friendly, Noisy, Smelly,
        Climbable, Rideable, Holdable, Container,
    }

    /// <summary>One component of a POST /interpret reply, exactly as the JSON has it (flat: unused fields are 0 / "").</summary>
    [Serializable]
    public class InterpretComponent
    {
        public string type;
        public string activation;
        public float anchor_x;
        public float anchor_y;
        public float launch_speed;
        public float angle_deg;
        public float acceleration;
        public float duration_s;
        public string emit_kind;
        public float range_m;
    }

    /// <summary>A POST /interpret reply, exactly as the JSON has it. Untrusted: <see cref="InventionSpec.Parse"/> cleans it.</summary>
    [Serializable]
    public class LegacyInterpretResponse
    {
        public string status; // "ok", "rejected" or "error"
        public string capture_id;
        public int run_id;
        public string name;
        public string message;
        public string[] adjectives;
        public string material;
        public string weight;
        public string body;
        public string[] traits;
        public float width_m;
        public int ink_cost;
        public InterpretComponent[] components;
        public string provider;
        public float latency_ms;
    }

    /// <summary>A component after clamping. Fields that don't apply to its <see cref="Type"/> are 0.</summary>
    public class InventionPart
    {
        public PartType Type;
        public Activation Activation;
        public Vector2 Anchor;
        public float LaunchSpeed;
        public float AngleDeg;
        public float Acceleration;
        public float DurationS;
        public EmitKind EmitKind;
        public float RangeM;
    }

    /// <summary>
    /// What a drawing is, as a combination of attributes (like Scribblenauts): one material, weight and body, any number
    /// of traits, and a few active parts. Puzzles and the world check the attributes, never the name. Always clamped and
    /// valid; build one with <see cref="Parse"/> (a backend reply) or <see cref="FromLabel"/> (the old label services).
    /// </summary>
    public class InventionSpec
    {
        public const int MaxTraits = 8;
        public const float MinWidth = 0.5f, MaxWidth = 6f;
        public const int MinInk = 5, MaxInk = 100;

        static readonly Dictionary<MaterialKind, Trait[]> Implied = new Dictionary<MaterialKind, Trait[]>
        {
            { MaterialKind.Wood, new[] { Trait.Flammable, Trait.Floats } },
            { MaterialKind.Paper, new[] { Trait.Flammable, Trait.Floats } },
            { MaterialKind.Metal, new[] { Trait.Conductive, Trait.Magnetic } },
            { MaterialKind.Rubber, new[] { Trait.Bouncy } },
            { MaterialKind.Ice, new[] { Trait.Cold, Trait.Slippery, Trait.Fragile } },
            { MaterialKind.Glass, new[] { Trait.Fragile } },
            { MaterialKind.Jelly, new[] { Trait.Bouncy, Trait.Sticky } },
            { MaterialKind.Cloud, new[] { Trait.LighterThanAir, Trait.Soft } },
            { MaterialKind.Cloth, new[] { Trait.Flammable, Trait.Soft } },
            { MaterialKind.Plant, new[] { Trait.Flammable, Trait.Edible } },
            { MaterialKind.Flesh, new[] { Trait.Alive } },
        };

        // Stand-ins for the old label-only services (Mock, the Python service) until the backend returns attributes.
        static readonly Dictionary<string, (MaterialKind material, Trait[] traits)> LabelTable =
            new Dictionary<string, (MaterialKind, Trait[])>
            {
                { "bridge", (MaterialKind.Wood, new Trait[0]) },
                { "light", (MaterialKind.Glass, new[] { Trait.Glows }) },
                { "sun", (MaterialKind.Cloud, new[] { Trait.Glows, Trait.Hot }) },
                { "torch", (MaterialKind.Wood, new[] { Trait.Glows, Trait.Hot }) },
                { "fish", (MaterialKind.Flesh, new[] { Trait.Edible, Trait.Wet }) },
                { "cat", (MaterialKind.Flesh, new[] { Trait.Friendly, Trait.Noisy }) },
                { "mushroom", (MaterialKind.Plant, new[] { Trait.Bouncy }) },
            };

        readonly HashSet<Trait> traits = new HashSet<Trait>();

        public string Name { get; private set; } = SocketLogic.Unknown;
        /// <summary>Player-facing line from the backend, or "".</summary>
        public string Message { get; private set; } = string.Empty;
        public IReadOnlyList<string> Adjectives { get; private set; } = Array.Empty<string>();
        /// <summary>The backend said this can't be built (its <see cref="Message"/> says why).</summary>
        public bool Rejected { get; private set; }
        public MaterialKind Material { get; private set; } = MaterialKind.Wood;
        public WeightClass Weight { get; private set; } = WeightClass.Medium;
        public BodyKind Body { get; private set; } = BodyKind.Static;
        /// <summary>Declared traits plus the ones the material implies.</summary>
        public IReadOnlyCollection<Trait> Traits => traits;
        public IReadOnlyList<InventionPart> Parts { get; private set; } = Array.Empty<InventionPart>();
        /// <summary>World width in metres, or 0 when the backend didn't say (the build slot decides).</summary>
        public float WidthM { get; private set; }
        public int InkCost { get; private set; }
        /// <summary>Strings from the reply that weren't in the vocabulary, for logging.</summary>
        public IReadOnlyList<string> Dropped { get; private set; } = Array.Empty<string>();

        public bool Has(Trait trait) => traits.Contains(trait);
        public bool HasPart(PartType type) => Parts.Any(p => p.Type == type);

        /// <summary>A plain Support of the given name: what anything unreadable becomes.</summary>
        public static InventionSpec Plain(string name = SocketLogic.Unknown) => FromLabel(name);

        /// <summary>Maps an old-style label to attributes. Unknown labels are a plain wooden Support with that name.</summary>
        public static InventionSpec FromLabel(string label)
        {
            var spec = new InventionSpec { Name = SocketLogic.Normalize(label) };
            var declared = new List<Trait>();
            if (LabelTable.TryGetValue(spec.Name, out var entry))
            {
                spec.Material = entry.material;
                declared.AddRange(entry.traits);
            }
            spec.Finish(declared, new List<InventionPart>(), new List<string>());
            return spec;
        }

        /// <summary>
        /// Cleans a backend reply: case-insensitive vocabulary with unknown strings dropped, the contract's clamps applied
        /// again, exclusions resolved, at most one part per type, and at least one part (a Support). A missing reply or
        /// an "error" status is a plain Support, never a failure.
        /// </summary>
        public static InventionSpec Parse(LegacyInterpretResponse r)
        {
            if (!IsPresent(r)) return Plain();
            var status = Lower(r.status);
            if (status == "error") return Plain(r.name);

            var spec = new InventionSpec
            {
                Name = SocketLogic.Normalize(r.name),
                Message = r.message ?? string.Empty,
                Adjectives = (r.adjectives ?? Array.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a.Trim().ToLowerInvariant()).ToArray(),
                Rejected = status == "rejected",
                WidthM = r.width_m > 0f ? Mathf.Clamp(r.width_m, MinWidth, MaxWidth) : 0f,
                InkCost = Mathf.Clamp(r.ink_cost, MinInk, MaxInk),
            };
            var dropped = new List<string>();
            spec.Material = ParseOr(r.material, MaterialKind.Wood, dropped);
            spec.Weight = ParseOr(r.weight, WeightClass.Medium, dropped);
            spec.Body = ParseOr(r.body, BodyKind.Static, dropped);

            var declared = new List<Trait>();
            foreach (var t in r.traits ?? Array.Empty<string>())
            {
                if (TryParse(t, out Trait trait)) { if (!declared.Contains(trait)) declared.Add(trait); }
                else if (!string.IsNullOrWhiteSpace(t)) dropped.Add(t);
            }
            if (declared.Count > MaxTraits) declared.RemoveRange(MaxTraits, declared.Count - MaxTraits);

            var parts = new List<InventionPart>();
            foreach (var c in r.components ?? Array.Empty<InterpretComponent>())
            {
                if (c == null) continue;
                if (!TryParse(c.type, out PartType type)) { dropped.Add(c.type); continue; }
                if (parts.Any(p => p.Type == type)) continue;
                var part = ClampPart(type, c, dropped);
                if (part != null) parts.Add(part);
            }

            spec.Finish(declared, parts, dropped);
            return spec;
        }

        /// <summary>A reply that was actually sent: <c>JsonUtility</c> leaves an absent nested object empty, not null.</summary>
        public static bool IsPresent(LegacyInterpretResponse r) => r != null && !string.IsNullOrWhiteSpace(r.status);

        static InventionPart ClampPart(PartType type, InterpretComponent c, List<string> dropped)
        {
            var part = new InventionPart
            {
                Type = type,
                Anchor = new Vector2(Mathf.Clamp01(c.anchor_x), Mathf.Clamp01(c.anchor_y)),
            };
            switch (type)
            {
                case PartType.Support:
                    part.Activation = Activation.Always;
                    break;
                case PartType.Bounce:
                    part.Activation = ActivationOr(c.activation, Activation.Contact, dropped, Activation.Contact, Activation.Button);
                    part.LaunchSpeed = Mathf.Clamp(c.launch_speed, 3f, 14f);
                    part.AngleDeg = Mathf.Clamp(c.angle_deg, -60f, 60f);
                    break;
                case PartType.Thrust:
                    part.Activation = ActivationOr(c.activation, Activation.Button, dropped, Activation.Button, Activation.Contact);
                    part.Acceleration = Mathf.Clamp(c.acceleration, 2f, 25f);
                    part.DurationS = Mathf.Clamp(c.duration_s, 0.5f, 3f);
                    part.AngleDeg = Mathf.DeltaAngle(0f, c.angle_deg);
                    break;
                case PartType.Emit:
                    if (!TryParse(c.emit_kind, out EmitKind kind) || kind == EmitKind.None)
                    {
                        dropped.Add("emit:" + c.emit_kind);
                        return null;
                    }
                    part.Activation = ActivationOr(c.activation, Activation.Always, dropped,
                        Activation.Always, Activation.Contact, Activation.Button);
                    part.EmitKind = kind;
                    part.RangeM = Mathf.Clamp(c.range_m, 0.5f, 8f);
                    part.AngleDeg = Mathf.DeltaAngle(0f, c.angle_deg);
                    break;
                case PartType.Explode:
                    part.Activation = ActivationOr(c.activation, Activation.Button, dropped, Activation.Contact, Activation.Button);
                    part.RangeM = Mathf.Clamp(c.range_m, 0.5f, 8f);
                    part.LaunchSpeed = Mathf.Clamp(c.launch_speed, 3f, 14f);
                    break;
            }
            return part;
        }

        // Merges the material's traits, lets emitting parts imply theirs, resolves exclusions, and guarantees a part.
        void Finish(List<Trait> declared, List<InventionPart> parts, List<string> dropped)
        {
            traits.UnionWith(declared);
            if (Implied.TryGetValue(Material, out var implied)) traits.UnionWith(implied);
            foreach (var part in parts.Where(p => p.Type == PartType.Emit))
            {
                if (part.EmitKind == EmitKind.Light) traits.Add(Trait.Glows);
                if (part.EmitKind == EmitKind.Fire) traits.UnionWith(new[] { Trait.Hot, Trait.Glows });
                if (part.EmitKind == EmitKind.Water) traits.Add(Trait.Wet);
            }
            if (traits.Contains(Trait.Hot)) traits.Remove(Trait.Cold);
            if (Weight == WeightClass.Heavy)
            {
                traits.Remove(Trait.Floats);
                traits.Remove(Trait.LighterThanAir);
            }

            if (parts.Count == 0) parts.Add(new InventionPart { Type = PartType.Support, Anchor = new Vector2(0.5f, 0.5f) });
            Parts = parts;
            Dropped = dropped;
        }

        /// <summary>One line for logs and the debug overlay: "torch: wood, medium; flammable, floats, glows; support".</summary>
        public string Describe()
        {
            var traitList = traits.Count > 0 ? string.Join(", ", traits.OrderBy(t => t).Select(t => Snake(t))) : "-";
            var partList = string.Join(", ", Parts.Select(p => Snake(p.Type)));
            return $"{Name}{(Rejected ? " (rejected)" : "")}: {Snake(Material)}, {Snake(Weight)}; {traitList}; {partList}";
        }

        /// <summary>The contract's spelling of a vocabulary value: LighterThanAir is "lighter_than_air".</summary>
        public static string Snake<T>(T value) where T : Enum
        {
            var s = value.ToString();
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (var i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(s[i]));
            }
            return sb.ToString();
        }

        static string Lower(string s) => (s ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>Case-insensitive, ignoring '_', '-' and spaces. Numbers are never accepted as names.</summary>
        public static bool TryParse<T>(string s, out T value) where T : struct, Enum
        {
            value = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var key = s.Trim().Replace("_", "").Replace("-", "").Replace(" ", "");
            if (key.Length == 0 || !char.IsLetter(key[0])) return false;
            return Enum.TryParse(key, true, out value) && Enum.IsDefined(typeof(T), value);
        }

        static T ParseOr<T>(string s, T fallback, List<string> dropped) where T : struct, Enum
        {
            if (TryParse(s, out T value)) return value;
            if (!string.IsNullOrWhiteSpace(s)) dropped.Add(s);
            return fallback;
        }

        static Activation ActivationOr(string s, Activation fallback, List<string> dropped, params Activation[] allowed)
        {
            var a = ParseOr(s, fallback, dropped);
            return allowed.Contains(a) ? a : fallback;
        }
    }

    /// <summary>How an invention feels to walk on, from its traits. B uses a CharacterController, so this is applied
    /// by <see cref="PlayerController"/>, not by a PhysicMaterial.</summary>
    public readonly struct InventionSurface
    {
        /// <summary>Multiplies B's walk speed (sticky is slow).</summary>
        public readonly float SpeedScale;
        /// <summary>How fast (m/s²) B's walking speed can change; infinite = instant, low = slides like ice.</summary>
        public readonly float Grip;
        /// <summary>Upward speed (m/s) B is thrown up with on landing; 0 = no bounce.</summary>
        public readonly float Bounce;

        public InventionSurface(float speedScale, float grip, float bounce)
        {
            SpeedScale = speedScale;
            Grip = grip;
            Bounce = bounce;
        }

        public static readonly InventionSurface Normal = new InventionSurface(1f, float.PositiveInfinity, 0f);

        public static InventionSurface For(InventionSpec spec)
        {
            if (spec == null) return Normal;
            var speed = spec.Has(Trait.Sticky) ? 0.5f : 1f;
            var grip = spec.Has(Trait.Slippery) ? 3f : float.PositiveInfinity;
            // About 1.7 m up with B's gravity: higher than a jump, so a bouncy build is a trampoline.
            var bounce = spec.Has(Trait.Bouncy) ? 10f : 0f;
            return new InventionSurface(speed, grip, bounce);
        }
    }

    public enum Verdict { Ok, Downgraded, Rejected }

    public readonly struct Feasibility
    {
        public readonly Verdict Verdict;
        /// <summary>Player-facing line, or "" when there's nothing to say.</summary>
        public readonly string Message;
        /// <summary>Parts the spec asks for that Unity can't run yet; the rest is still built.</summary>
        public readonly IReadOnlyList<PartType> Unsupported;

        public Feasibility(Verdict verdict, string message, IReadOnlyList<PartType> unsupported)
        {
            Verdict = verdict;
            Message = message;
            Unsupported = unsupported;
        }

        public bool Builds => Verdict != Verdict.Rejected;
    }

    /// <summary>
    /// Unity's last word before a free build: the backend judges the object, but it never sees the level. Pure, so it
    /// can be tested; Ink affordability joins it with the Ink bar.
    /// </summary>
    public static class InventionFeasibility
    {
        public const string NoRoomLine = "No room to build here!";

        /// <summary>True for the parts Unity can run today: Support, and Emit light (its light comes from Glows).</summary>
        public static bool Supported(InventionPart part) =>
            part.Type == PartType.Support || (part.Type == PartType.Emit && part.EmitKind == EmitKind.Light);

        public static Feasibility Check(InventionSpec spec, bool spotClear)
        {
            if (spec.Rejected)
            {
                var why = string.IsNullOrWhiteSpace(spec.Message) ? "That can't be built. Try something else!" : spec.Message;
                return new Feasibility(Verdict.Rejected, why, Array.Empty<PartType>());
            }
            if (!spotClear) return new Feasibility(Verdict.Rejected, NoRoomLine, Array.Empty<PartType>());

            var unsupported = spec.Parts.Where(p => !Supported(p)).Select(p => p.Type).Distinct().ToArray();
            if (unsupported.Length == 0) return new Feasibility(Verdict.Ok, string.Empty, unsupported);
            var names = string.Join(" and ", unsupported.Select(t => InventionSpec.Snake(t)));
            return new Feasibility(Verdict.Downgraded,
                $"Looks like a {spec.Name}! Its {names} doesn't work yet, so it's just solid.", unsupported);
        }
    }
}
