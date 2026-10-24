using System.Threading;
using System.Threading.Tasks;

namespace CalHacks
{
    /// <summary>
    /// The game's view of the vision service. Calls never throw for service errors (they return a failed
    /// VisionResult); they throw OperationCanceledException when the token is cancelled (reset or manual override).
    /// </summary>
    public interface IVisionClient
    {
        string Name { get; }
        Task<VisionResult<CaptureResponse>> CaptureAsync(CaptureRequest request, CancellationToken ct);
        Task<VisionResult<LabelResponse>> GetLabelAsync(string captureId, CancellationToken ct);
        Task<VisionResult<HealthResponse>> HealthAsync(CancellationToken ct);
        Task<VisionResult<CalibrateResponse>> CalibrateAsync(CancellationToken ct);
    }
}
