using System;
using UnityEngine;

namespace CalHacks
{
    [Serializable]
    public class MockDrawing
    {
        [Tooltip("PNG name under Assets/Resources/MockDrawings/ (no extension).")]
        public string resourceName;
        [Tooltip("Label the mock 'provider' returns for this drawing.")]
        public string label;
    }

    [CreateAssetMenu(menuName = "Game/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("Vision")]
        [Tooltip("On: MockVisionClient (no Python service needed). Off: HttpVisionClient against serviceUrl.\n" +
                 "A build can override this with -vision=mock or -vision=http on the command line.")]
        public bool useMockVision = true;
        public string serviceUrl = "http://localhost:8000";
        [Tooltip("Seconds before POST /capture gives up.")]
        public int captureTimeout = 10;
        [Tooltip("Seconds before GET /label/{id} (held open by the service) gives up.")]
        public int labelTimeout = 20;
        [Tooltip("Seconds between GET /health polls while the debug overlay is open.")]
        public float healthPollInterval = 3f;

        [Header("Gameplay")]
        [Tooltip("CAST binds to the nearest unsolved socket within this horizontal distance of the explorer.")]
        public float castRange = 5f;
        [Tooltip("Falling below this height respawns the explorer at the last checkpoint.")]
        public float killY = -5f;

        [Header("Mock vision")]
        public float mockCaptureDelay = 0.1f;
        public float mockLabelDelay = 1f;
        [Tooltip("Cycled in order on each CAST. Keys 1-5 pick the next one; 0 makes the next CAST an empty board.")]
        public MockDrawing[] mockDrawings =
        {
            new MockDrawing { resourceName = "bridge", label = "bridge" },
            new MockDrawing { resourceName = "sun", label = "light" },
            new MockDrawing { resourceName = "fish", label = "fish" },
            new MockDrawing { resourceName = "cat", label = "cat" },
            new MockDrawing { resourceName = "scribble", label = "unknown" },
        };

        public bool ResolveUseMock()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.Equals("-vision=mock", StringComparison.OrdinalIgnoreCase)) return true;
                if (arg.Equals("-vision=http", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return useMockVision;
        }
    }
}
