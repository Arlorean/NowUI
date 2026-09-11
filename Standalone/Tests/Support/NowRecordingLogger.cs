// The log sink behind UnityEngine.TestTools.LogAssert.
//
// The standalone test host installs this as the shim's INowLogger, so every UnityEngine.Debug call the core makes lands
// here instead of on the console. Two jobs: satisfy the explicit LogAssert.Expect assertions three gate files make, and
// enforce Unity's implicit rule that an unexpected error-level log fails the test (test plan section 4.4, R7).
//
// New file of ours; nothing under Assets/NowUITests is modified.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace NowUI.Standalone.Tests
{
    /// <summary>One registered expectation: a severity plus either an exact message or a pattern.</summary>
    internal readonly struct NowExpectedLog
    {
        public readonly UnityEngine.LogType type;
        public readonly string message;
        public readonly Regex pattern;

        public NowExpectedLog(UnityEngine.LogType type, string message, Regex pattern)
        {
            this.type = type;
            this.message = message;
            this.pattern = pattern;
        }

        public bool Matches(UnityEngine.LogType candidateType, string candidateMessage)
        {
            if (candidateType != type)
                return false;

            if (pattern != null)
                return candidateMessage != null && pattern.IsMatch(candidateMessage);

            return string.Equals(message, candidateMessage, StringComparison.Ordinal);
        }

        public override string ToString()
        {
            return "[" + type + "] " + (pattern != null ? pattern.ToString() : message);
        }
    }

    /// <summary>One log the shim emitted during the current test.</summary>
    internal readonly struct NowReceivedLog
    {
        public readonly UnityEngine.LogType type;
        public readonly string message;

        public NowReceivedLog(UnityEngine.LogType type, string message)
        {
            this.type = type;
            this.message = message;
        }

        /// <summary>The severities Unity's test runner fails a test for when they are not expected.</summary>
        public bool isFailure =>
            type == UnityEngine.LogType.Error ||
            type == UnityEngine.LogType.Assert ||
            type == UnityEngine.LogType.Exception;

        public override string ToString()
        {
            return "[" + type + "] " + message;
        }
    }

    /// <summary>
    /// Records the current test's logs and matches them against the expectations <c>LogAssert</c> registers.
    /// </summary>
    public sealed class NowRecordingLogger : NowUI.Engine.INowLogger
    {
        private static NowRecordingLogger s_Active = new NowRecordingLogger();

        private readonly List<NowExpectedLog> m_Expected = new List<NowExpectedLog>();
        private readonly List<NowReceivedLog> m_Received = new List<NowReceivedLog>();

        /// <summary>
        /// The instance <c>LogAssert</c> talks to. Never null even before the host installs one, so a stray log during
        /// assembly initialisation has somewhere to go instead of throwing.
        /// </summary>
        internal static NowRecordingLogger active
        {
            get { return s_Active; }
            set { s_Active = value ?? new NowRecordingLogger(); }
        }

        /// <summary>
        /// Mirrors every log to stdout as well. Off by default: a green run should be quiet, and a failing one reports
        /// the entries through the assertion message. Turn it on while diagnosing a shim gap.
        /// </summary>
        public bool echoToConsole { get; set; }

        void NowUI.Engine.INowLogger.Log(UnityEngine.LogType type, string message, Exception exception, UnityEngine.Object context)
        {
            // The shim already formats Debug.LogException as "TypeName: Message" and passes it as `message`, which is
            // exactly the string NowTextPreprocessorTests expects; the exception object is only a fallback here.
            string text = message ?? (exception != null ? exception.GetType().Name + ": " + exception.Message : "Null");

            if (echoToConsole)
                Console.Out.WriteLine("[" + type + "] " + text);

            // FIFO over the expectations, so two Expect calls for the same type consume the first two matching logs in
            // the order they were registered - Unity's rule.
            for (int i = 0; i < m_Expected.Count; i++)
            {
                if (!m_Expected[i].Matches(type, text))
                    continue;

                m_Expected.RemoveAt(i);
                return;
            }

            m_Received.Add(new NowReceivedLog(type, text));
        }

        /// <summary>Clears both queues. Called before every test by the assembly-level action.</summary>
        internal void BeginTest()
        {
            m_Expected.Clear();
            m_Received.Clear();
        }

        internal void Expect(NowExpectedLog expectation)
        {
            // An Expect that arrives after the log consumes the log, which is what lets a test log first and assert
            // afterwards (Unity allows both orders).
            for (int i = 0; i < m_Received.Count; i++)
            {
                if (!expectation.Matches(m_Received[i].type, m_Received[i].message))
                    continue;

                m_Received.RemoveAt(i);
                return;
            }

            m_Expected.Add(expectation);
        }

        internal void AssertNoUnexpectedReceived()
        {
            if (m_Received.Count == 0)
                return;

            string report = Describe("Unexpected log message(s) received:", m_Received);
            m_Received.Clear();
            NUnit.Framework.Assert.Fail(report);
        }

        /// <summary>
        /// The end-of-test policy: an expectation that never matched fails, and so does an unconsumed error-level log
        /// unless <c>LogAssert.ignoreFailingMessages</c> was set. Returns null when the test is clean.
        /// </summary>
        internal string EndTest()
        {
            StringBuilder builder = null;

            if (m_Expected.Count > 0)
            {
                builder = new StringBuilder("Expected log message(s) that were never received:");
                for (int i = 0; i < m_Expected.Count; i++)
                    builder.Append(Environment.NewLine).Append("  ").Append(m_Expected[i].ToString());
            }

            if (!UnityEngine.TestTools.LogAssert.ignoreFailingMessages)
            {
                bool headerWritten = false;
                for (int i = 0; i < m_Received.Count; i++)
                {
                    if (!m_Received[i].isFailure)
                        continue;

                    if (builder == null)
                        builder = new StringBuilder();
                    else if (!headerWritten)
                        builder.Append(Environment.NewLine);

                    if (!headerWritten)
                    {
                        builder.Append("Unexpected error log(s) received:");
                        headerWritten = true;
                    }

                    builder.Append(Environment.NewLine).Append("  ").Append(m_Received[i].ToString());
                }
            }

            m_Expected.Clear();
            m_Received.Clear();

            return builder == null ? null : builder.ToString();
        }

        private static string Describe(string header, List<NowReceivedLog> entries)
        {
            StringBuilder builder = new StringBuilder(header);
            for (int i = 0; i < entries.Count; i++)
                builder.Append(Environment.NewLine).Append("  ").Append(entries[i].ToString());
            return builder.ToString();
        }
    }
}
