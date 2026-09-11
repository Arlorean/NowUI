// UnityEngine.TestTools.LogAssert for the engine-free test run.
//
// This is a NEW file of ours, not an edit to any Unity test: the tests under Assets/NowUITests call LogAssert and the
// dotnet runner has no Unity test framework to provide it. The behaviour reproduced here is Unity's, because Unity's
// behaviour is what the tests were written against:
//
//   * Expect(type, message) registers an expectation. It matches a log that was already received during this test as
//     well as one that arrives later - Unity's LogAssert.Expect may be called on either side of the call that logs.
//   * Matching is FIFO over the expectations, by exact string or by Regex.IsMatch, within the same LogType.
//   * NoUnexpectedReceived() fails on ANY unconsumed entry, at any severity.
//   * At the end of a test, an unconsumed Error/Assert/Exception entry fails it, and an expectation that never matched
//     fails it. That implicit rule is what turns a missing font or material into a visible failure instead of silently
//     blank geometry (test plan section 4.4 and R7).
//
// The matching itself lives in Support/NowRecordingLogger.cs, which is the INowLogger the test host installs into the
// shim's Debug sink. Design: Docs/Standalone/StandaloneCoreDesign.md section 7.2 ("Shims/LogAssert.cs").
using System;
using System.Text.RegularExpressions;
using NowUI.Standalone.Tests;

namespace UnityEngine.TestTools
{
    /// <summary>
    /// Unity's log-expectation API, backed by <see cref="NowRecordingLogger"/>, which the standalone test host installs
    /// as the shim's <c>INowLogger</c>.
    /// </summary>
    public static class LogAssert
    {
        /// <summary>
        /// When true, unexpected error-level logs stop failing tests. Unity resets it per test; so does this shim, from
        /// the assembly-level per-test action in <see cref="NowStandaloneTestHost"/>.
        /// </summary>
        public static bool ignoreFailingMessages { get; set; }

        /// <summary>Expects one log of <paramref name="type"/> whose message equals <paramref name="message"/>.</summary>
        public static void Expect(LogType type, string message)
        {
            NowRecordingLogger.active.Expect(new NowExpectedLog(type, message, null));
        }

        /// <summary>Expects one log of <paramref name="type"/> whose message matches <paramref name="message"/>.</summary>
        public static void Expect(LogType type, Regex message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            NowRecordingLogger.active.Expect(new NowExpectedLog(type, null, message));
        }

        /// <summary>
        /// Fails when any log received in this test has not been consumed by an <see cref="Expect(LogType,string)"/> -
        /// at any severity, which is stricter than the implicit end-of-test rule.
        /// </summary>
        public static void NoUnexpectedReceived()
        {
            NowRecordingLogger.active.AssertNoUnexpectedReceived();
        }
    }
}
