// Mirrors UnityEngine.MissingReferenceException and UnityEngine.UnityException for the NowUI standalone build.
// Spec: Docs/Standalone/UnityValueTypeSemantics.md (VT §13). Design: Docs/Standalone/StandaloneCoreDesign.md (§3.4).
using System;

namespace UnityEngine
{
    /// <summary>
    /// Thrown when managed code touches a member of a destroyed <see cref="Object"/> (VT §13: <c>name</c> and
    /// <c>hideFlags</c> get/set). Derives from <see cref="SystemException"/> exactly as Unity's does, so a
    /// <c>catch (SystemException)</c> in core code behaves the same in both builds.
    /// </summary>
    // No [Serializable]/SerializationInfo constructor: .NET 9 marks formatter-based serialization obsolete (SYSLIB0051)
    // and nothing in the standalone build serialises exceptions.
    public class MissingReferenceException : SystemException
    {
        // VT §13 records the verified 6000.4 text. It is reproduced verbatim because NowUI logs exception messages and
        // the standalone log output is compared against the Unity log output during bring-up.
        internal const string MessageFormat =
            "The object of type '{0}' has been destroyed but you are still trying to access it.\n" +
            "Your script should either check if it is null or you should not destroy the object.";

        public MissingReferenceException()
            : base("A Unity Runtime error occurred!")
        {
        }

        public MissingReferenceException(string message)
            : base(message)
        {
        }

        public MissingReferenceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Builds the exception a destroyed object's member access throws. Internal because it is not a Unity member;
        /// the design forbids adding public non-Unity members to a <c>UnityEngine.*</c> type.
        /// </summary>
        internal static MissingReferenceException For(Type type)
        {
            // Unity names the *declared* type of the destroyed object, e.g. 'UnityEngine.GameObject'.
            return new MissingReferenceException(string.Format(MessageFormat, type == null ? "UnityEngine.Object" : type.FullName));
        }
    }

    /// <summary>
    /// Unity's general runtime exception. Referenced by the standalone build because
    /// <c>Texture2D.GetRawTextureData</c> throws it for a non-readable texture (design §3.4).
    /// </summary>
    // No [Serializable]/SerializationInfo constructor: .NET 9 marks formatter-based serialization obsolete (SYSLIB0051)
    // and nothing in the standalone build serialises exceptions.
    public class UnityException : SystemException
    {
        public UnityException()
            : base("A Unity Runtime error occurred!")
        {
        }

        public UnityException(string message)
            : base(message)
        {
        }

        public UnityException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
