using UnityEditor;
using UnityEngine;

namespace CalHacks.EditorTools
{
    /// <summary>Switch capture mode without editing serialized asset YAML or rebuilding team prefabs.</summary>
    public static class DigitalDrawingSetup
    {
        [MenuItem("Game/Use Digital Drawing (No Webcam)")]
        public static void Enable()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameAssetBuilder.ConfigPath);
            if (config == null)
            {
                Debug.LogError("[Digital] GameConfig not found. Run Game/Build Assets first.");
                return;
            }
            Undo.RecordObject(config, "Enable digital drawing mode");
            config.captureSource = CaptureSource.Digital;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log("[Digital] Enabled: press Play, draw on the pad with the mouse, press BUILD; P reopens it.");
        }

        [MenuItem("Game/Use Webcam Drawing")]
        public static void Disable()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameAssetBuilder.ConfigPath);
            if (config == null) return;
            Undo.RecordObject(config, "Enable webcam drawing mode");
            config.captureSource = CaptureSource.Webcam;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log("[Digital] Restored webcam mode.");
        }
    }
}
