// UnityEngine.Profiling.Recorder for the engine-free test run.
//
// New file of ours. NowBenchmarkAllocations probes two allocation backends: GC.GetAllocatedBytesForCurrentThread (exact
// on CoreCLR, so `bytesAvailable` is true) and Unity's "GC.Alloc" profiler recorder (its fallback on Mono builds where
// the byte API returns zero). There is no profiler here, so every Recorder reports isValid == false and the benchmark
// helper takes the byte path - which is the stricter of the two and the one test plan R6 discusses.
//
// Design: Docs/Standalone/StandaloneCoreDesign.md section 7.2 ("Shims/Recorder.cs").
namespace UnityEngine.Profiling
{
    /// <summary>
    /// A profiler sampler handle that is never valid. Everything is a no-op, which is exactly what the "no profiler
    /// backend" case looks like in Unity too, so <c>NowBenchmarkAllocations</c> takes a path it already handles.
    /// </summary>
    public sealed class Recorder
    {
        private static readonly Recorder s_Invalid = new Recorder();

        private Recorder()
        {
        }

        /// <summary>Unity returns a non-null, invalid recorder for an unknown sampler; so does this.</summary>
        public static Recorder Get(string samplerName)
        {
            return s_Invalid;
        }

        /// <summary>Always false: there is no profiler backend behind the standalone run.</summary>
        public bool isValid => false;

        /// <summary>Settable and ignored, mirroring an invalid Unity recorder.</summary>
        public bool enabled { get; set; }

        /// <summary>Samples collected while enabled. Always zero here.</summary>
        public int sampleBlockCount => 0;

        /// <summary>Nanoseconds collected while enabled. Always zero here.</summary>
        public long elapsedNanoseconds => 0L;

        public void FilterToCurrentThread()
        {
        }

        public void CollectFromAllThreads()
        {
        }
    }
}
