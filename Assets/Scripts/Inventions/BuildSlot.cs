using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// A designer-placed spot at a free-build obstacle (the high ledge): while B is on the ground inside this trigger, a
    /// free build lands in the slot's box instead of beside B, and <see cref="BuildGhost"/> outlines that box. The box
    /// sits against the obstacle and is sized so any fitted drawing makes a step B can jump onto and then off. A new
    /// build here replaces the previous one.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class BuildSlot : MonoBehaviour
    {
        /// <summary>
        /// The slot box the level uses, in metres: wide enough that a 4:1 drawing still makes a 0.75 m step, and no taller
        /// than B can jump onto (0.7–1.6 m steps work for the 2.3 m ledge). The ghost texture is drawn at this aspect.
        /// </summary>
        public static readonly Vector2 DefaultBox = new Vector2(3f, 1.2f);

        static readonly List<BuildSlot> Slots = new List<BuildSlot>();

        [Tooltip("Where the slot box stands: the bottom-centre of its front-to-back middle.")]
        [SerializeField] Vector3 bottomCentre;
        [Tooltip("Width and height of the slot (the ghost). Drawings are fitted inside, keeping their shape.")]
        [SerializeField] Vector2 box = DefaultBox;
        [Tooltip("How deep the build's colliders are (Z). Match the obstacle it's against.")]
        [SerializeField] float depth = 2.5f;
        [Tooltip("Which side of the box a narrower drawing is pushed to: +1 right, -1 left, 0 centred. Face the obstacle.")]
        [SerializeField] float align = 1f;

        Collider area;

        public Vector3 SlotBottom => bottomCentre;
        public Vector2 Box => box;
        public float Depth => depth;

        /// <summary>The build standing in the slot now, if any (null once destroyed, e.g. by F5).</summary>
        public Invention Occupant { get; set; }

        void Awake() => area = GetComponent<Collider>();

        void OnEnable()
        {
            if (!Slots.Contains(this)) Slots.Add(this);
        }

        void OnDisable() => Slots.Remove(this);

        /// <summary>The slot whose area holds a player with feet at <paramref name="feet"/>, or null.</summary>
        public static BuildSlot At(Vector3 feet)
        {
            // Half a metre up, so standing on the floor counts but standing on the slot's own build (or anything taller)
            // doesn't: up there a build stacks beside B as usual.
            var point = feet + Vector3.up * 0.5f;
            foreach (var slot in Slots)
            {
                if (slot != null && slot.area != null && slot.area.bounds.Contains(point)) return slot;
            }
            return null;
        }

        /// <summary>Bottom-centre for a drawing fitted to <paramref name="size"/> inside this slot.</summary>
        public Vector3 BottomCentreFor(Vector2 size) => InventionPlacement.SlotBottomCentre(bottomCentre, box, size, align);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.62f, 0.15f, 0.8f);
            Gizmos.DrawWireCube(bottomCentre + Vector3.up * (box.y * 0.5f), new Vector3(box.x, box.y, depth));
        }
    }
}
