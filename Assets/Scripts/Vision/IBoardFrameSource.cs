using UnityEngine;

namespace CalHacks
{
    /// <summary>Where a capture's whiteboard frame comes from: the live webcam, or saved board photos for replay trials.</summary>
    public interface IBoardFrameSource
    {
        string DeviceName { get; }
        string Status { get; }
        /// <summary>A frame is ready to grab right now.</summary>
        bool HasFreshFrame { get; }
        /// <summary>The current frame upright: board space, origin bottom-left. Null if there isn't one.</summary>
        Color32[] Grab(out int width, out int height);
    }
}
