namespace CalHacks
{
    public enum CastBlock
    {
        None,
        InFlight,
        BoardLocked,
        Finished,
    }

    /// <summary>Pure run bookkeeping: run_id, timer, CAST lock. No Unity dependencies so it can be unit tested.</summary>
    public class RunState
    {
        public int RunId { get; private set; } = 1;
        public float Elapsed { get; private set; }
        public bool Finished { get; private set; }
        public bool BoardLocked { get; private set; }
        public bool InFlight { get; private set; }

        public CastBlock CanCast()
        {
            if (Finished) return CastBlock.Finished;
            if (InFlight) return CastBlock.InFlight;
            if (BoardLocked) return CastBlock.BoardLocked;
            return CastBlock.None;
        }

        public void BeginRequest() => InFlight = true;
        public void EndRequest() => InFlight = false;

        /// <summary>A drawing was consumed from the board; CAST stays locked until NEW BOARD.</summary>
        public void LockBoard() => BoardLocked = true;
        public void NewBoard() => BoardLocked = false;

        public void Tick(float dt)
        {
            if (!Finished) Elapsed += dt;
        }

        public void Finish() => Finished = true;

        /// <summary>Starts a new run. Any reply carrying an older run_id is stale from now on.</summary>
        public int Reset()
        {
            RunId++;
            Elapsed = 0f;
            Finished = false;
            BoardLocked = false;
            InFlight = false;
            return RunId;
        }

        public bool IsCurrent(int runId) => runId == RunId;

        /// <summary>A reply is applied only if the request was made in this run and the service echoed the same run_id.</summary>
        public bool ShouldApply(int requestRunId, int responseRunId) =>
            IsCurrent(requestRunId) && responseRunId == requestRunId;
    }
}
