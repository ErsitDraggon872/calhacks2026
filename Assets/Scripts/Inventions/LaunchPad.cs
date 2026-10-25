using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// The launch deck, for the operator: F1 while B stands in this trigger builds a fallback rocket in front of B, the
    /// way F1 lays the bridge or feeds the gatekeeper. A drawn rocket needs nothing from it; it flies on its own thrust.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class LaunchPad : MonoBehaviour
    {
        /// <summary>The fallback rocket's drawing, under Resources (made by MockDrawingGenerator).</summary>
        public const string RocketDrawing = "MockDrawings/rocket";

        static readonly List<LaunchPad> Pads = new List<LaunchPad>();

        Collider area;

        void Awake() => area = GetComponent<Collider>();

        void OnEnable()
        {
            if (!Pads.Contains(this)) Pads.Add(this);
        }

        void OnDisable() => Pads.Remove(this);

        /// <summary>The pad whose area holds a player with feet at <paramref name="feet"/>, or null.</summary>
        public static LaunchPad At(Vector3 feet)
        {
            var point = feet + Vector3.up * 0.5f;
            foreach (var pad in Pads)
            {
                if (pad != null && pad.area != null && pad.area.bounds.Contains(point)) return pad;
            }
            return null;
        }

        /// <summary>F1's rocket: straight up, at the backend demo's middle power (14 m/s² for 1.6 s).</summary>
        public static InventionSpec FallbackRocket() => InventionSpec.Parse(new InterpretResponse
        {
            status = "ok",
            name = "rocket",
            message = "Press E to fire!",
            components = new[]
            {
                new InterpretComponent { type = "support", activation = "always", anchor_x = .5f, anchor_y = .5f },
                new InterpretComponent { type = "thrust", activation = "button", anchor_x = .5f, anchor_y = .1f,
                    acceleration = 14f, duration_s = 1.6f, angle_deg = 90f },
            },
        });
    }
}
