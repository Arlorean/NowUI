// Frame-clock hook for the NowUI standalone build; the UnityEngine.Time shim forwards to it. Spec: Docs/Standalone/UnityValueTypeSemantics.md (§0, Time.deltaTime).
namespace NowUI.Engine
{
    /// <summary>
    /// Minimal per-frame clock the shim's <c>Mathf.SmoothDamp</c> / <c>Vector2.SmoothDamp</c> / <c>Vector3.SmoothDamp</c>
    /// overloads (the ones that omit <c>deltaTime</c>) read instead of <c>UnityEngine.Time.deltaTime</c>. Spec §0 requires
    /// the shim to provide such a source; this is it, and it is <b>public</b> so a host assembly outside NowUI.Engine can
    /// set it once per frame — an internal setter would leave those overloads permanently at deltaTime == 0 for every
    /// external caller. The full <c>UnityEngine.Time</c> shim (frameCount, realtimeSinceStartup, …) belongs to the host
    /// services layer and forwards here for <c>deltaTime</c>. Default 0.
    /// </summary>
    public static class EngineClock
    {
        private static float s_DeltaTime;

        /// <summary>Seconds elapsed since the previous frame; the value <c>Time.deltaTime</c> would report.</summary>
        public static float deltaTime
        {
            get => s_DeltaTime;
            set => s_DeltaTime = value;
        }
    }
}
