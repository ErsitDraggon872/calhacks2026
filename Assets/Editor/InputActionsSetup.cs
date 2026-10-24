using System.IO;
using UnityEditor;
using UnityEngine.InputSystem;

namespace CalHacks.EditorTools
{
    /// <summary>
    /// Adds the station and operator actions to the project-wide Input Actions asset. Idempotent.
    /// Keys are the contract with the button station firmware: change them only together.
    /// </summary>
    public static class InputActionsSetup
    {
        const string AssetPath = "Assets/InputSystem_Actions.inputactions";
        const string Scheme = "Keyboard&Mouse";

        [MenuItem("Game/Setup Input Actions")]
        public static void Setup()
        {
            var asset = InputActionAsset.FromJson(File.ReadAllText(AssetPath));
            var map = asset.FindActionMap("Player", throwIfNotFound: true);

            // No jumping in this game, and Space belongs to CAST.
            if (map.FindAction("Jump") != null) asset.RemoveAction("Player/Jump");

            Add(map, "Cast", "<Keyboard>/space");
            Add(map, "NewBoard", "<Keyboard>/backspace");
            Add(map, "ResetLevel", "<Keyboard>/f5");
            Add(map, "ManualAccept", "<Keyboard>/f1");
            Add(map, "ManualReject", "<Keyboard>/f2");
            Add(map, "DebugOverlay", "<Keyboard>/f3");
            Add(map, "Recalibrate", "<Keyboard>/f9");
            Add(map, "MockPick", "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5", "<Keyboard>/0");

            File.WriteAllText(AssetPath, asset.ToJson());
            AssetDatabase.ImportAsset(AssetPath);
            UnityEngine.Object.DestroyImmediate(asset);
        }

        static void Add(InputActionMap map, string name, params string[] paths)
        {
            if (map.FindAction(name) != null) return;
            var action = map.AddAction(name, InputActionType.Button);
            foreach (var path in paths) action.AddBinding(path, groups: Scheme);
        }
    }
}
