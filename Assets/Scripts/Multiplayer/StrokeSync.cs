using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Creator side of the digital whiteboard: the canvas reports pen strokes here and they go out in small batches, so
    /// the Explorer sees the drawing while it's being made. Coordinates are normalized 0-1 over the canvas, origin
    /// bottom-left (board space, like the webcam path).
    ///
    /// A canvas only needs three calls: <see cref="BeginStroke"/> on pen down, <see cref="AddPoint"/> while it moves,
    /// <see cref="EndStroke"/> on pen up. CLEAR is <see cref="CreatorBuild.ClearDrawing"/>.
    /// </summary>
    public class StrokeSync : MonoBehaviour
    {
        public static StrokeSync Instance { get; private set; }

        [Tooltip("Send a batch at least this often while drawing (seconds).")]
        [SerializeField] float flushInterval = 0.06f;
        [Tooltip("Send a batch once it has this many points.")]
        [SerializeField] int maxBatchPoints = 48;
        [Tooltip("Ignore moves shorter than this (normalized), so a still pen sends nothing.")]
        [SerializeField] float minStep = 0.003f;

        readonly List<float> batch = new List<float>();
        Vector2 last;
        bool drawing;
        float nextFlush;
        uint sequence;
        string sequenceFor;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void BeginStroke(Vector2 normalized)
        {
            if (drawing) EndStroke();
            drawing = true;
            batch.Clear();
            Append(Clamp(normalized));
            nextFlush = Time.unscaledTime + flushInterval;
        }

        public void AddPoint(Vector2 normalized)
        {
            if (!drawing) return;
            var p = Clamp(normalized);
            if ((p - last).sqrMagnitude < minStep * minStep) return;
            Append(p);
            if (batch.Count / 2 >= maxBatchPoints || Time.unscaledTime >= nextFlush) Flush(true);
        }

        public void EndStroke()
        {
            if (!drawing) return;
            drawing = false;
            // A tap is a dot: a stroke needs two points to be drawn.
            if (batch.Count == 2) Append(last + new Vector2(minStep, 0f));
            Flush(false);
        }

        void Update()
        {
            if (drawing && batch.Count >= 4 && Time.unscaledTime >= nextFlush) Flush(true);
        }

        void Append(Vector2 p)
        {
            batch.Add(p.x);
            batch.Add(p.y);
            last = p;
        }

        /// <summary>Sends the batch. Mid-stroke, the last point starts the next batch so the line stays joined.</summary>
        void Flush(bool continuing)
        {
            nextFlush = Time.unscaledTime + flushInterval;
            if (batch.Count < 4) return;
            var session = ThinkInkSession.Instance;
            var build = CreatorBuild.Instance;
            if (session != null && build != null && session.Role == CoopRole.Creator)
            {
                var captureId = build.CaptureId;
                if (sequenceFor != captureId)
                {
                    sequenceFor = captureId;
                    sequence = 0;
                }
                session.AddStroke(captureId, sequence++, new List<float>(batch));
            }
            batch.Clear();
            if (continuing) Append(last);
        }

        static Vector2 Clamp(Vector2 p) => new Vector2(Mathf.Clamp01(p.x), Mathf.Clamp01(p.y));
    }
}
