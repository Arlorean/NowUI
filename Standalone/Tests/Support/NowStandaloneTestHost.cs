// The standalone test host: what the Unity Test Runner does for Assets/NowUITests, done by us for `dotnet test`.
//
// Two pieces, both new files of ours (nothing under Assets/NowUITests is touched):
//
//   NowStandaloneTestHost      a root-namespace [SetUpFixture], so it brackets EVERY fixture in the assembly. It
//                              installs the null render backend and the test host services, and mirrors EditMode by
//                              reporting Application.isPlaying == false.
//   NowStandaloneFrameAttribute an assembly-level ITestAction (applied in Shims/AssemblyInfo.cs) that advances the
//                              frame once per test and enforces Unity's log policy around it.
//
// Why the frame advance is per test, not per NowInput.Begin: NowUI's per-frame caches, its frame-keyed registries and
// its one-frame-late pointer arbitration all key off Time.frameCount, and the Unity tests were written for EditMode,
// where frameCount is CONSTANT for the whole duration of a synchronous test. Tests force frame swaps explicitly
// (NowFocus.ForceNewFrame, NowOverlay.ForceNewFrame, NowPointerArbiter.ForceNewFrame) and
// NowControlsTests.ImmediateTabNavigationDoesNotWaitForUnityFrameCount asserts that two NowInput.Begin scopes in one
// test see the same frame. So: exactly one BeginFrame per test, and none inside it (test plan section 4.2, R4).
//
// Ordering note, measured against NUnit 3.14 + NUnit3TestAdapter 4.6 rather than assumed:
//   BeforeTest -> [SetUp] -> test body -> [TearDown] -> AfterTest
// which is what makes it correct to advance the frame in BeforeTest (the fixture's own SetUp then sees the new frame)
// and to evaluate the log policy in AfterTest (logs emitted from TearDown are still counted).
//
// Design: Docs/Standalone/StandaloneCoreDesign.md section 7.2; test plan sections 4.2-4.4.
using System;
using NowUI.Engine;
using NowUI.Standalone.Tests;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEngine.TestTools;

/// <summary>
/// Brackets the whole test assembly. In the root namespace on purpose: NUnit applies a namespace-less
/// <c>[SetUpFixture]</c> to every fixture in the assembly, and the Unity test files declare no namespace.
/// </summary>
[SetUpFixture]
public sealed class NowStandaloneTestHost
{
    private static NowRecordingLogger s_Logger;
    private static NowStandaloneTestResources s_Resources;

    /// <summary>The log recorder for the run. Never null once <see cref="OneTimeSetUp"/> has run.</summary>
    public static NowRecordingLogger logger => s_Logger;

    /// <summary>The resource provider for the run, so a test-support file can register a fixture into it.</summary>
    public static NowStandaloneTestResources resources => s_Resources;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        s_Logger = new NowRecordingLogger();
        NowRecordingLogger.active = s_Logger;

        s_Resources = new NowStandaloneTestResources();

        // EditMode parity: NowDrawList.Dispose takes the DestroyImmediate branch, and the edit-mode gates in
        // NowValueControls / NowFilePicker / Now.cs take the same path they take in the editor (test plan 4.3).
        NowRuntime.isPlaying = false;

        // NowUI's colour maths is written for the gamma pipeline, which is what the Unity EditMode run uses.
        NowRuntime.colorSpace = UnityEngine.ColorSpace.Gamma;

        NowRuntime.Initialize(new NowStandaloneTestHostServices(s_Logger, s_Resources), new NullRenderBackend());
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        // Releases every live shim Object through the backend and restores the default host, so a second run in the
        // same process (a test-adapter re-run) starts from the same state as the first.
        NowRuntime.Shutdown();

        NowRecordingLogger.active = null;
        s_Logger = null;
        s_Resources = null;
    }

    internal static void BeforeTest()
    {
        LogAssert.ignoreFailingMessages = false;
        NowRecordingLogger.active.BeginTest();

        // One frame per test. Frame-keyed registries roll over between tests exactly as they do between editor
        // updates, so stale state from a test that forgot a Reset() cannot alias into the next one.
        NowRuntime.BeginFrame();
    }

    internal static void AfterTest()
    {
        string failure = NowRecordingLogger.active.EndTest();
        LogAssert.ignoreFailingMessages = false;

        if (failure == null)
            return;

        // Only convert the log policy into a failure when the test would otherwise have passed: a test that already
        // failed usually logged the error on its way out, and replacing its diagnosis with ours would lose it.
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Passed)
            Assert.Fail(failure);
    }
}

/// <summary>
/// The assembly-level per-test action. Applied once in <c>Shims/AssemblyInfo.cs</c>; with
/// <see cref="ActionTargets.Test"/> NUnit runs it around every test case in the assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class NowStandaloneFrameAttribute : Attribute, ITestAction
{
    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        NowStandaloneTestHost.BeforeTest();
    }

    public void AfterTest(ITest test)
    {
        NowStandaloneTestHost.AfterTest();
    }
}
