// Unity.PerformanceTesting for the engine-free test run.
//
// New file of ours. Only the surface the Unity tests actually touch is here, and it is a no-op that STILL EXECUTES THE
// BODY: Measure.Method(...).Run() runs the warmup and measurement iterations exactly as Unity's does, it just throws
// the timings away instead of writing a performance report. That matters because the tests use the measurement loop as
// a warmup for the allocation assertions that follow, and a stub that skipped the body would change what they measure.
//
// PerformanceAttribute derives from NUnit's CategoryAttribute("Performance") the way Unity's does, so
// `--filter Category!=Performance` selects the same set here as in the editor.
//
// Design: Docs/Standalone/StandaloneCoreDesign.md section 7.2 ("Shims/PerformanceTesting.cs");
// surface list: Docs/Standalone/StandaloneTestPlan.md section 5.
using System;

namespace Unity.PerformanceTesting
{
    /// <summary>The unit a <see cref="SampleGroup"/> reports in. Values and names are Unity's.</summary>
    public enum SampleUnit
    {
        Nanosecond,
        Microsecond,
        Millisecond,
        Second,
        Byte,
        Kilobyte,
        Megabyte,
        Gigabyte,
        Undefined,
    }

    /// <summary>
    /// Marks a performance test. Derived from <c>CategoryAttribute("Performance")</c>, as Unity's is, so the category
    /// filters behave identically in both runners.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class PerformanceAttribute : NUnit.Framework.CategoryAttribute
    {
        public PerformanceAttribute()
            : base("Performance")
        {
        }
    }

    /// <summary>A named measurement channel. Carried through the API and discarded at the end of it.</summary>
    public sealed class SampleGroup
    {
        public string Name { get; }

        public SampleUnit Unit { get; }

        public bool IncreaseIsBetter { get; }

        public SampleGroup(string name)
            : this(name, SampleUnit.Millisecond, false)
        {
        }

        public SampleGroup(string name, SampleUnit unit)
            : this(name, unit, false)
        {
        }

        public SampleGroup(string name, SampleUnit unit, bool increaseIsBetter)
        {
            Name = name;
            Unit = unit;
            IncreaseIsBetter = increaseIsBetter;
        }

        public override string ToString()
        {
            return Name + " (" + Unit + ")";
        }
    }

    /// <summary>
    /// The measurement builder. Every setter returns <c>this</c> so the tests' fluent chains compile unchanged, and
    /// <see cref="Run"/> executes warmup + measurement iterations for real.
    /// </summary>
    public sealed class MethodMeasurement
    {
        private const int k_DefaultWarmupCount = 3;
        private const int k_DefaultMeasurementCount = 7;
        private const int k_DefaultIterations = 1;

        private readonly Action m_Action;
        private Action m_Setup;
        private Action m_Cleanup;
        private SampleGroup m_SampleGroup;
        private int m_WarmupCount = k_DefaultWarmupCount;
        private int m_MeasurementCount = k_DefaultMeasurementCount;
        private int m_Iterations = k_DefaultIterations;

        internal MethodMeasurement(Action action)
        {
            m_Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public MethodMeasurement SampleGroup(string name)
        {
            m_SampleGroup = new SampleGroup(name);
            return this;
        }

        public MethodMeasurement SampleGroup(SampleGroup sampleGroup)
        {
            m_SampleGroup = sampleGroup;
            return this;
        }

        public MethodMeasurement WarmupCount(int count)
        {
            m_WarmupCount = count;
            return this;
        }

        public MethodMeasurement MeasurementCount(int count)
        {
            m_MeasurementCount = count;
            return this;
        }

        public MethodMeasurement IterationsPerMeasurement(int count)
        {
            m_Iterations = count;
            return this;
        }

        public MethodMeasurement SetUp(Action action)
        {
            m_Setup = action;
            return this;
        }

        public MethodMeasurement CleanUp(Action action)
        {
            m_Cleanup = action;
            return this;
        }

        /// <summary>
        /// Runs the body warmup + measurement times. No timing is recorded; the run itself is the contract, because the
        /// tests rely on it to warm caches before their own assertions.
        /// </summary>
        public void Run()
        {
            int warmup = m_WarmupCount < 0 ? 0 : m_WarmupCount;
            int measurements = m_MeasurementCount < 0 ? 0 : m_MeasurementCount;
            int iterations = m_Iterations < 1 ? 1 : m_Iterations;

            for (int i = 0; i < warmup; i++)
                Invoke(iterations);

            for (int i = 0; i < measurements; i++)
                Invoke(iterations);
        }

        private void Invoke(int iterations)
        {
            if (m_Setup != null)
                m_Setup();

            for (int i = 0; i < iterations; i++)
                m_Action();

            if (m_Cleanup != null)
                m_Cleanup();
        }
    }

    /// <summary>The entry points the Unity tests call.</summary>
    public static class Measure
    {
        public static MethodMeasurement Method(Action action)
        {
            return new MethodMeasurement(action);
        }

        /// <summary>Records one sample. Discarded: there is no performance report in the standalone run.</summary>
        public static void Custom(SampleGroup sampleGroup, double value)
        {
        }

        /// <summary>Records one sample under a freshly named group. Discarded, as above.</summary>
        public static void Custom(string name, double value)
        {
        }
    }
}
