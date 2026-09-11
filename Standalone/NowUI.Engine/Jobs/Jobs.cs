// Mirrors Unity.Jobs: IJob, IJobParallelFor, JobHandle, IJobExtensions and IJobParallelForExtensions.
//
// Governed by Docs/Standalone/StandaloneCoreDesign.md section 3.9. The members NowUI calls are listed in
// Docs/Standalone/UnityDependencyInventory.md section A row "Unity.Jobs.*": IJob, IJobExtensions.Run,
// IJobParallelFor, IJobParallelForExtensions.Schedule(int,int) and JobHandle.Complete().
//
// WHY everything runs synchronously on the calling thread: the standalone target is single-threaded .NET
// WebAssembly, and design section 3.9 fixes the semantics as "execution is synchronous on the calling thread at
// Schedule time". A parallel-for is therefore just `for (i = 0; i < arrayLength; ++i) Execute(i)` in index order.
// The job struct is copied by value on the way in exactly as Unity does, so results have to flow out through
// NativeArray buffers either way and there is no observable difference beyond timing and index ordering — and the
// one parallel-for NowUI schedules (NowSdfBakeJob) writes disjoint output ranges, so ordering is unobservable too.

using System;

namespace Unity.Jobs
{
    /// <summary>Mirrors <c>Unity.Jobs.IJob</c>.</summary>
    public interface IJob
    {
        void Execute();
    }

    /// <summary>Mirrors <c>Unity.Jobs.IJobParallelFor</c>.</summary>
    public interface IJobParallelFor
    {
        void Execute(int index);
    }

    /// <summary>
    /// Mirrors <c>Unity.Jobs.JobHandle</c>. Every handle the shim hands back refers to work that has already
    /// finished, so <see cref="Complete"/> is a no-op and <see cref="IsCompleted"/> is always true.
    /// </summary>
    public struct JobHandle : IEquatable<JobHandle>
    {
        /// <summary>
        /// No-op: the job ran to completion inside <c>Schedule</c>. Kept as a real method (rather than removed)
        /// because NowManagedFontSession writes <c>...Schedule(cellCount, 1).Complete()</c> verbatim.
        /// </summary>
        public void Complete()
        {
        }

        /// <summary>Always true — see <see cref="Complete"/>.</summary>
        public bool IsCompleted => true;

        /// <summary>No-op; both jobs already ran.</summary>
        public static void CompleteAll(ref JobHandle job0, ref JobHandle job1)
        {
        }

        /// <summary>No-op; all three jobs already ran.</summary>
        public static void CompleteAll(ref JobHandle job0, ref JobHandle job1, ref JobHandle job2)
        {
        }

        /// <summary>Returns a completed handle: there is nothing left to depend on.</summary>
        public static JobHandle CombineDependencies(JobHandle job0, JobHandle job1) => default;

        /// <summary>Returns a completed handle: there is nothing left to depend on.</summary>
        public static JobHandle CombineDependencies(JobHandle job0, JobHandle job1, JobHandle job2) => default;

        /// <summary>No-op; scheduled work is already flushed because it ran inline.</summary>
        public static void ScheduleBatchedJobs()
        {
        }

        /// <summary>All handles are equivalent, so any two compare equal.</summary>
        public bool Equals(JobHandle other) => true;

        public override bool Equals(object obj) => obj is JobHandle;

        public override int GetHashCode() => 0;

        public static bool operator ==(JobHandle left, JobHandle right) => true;

        public static bool operator !=(JobHandle left, JobHandle right) => false;
    }

    /// <summary>Mirrors <c>Unity.Jobs.IJobExtensions</c>.</summary>
    public static class IJobExtensions
    {
        /// <summary>
        /// Runs the job on the calling thread. <typeparamref name="T"/> is a struct constrained to
        /// <see cref="IJob"/>, so the call is a constrained callvirt on the local copy: no boxing, no allocation.
        /// </summary>
        public static void Run<T>(this T jobData) where T : struct, IJob => jobData.Execute();

        /// <summary>
        /// Runs the job immediately and returns an already-completed handle. <paramref name="dependsOn"/> is
        /// ignored because any dependency has, by construction, already run.
        /// </summary>
        public static JobHandle Schedule<T>(this T jobData, JobHandle dependsOn = default) where T : struct, IJob
        {
            jobData.Execute();
            return default;
        }
    }

    /// <summary>Mirrors <c>Unity.Jobs.IJobParallelForExtensions</c>.</summary>
    public static class IJobParallelForExtensions
    {
        /// <summary>
        /// Runs <c>Execute(i)</c> for every <c>i</c> in <c>[0, arrayLength)</c> in ascending order on the calling
        /// thread, then returns a completed handle. <paramref name="innerloopBatchCount"/> only affects Unity's
        /// work distribution, so the shim ignores it after validating it the way Unity does.
        /// </summary>
        public static JobHandle Schedule<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default)
            where T : struct, IJobParallelFor
        {
            if (arrayLength < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayLength), "Array length must be >= 0.");

            if (innerloopBatchCount < 0)
                throw new ArgumentOutOfRangeException(nameof(innerloopBatchCount), "Inner loop batch count must be >= 0.");

            for (int i = 0; i < arrayLength; ++i)
                jobData.Execute(i);

            return default;
        }

        /// <summary>Runs <c>Execute(i)</c> for every <c>i</c> in <c>[0, arrayLength)</c> on the calling thread.</summary>
        public static void Run<T>(this T jobData, int arrayLength) where T : struct, IJobParallelFor
        {
            if (arrayLength < 0)
                throw new ArgumentOutOfRangeException(nameof(arrayLength), "Array length must be >= 0.");

            for (int i = 0; i < arrayLength; ++i)
                jobData.Execute(i);
        }

        /// <summary>
        /// Same as <see cref="Schedule{T}"/>; Unity's variant only differs in how it splits the range across
        /// worker threads, of which the shim has none.
        /// </summary>
        public static JobHandle ScheduleBatch<T>(this T jobData, int arrayLength, int indicesPerJobCount, JobHandle dependsOn = default)
            where T : struct, IJobParallelFor
            => Schedule(jobData, arrayLength, indicesPerJobCount, dependsOn);
    }
}
