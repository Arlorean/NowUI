// Mirrors UnityEngine.Debug.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 member list and the LogException format rule).
// Behaviour spec: Docs/Standalone/UnityDependencyInventory.md (§A.3 "UnityEngine.Debug").
using System;
using System.Globalization;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// The logging front end. Every call funnels into <c>NowRuntime.host.logger</c>, and <b>nothing is formatted when
    /// there is no logger</b> (design §3.7): the core logs from inside layout and text paths, so a host that discards
    /// logs must not pay for <c>ToString</c> and <c>string.Format</c> on every frame.
    /// </summary>
    public static class Debug
    {
        /// <summary>
        /// What Unity prints for a null message. Reproduced because <c>LogAssert</c>-style matching in the suite
        /// compares whole log lines.
        /// </summary>
        private const string k_NullMessage = "Null";

        /// <summary>Unity's message for a bare failed <see cref="Assert(bool)"/>.</summary>
        private const string k_AssertionFailed = "Assertion failed";

        /// <summary>
        /// True in a Debug configuration, matching Unity, where this is true in the editor and in a development
        /// build. It is a compile-time constant so a release build's guarded logging is removed entirely.
        /// </summary>
        public static bool isDebugBuild =>
#if DEBUG
            true;
#else
            false;
#endif

        public static void Log(object message)
        {
            Dispatch(LogType.Log, message, null);
        }

        public static void Log(object message, Object context)
        {
            Dispatch(LogType.Log, message, context);
        }

        public static void LogWarning(object message)
        {
            Dispatch(LogType.Warning, message, null);
        }

        public static void LogWarning(object message, Object context)
        {
            Dispatch(LogType.Warning, message, context);
        }

        public static void LogError(object message)
        {
            Dispatch(LogType.Error, message, null);
        }

        public static void LogError(object message, Object context)
        {
            Dispatch(LogType.Error, message, context);
        }

        public static void LogAssertion(object message)
        {
            Dispatch(LogType.Assert, message, null);
        }

        public static void LogAssertion(object message, Object context)
        {
            Dispatch(LogType.Assert, message, context);
        }

        /// <summary>
        /// Logs an exception. The message is <c>"{TypeName}: {Message}"</c> and that exact shape is load-bearing:
        /// <c>NowTextPreprocessorTests</c> matches on it through <c>LogAssert</c> (design §3.7, TP §2.2).
        /// </summary>
        public static void LogException(Exception exception)
        {
            LogException(exception, null);
        }

        /// <inheritdoc cref="LogException(System.Exception)"/>
        public static void LogException(Exception exception, Object context)
        {
            INowLogger logger = NowRuntime.host.logger;
            if (logger == null)
                return;

            logger.Log(LogType.Exception, FormatException(exception), exception, context);
        }

        public static void LogFormat(string format, params object[] args)
        {
            DispatchFormat(LogType.Log, format, args);
        }

        public static void LogWarningFormat(string format, params object[] args)
        {
            DispatchFormat(LogType.Warning, format, args);
        }

        public static void LogErrorFormat(string format, params object[] args)
        {
            DispatchFormat(LogType.Error, format, args);
        }

        /// <summary>Logs <c>"Assertion failed"</c> at assert severity when <paramref name="condition"/> is false.</summary>
        public static void Assert(bool condition)
        {
            if (condition)
                return;

            INowLogger logger = NowRuntime.host.logger;
            if (logger == null)
                return;

            logger.Log(LogType.Assert, k_AssertionFailed, null, null);
        }

        /// <inheritdoc cref="Assert(bool)"/>
        public static void Assert(bool condition, string message)
        {
            if (condition)
                return;

            INowLogger logger = NowRuntime.host.logger;
            if (logger == null)
                return;

            logger.Log(LogType.Assert, message ?? k_NullMessage, null, null);
        }

        /// <summary>
        /// The exception format the suite matches on. Deliberately not <c>exception.ToString()</c>, which would append
        /// the stack trace: the host's logger receives the exception object itself and decides whether to print one.
        /// </summary>
        internal static string FormatException(Exception exception)
        {
            if (exception == null)
                return k_NullMessage;

            return exception.GetType().Name + ": " + exception.Message;
        }

        private static void Dispatch(LogType type, object message, Object context)
        {
            INowLogger logger = NowRuntime.host.logger;

            // The whole point of the early-out: message.ToString() on a struct boxes and on a builder allocates, and
            // a discarding host would pay it on every frame (design §1.2).
            if (logger == null)
                return;

            logger.Log(type, message == null ? k_NullMessage : message.ToString(), null, context);
        }

        private static void DispatchFormat(LogType type, string format, object[] args)
        {
            INowLogger logger = NowRuntime.host.logger;
            if (logger == null)
                return;

            logger.Log(type, Format(format, args), null, null);
        }

        /// <summary>
        /// Invariant-culture formatting, as Unity's own <c>UnityString.Format</c> does: a log line must read the same
        /// under every locale so that a matcher written against it keeps working.
        /// </summary>
        private static string Format(string format, object[] args)
        {
            if (format == null)
                return k_NullMessage;

            if (args == null || args.Length == 0)
                return format;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                // A malformed format string must not take down the caller: Unity logs the raw format rather than
                // throwing out of a logging call, and losing a log line is always better than losing a frame.
                return format;
            }
        }
    }
}
