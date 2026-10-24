using System;
using UnityEngine;

namespace CalHacks
{
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

}
