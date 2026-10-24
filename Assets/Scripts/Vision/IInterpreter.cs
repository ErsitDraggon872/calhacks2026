using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// One BUILD as captured locally. Both images are kept: the cutout is what the player sees, the untouched board
    /// crop is what the interpreter should recognise from, so an ink-extraction mistake can't spoil recognition.
    /// Pixel data is in board space: the upright frame cropped to GameConfig.boardCrop, origin bottom-left.
    /// </summary>
    public class BoardCapture
    {
        public CaptureRequest Request;
        public CaptureResponse Cutout;
        public Color32[] Board;
        public int BoardWidth;
        public int BoardHeight;
        /// <summary>Where the cutout sits inside the board crop.</summary>
        public RectInt CutoutRect;
    }

    /// <summary>Turns a captured drawing into a result. Replaceable independently of where captures come from.</summary>
    public interface IInterpreter
    {
        string Name { get; }
        Task<VisionResult<LabelResponse>> InterpretAsync(BoardCapture capture, CancellationToken ct);
    }

    /// <summary>No backend yet: every drawing is "unknown".</summary>
    public class NoInterpreter : IInterpreter
    {
        public string Name => "none";

        public Task<VisionResult<LabelResponse>> InterpretAsync(BoardCapture capture, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(VisionResult<LabelResponse>.Success(new LabelResponse
            {
                status = "done",
                socket_id = capture.Request.socket_id,
                run_id = capture.Request.run_id,
                label = SocketLogic.Unknown,
                provider = Name,
            }));
        }
    }
}
