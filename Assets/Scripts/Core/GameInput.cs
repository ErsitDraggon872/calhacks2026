using UnityEngine.InputSystem;

namespace CalHacks
{
    /// <summary>
    /// Actions from the project-wide Input Actions asset (Assets/InputSystem_Actions.inputactions, map "Player").
    /// The button station is a USB keyboard, so these keys are the station's contract:
    /// Space CAST, Backspace NEW BOARD, F5 reset, F1 accept, F2 reject, F3 overlay, F9 recalibrate.
    /// </summary>
    public static class GameInput
    {
        static InputAction move, cast, newBoard, resetLevel, manualAccept, manualReject, debugOverlay, recalibrate, mockPick;

        public static InputAction Move => move ??= Find("Move");
        public static InputAction Cast => cast ??= Find("Cast");
        public static InputAction NewBoard => newBoard ??= Find("NewBoard");
        public static InputAction ResetLevel => resetLevel ??= Find("ResetLevel");
        public static InputAction ManualAccept => manualAccept ??= Find("ManualAccept");
        public static InputAction ManualReject => manualReject ??= Find("ManualReject");
        public static InputAction DebugOverlay => debugOverlay ??= Find("DebugOverlay");
        public static InputAction Recalibrate => recalibrate ??= Find("Recalibrate");
        public static InputAction MockPick => mockPick ??= Find("MockPick");

        public static void Enable() => InputSystem.actions?.Enable();

        static InputAction Find(string name) => InputSystem.actions.FindAction("Player/" + name, throwIfNotFound: true);
    }
}
