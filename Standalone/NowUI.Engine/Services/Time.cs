// Mirrors UnityEngine.Time.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 member list, §6.1 time and frame contract).
// Behaviour spec: Docs/Standalone/UnityDependencyInventory.md (§A.3 "UnityEngine.Time", §H.9).
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// The frame clock. Every field except <see cref="realtimeSinceStartup"/> and
    /// <see cref="realtimeSinceStartupAsDouble"/> is latched once per frame by <c>NowRuntime.BeginFrame()</c>, which is
    /// the <b>only</b> writer (design §6.1): the core detects nested and leaked scopes by comparing
    /// <see cref="frameCount"/> against a stored value, so a second incrementer anywhere would break that detection.
    /// </summary>
    public static class Time
    {
        /// <summary>
        /// Unity's fixed-timestep default. The standalone build has no fixed-step loop at all, so this is a constant
        /// rather than a setting - it exists because core code reads it as a "typical frame length" fallback.
        /// </summary>
        private const float k_FixedDeltaTime = 0.02f;

        private static float s_TimeScale = 1f;
        private static float s_FixedDeltaTime = k_FixedDeltaTime;

        /// <summary>
        /// Frames begun so far; 0 before the first <c>NowRuntime.BeginFrame()</c>. Advances exactly once per host
        /// frame (design §6.1, hazard H.9).
        /// </summary>
        public static int frameCount => NowRuntime.frameCount;

        /// <summary>
        /// Seconds since the first frame began, read from the host clock <b>at call time</b> rather than latched per
        /// frame (design §6.1). This is deliberate and load-bearing: six suite tests sleep inside a single frame and
        /// compare successive reads, and a frame-latched value would report no elapsed time and fail them.
        /// </summary>
        public static float realtimeSinceStartup => (float)realtimeSinceStartupAsDouble;

        /// <summary>Double-precision <see cref="realtimeSinceStartup"/>; also read live. Backs <c>NowTime</c>.</summary>
        public static double realtimeSinceStartupAsDouble
        {
            get
            {
                double now = NowRuntime.host.clock.realtimeSeconds;
                double elapsed = now - NowRuntime.startSeconds;

                // Before the first BeginFrame there is no start reading yet, so startSeconds is 0 and the raw clock
                // value comes through - which is what an edit-mode-style host (no frame loop at all) wants: a clock
                // that still advances. Clamped at zero so a host clock that steps backwards cannot report negative
                // elapsed time.
                return elapsed > 0d ? elapsed : 0d;
            }
        }

        /// <summary>
        /// Seconds since the first frame began, latched at the start of the current frame. M1 has no pause or
        /// time-scale semantics, so this is the unscaled frame time (design §6.1).
        /// </summary>
        public static float time => (float)NowRuntime.timeSeconds;

        /// <summary>Double-precision <see cref="time"/>.</summary>
        public static double timeAsDouble => NowRuntime.timeSeconds;

        /// <summary>
        /// Same as <see cref="time"/>: without time-scale semantics the scaled and unscaled timelines coincide.
        /// </summary>
        public static float unscaledTime => time;

        /// <summary>
        /// Seconds between the last two frame starts; 0 on the first frame and never negative (design §6.1). This is
        /// also the value pushed into <c>NowUI.Engine.EngineClock.deltaTime</c> by <c>BeginFrame</c>, which is what the
        /// <c>SmoothDamp</c> overloads that omit a delta read.
        /// </summary>
        public static float deltaTime => NowRuntime.deltaTime;

        /// <summary>Same as <see cref="deltaTime"/>: no time scale in M1.</summary>
        public static float unscaledDeltaTime => NowRuntime.deltaTime;

        /// <summary>
        /// Same as <see cref="deltaTime"/>. Unity applies a low-pass filter here; M1 does not smooth, because nothing
        /// in the core reads it and an invented filter constant would be a fabricated behaviour rather than a shim.
        /// </summary>
        public static float smoothDeltaTime => NowRuntime.deltaTime;

        /// <summary>
        /// The fixed-timestep length. Settable as in Unity, but the standalone build runs no fixed-step loop, so
        /// setting it changes only what this property reports.
        /// </summary>
        public static float fixedDeltaTime
        {
            get => s_FixedDeltaTime;
            set => s_FixedDeltaTime = value;
        }

        /// <summary>
        /// Unity's global time multiplier, 1 by default. Kept settable so code written against Unity compiles, but M1
        /// has no scaled timeline (design §6.1): <see cref="time"/> and <see cref="deltaTime"/> ignore it.
        /// </summary>
        public static float timeScale
        {
            get => s_TimeScale;
            set => s_TimeScale = value;
        }
    }
}
