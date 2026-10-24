using System;
using UnityEngine;

namespace CalHacks
{
    [Serializable]
    public class InterpretRequest
    {
        public string board_png_base64;
        public string cutout_png_base64;
        public string capture_id;
        public int run_id;
        public string socket_id;
        public string goal;
        public string intent;
        public string intent_mode;
        public int board_width;
        public int board_height;
        public int cutout_x;
        public int cutout_y;
        public int cutout_width;
        public int cutout_height;
        public int ink_px;
    }

    [Serializable]
    public class PhysicsPart
    {
        public string type;
        public string activation;
        public float anchor_x;
        public float anchor_y;
        public float launch_speed;
        public int angle_deg;
        public float acceleration;
        public float duration_s;
    }

    /// <summary>Bounded shared gameplay traits. width_m remains on InterpretResponse.</summary>
    [Serializable]
    public class InventionAttributes
    {
        public float mass_kg = 3f;
        public float load_capacity_kg = 8f;
        public float friction = 0.6f;
        public float elasticity = 0.1f;
        public float drag = 0.2f;
        public float stability = 0.7f;
        public float durability = 0.7f;
        public float force = 0.6f;
        public float speed = 0.5f;
        public int direction_deg;
        public float duration_s = 3f;
        public float energy_capacity = 0.7f;
        public float carry_capacity_kg = 3f;
        public float range_m = 3f;
        public float cooldown_s = 0.5f;

        public void Normalize()
        {
            mass_kg = Mathf.Clamp(Finite(mass_kg, 3), 0.5f, 20f);
            load_capacity_kg = Mathf.Clamp(Finite(load_capacity_kg, 8), 0, 20);
            friction = Mathf.Clamp01(Finite(friction, .6f));
            elasticity = Mathf.Clamp01(Finite(elasticity, .1f));
            drag = Mathf.Clamp01(Finite(drag, .2f));
            stability = Mathf.Clamp01(Finite(stability, .7f));
            durability = Mathf.Clamp01(Finite(durability, .7f));
            force = Mathf.Clamp01(Finite(force, .6f));
            speed = Mathf.Clamp01(Finite(speed, .5f));
            direction_deg = Mathf.Clamp(direction_deg, -180, 180);
            duration_s = Mathf.Clamp(Finite(duration_s, 3), 0, 10);
            energy_capacity = Mathf.Clamp01(Finite(energy_capacity, .7f));
            carry_capacity_kg = Mathf.Clamp(Finite(carry_capacity_kg, 3), 0, 20);
            range_m = Mathf.Clamp(Finite(range_m, 3), .2f, 8f);
            cooldown_s = Mathf.Clamp(Finite(cooldown_s, .5f), 0, 5f);
        }

        static float Finite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    [Serializable]
    public class InterpretResponse
    {
        public string status;
        public string name;
        public string message;
        public float width_m;
        public PhysicsPart[] components;
        public InventionAttributes attributes;
        // Optional hints. Older Bitdeer responses without these fields remain valid.
        public string motion_behavior;
        public float motion_speed;
        public float motion_range;
        public string capture_id;
        public int run_id;
        public int ink_cost;
        public string provider;
        public float latency_ms;
    }

    /// <summary>Defense in depth: normalize even if the backend has its own validation.</summary>
    public static class InterpretContract
    {
        public static bool Validate(InterpretResponse response, out string error)
        {
            error = null;
            if (response == null) { error = "Empty AI response"; return false; }
            if (string.Equals(response.status, "rejected", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals((response.message ?? "").Trim(), "Try drawing a tool.", StringComparison.OrdinalIgnoreCase) &&
                response.components != null && response.components.Length > 0 &&
                response.components[0] != null &&
                string.Equals(response.components[0].type, "support", StringComparison.OrdinalIgnoreCase))
            {
                // Unsupported special power != forbidden cutout. Preserve the
                // original drawing as a physical shape, without fantasy powers.
                response.status = "ok";
                response.components = new[] { response.components[0] };
            }
            if (!string.Equals(response.status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                error = string.IsNullOrWhiteSpace(response.message) ? "AI could not interpret this drawing. Try again." : response.message;
                return false;
            }
            response.status = "ok";
            if (response.components == null || response.components.Length == 0)
            {
                error = "AI response has no physics components";
                return false;
            }
            response.width_m = Mathf.Clamp(Finite(response.width_m, 2.5f), 0.5f, 6f);
            // Old JSON without attributes remains accepted for current rocket/trampoline builds.
            if (response.attributes == null) response.attributes = new InventionAttributes();
            response.attributes.Normalize();
            var used = new System.Collections.Generic.HashSet<string>();
            if (response.components[0] == null ||
                !string.Equals(response.components[0].type, "support", StringComparison.OrdinalIgnoreCase))
            {
                error = "AI response must start with support";
                return false;
            }
            if (response.components.Length > 4) { error = "AI returned too many physics components"; return false; }
            for (int i = 0; i < response.components.Length; i++)
            {
                var part = response.components[i];
                if (part == null) { error = "Missing physics component"; return false; }
                part.type = (part.type ?? "").Trim().ToLowerInvariant();
                part.activation = (part.activation ?? "").Trim().ToLowerInvariant();
                if (part.activation != "always" && part.activation != "contact" &&
                    part.activation != "button" && part.activation != "hold" && part.activation != "toggle")
                    part.activation = part.type == "bounce" ? "contact" : "button";
                part.anchor_x = Mathf.Clamp01(Finite(part.anchor_x, 0.5f));
                part.anchor_y = Mathf.Clamp01(Finite(part.anchor_y, 0.5f));
                if (!used.Add(part.type)) { error = "Duplicate physics component"; return false; }
                switch (part.type)
                {
                    case "support":
                        if (i != 0) { error = "Duplicate support component"; return false; }
                        part.activation = "always";
                        part.anchor_x = 0.5f;
                        part.anchor_y = 0.5f;
                        part.launch_speed = 0;
                        part.angle_deg = 0;
                        part.acceleration = 0;
                        part.duration_s = 0;
                        break;
                    case "bounce":

                        if (part.activation != "button" && part.activation != "hold" && part.activation != "toggle") part.activation = "contact";
                        part.launch_speed = Mathf.Clamp(Finite(part.launch_speed, 10f), 3f, 14f);
                        part.angle_deg = Mathf.Clamp(part.angle_deg, -60, 60);
                        part.acceleration = 0;
                        part.duration_s = 0;
                        break;
                    case "thrust":

                        if (part.activation != "contact" && part.activation != "hold" && part.activation != "toggle") part.activation = "button";
                        part.launch_speed = 0;
                        part.angle_deg = Mathf.Clamp(part.angle_deg, -180, 180);
                        part.acceleration = Mathf.Clamp(Finite(part.acceleration, 12f), 2f, 25f);
                        part.duration_s = Mathf.Clamp(Finite(part.duration_s, 1.5f), 0.5f, 3f);
                        break;
                    case "lift":
                    case "roll":
                    case "push_pull":
                        part.launch_speed = 0;
                        part.angle_deg = Mathf.Clamp(part.angle_deg, -180, 180);
                        part.acceleration = Mathf.Clamp(Finite(part.acceleration, 12f), 2f, part.type == "roll" ? 14f : 25f);
                        part.duration_s = Mathf.Clamp(Finite(part.duration_s, 3f), 0f, 10f);
                        break;
                    case "glide":
                        part.launch_speed = Mathf.Clamp(Finite(part.launch_speed, 2.5f), 1f, 6f);
                        part.angle_deg = 0;
                        part.acceleration = part.duration_s = 0;
                        break;
                    case "attach":
                    case "tether":
                    case "hinge":
                        part.launch_speed = part.acceleration = part.duration_s = 0;
                        part.angle_deg = 0;
                        break;
                    default:
                        error = "Unknown physics component: " + part.type;
                        return false;
                }
            }
            return true;
        }

        static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
