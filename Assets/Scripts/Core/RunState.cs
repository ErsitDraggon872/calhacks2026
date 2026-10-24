namespace CalHacks
{
    public enum CastBlock
    {
        None,
        InFlight,
        Cooldown,
        Finished,
    }

    /// <summary>Pure run bookkeeping: run_id, timer, BUILD cooldown. No Unity dependencies so it can be unit tested.</summary>
    public class RunState
    {
        public int RunId { get; private set; } = 1;
        public float Elapsed { get; private set; }
        public bool Finished { get; private set; }
        public bool InFlight { get; private set; }
        public float CooldownRemaining { get; private set; }
        public float CooldownDuration { get; private set; }

        /// <summary>0 right after a build, 1 when BUILD is ready.</summary>
        public float CooldownFraction => CooldownDuration > 0f ? 1f - CooldownRemaining / CooldownDuration : 1f;

        public CastBlock CanCast()
        {
            if (Finished) return CastBlock.Finished;
            if (InFlight) return CastBlock.InFlight;
            if (CooldownRemaining > 0f) return CastBlock.Cooldown;
            return CastBlock.None;
        }

        public void BeginRequest() => InFlight = true;
        public void EndRequest() => InFlight = false;

        /// <summary>A drawing was built; BUILD is blocked for <paramref name="seconds"/>.</summary>
        public void StartCooldown(float seconds)
        {
            CooldownDuration = seconds;
            CooldownRemaining = seconds;
        }

        public void Tick(float dt)
        {
            if (!Finished) Elapsed += dt;
            CooldownRemaining = System.Math.Max(0f, CooldownRemaining - dt);
        }

        public void Finish() => Finished = true;

        /// <summary>Starts a new run. Any reply carrying an older run_id is stale from now on.</summary>
        public int Reset()
        {
            RunId++;
            Elapsed = 0f;
            Finished = false;
            CooldownRemaining = 0f;
            InFlight = false;
            return RunId;
        }

        public bool IsCurrent(int runId) => runId == RunId;

        /// <summary>A reply is applied only if the request was made in this run and the service echoed the same run_id.</summary>
        public bool ShouldApply(int requestRunId, int responseRunId) =>
            IsCurrent(requestRunId) && responseRunId == requestRunId;
    }
}
