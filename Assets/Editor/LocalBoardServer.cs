using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CalHacks.EditorTools
{
    /// <summary>Starts the loopback drawing service when this Editor enters Play; never launches Unity.</summary>
    [InitializeOnLoad]
    public static class LocalBoardServer
    {
        static LocalBoardServer() => EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            var config = Resources.Load<GameConfig>("GameConfig");
            var session = Object.FindFirstObjectByType<ThinkInkSession>();
            if (config != null && config.ResolveCaptureSource() == CaptureSource.DigitalBoard &&
                session != null && session.IsLocal) StartServer();
        };

        [MenuItem("Game/Start Local Drawing Server")]
        public static void StartServer()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var script = Path.Combine(root, "tools", "start-local-board.ps1");
            // The script logs to a file: entering Play reloads the domain, which would kill any reader of its output.
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            UnityEngine.Debug.Log("[LocalBoard] Starting the local drawing server; progress in .utmp/spacetime/setup.log");
        }
    }
}
