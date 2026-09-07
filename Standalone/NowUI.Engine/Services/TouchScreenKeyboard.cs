// Mirrors UnityEngine.TouchScreenKeyboard.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 member list; §4.4 INowTouchKeyboard/Session).
// Behaviour spec: Docs/Standalone/GradientCurveSemantics.md §9 (the eight Open overloads, Status values, isSupported).
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// A soft keyboard session. Not sealed and not a <c>UnityEngine.Object</c>, exactly as in Unity (GC §9), because
    /// core code plain-null-checks it.
    /// </summary>
    /// <remarks>
    /// With no <c>INowTouchKeyboard</c> on the host - the M1 desktop and browser case - <see cref="isSupported"/> is
    /// false and NowUI never opens one. Unity's own unsupported-platform object throws
    /// <c>NullReferenceException</c> from every instance member; this one instead reports a
    /// <see cref="Status.Canceled"/>, inactive session over the text it was constructed with. That is the deliberate
    /// divergence: a caller that ignored <see cref="isSupported"/> gets NowUI's "the keyboard went away" path, which
    /// clears focus and drops the reference, rather than an exception out of a draw call.
    /// </remarks>
    public class TouchScreenKeyboard
    {
        /// <summary>Where an open keyboard is in its lifecycle. NowUI acts on <see cref="Visible"/> and <see cref="Done"/>.</summary>
        public enum Status
        {
            Visible = 0,
            Done = 1,
            Canceled = 2,
            LostFocus = 3,
        }

        private static TouchScreenKeyboard s_Current;

        private readonly INowTouchKeyboardSession m_Session;
        private readonly TouchScreenKeyboardType m_Type;
        private string m_Text;
        private int m_CharacterLimit;

        /// <summary>
        /// The one real constructor; the eight <c>Open</c> overloads all land here, as in Unity. <c>alert</c> and
        /// <c>textPlaceholder</c> are accepted and not forwarded: the host contract (§4.4) carries neither, and a host
        /// that wants them extends its own interface rather than having the shim invent a channel.
        /// </summary>
        public TouchScreenKeyboard(
            string text,
            TouchScreenKeyboardType keyboardType,
            bool autocorrection,
            bool multiline,
            bool secure,
            bool alert,
            string textPlaceholder,
            int characterLimit)
        {
            m_Text = text ?? "";
            m_Type = keyboardType;
            m_CharacterLimit = characterLimit;

            INowTouchKeyboard keyboard = NowRuntime.host.touchKeyboard;
            if (keyboard != null)
                m_Session = keyboard.Open(m_Text, keyboardType, autocorrection, multiline, secure);

            // Unity's `visible` and `area` describe "the" keyboard, of which there is at most one on any platform that
            // has one; tracking the most recently opened instance is how those statics can answer at all.
            s_Current = this;
        }

        /// <summary>
        /// Whether this platform has a soft keyboard - i.e. whether the host registered one. False on every M1 host
        /// (design §3.7), which is the branch NowTextField and NowTextArea take.
        /// </summary>
        public static bool isSupported => NowRuntime.host.touchKeyboard != null;

        /// <summary>Whether the platform hides its own input field over the keyboard. Host-set; false by default.</summary>
        public static bool hideInput { get; set; }

        /// <summary>Whether a keyboard is on screen right now.</summary>
        public static bool visible
        {
            get
            {
                TouchScreenKeyboard current = s_Current;
                return current != null && current.active && current.status == Status.Visible;
            }
        }

        /// <summary>
        /// The screen rectangle the keyboard covers.
        /// </summary>
        /// <remarks>
        /// Always <c>Rect.zero</c>, which is what Unity reports where there is no keyboard (GC §9).
        /// <c>INowTouchKeyboardSession</c> carries no geometry, so any other value would be invented.
        /// </remarks>
        public static Rect area => Rect.zero;

        /// <summary>The text being edited. Mirrored into the focused field by NowUI while the status is Visible.</summary>
        public string text
        {
            get
            {
                INowTouchKeyboardSession session = m_Session;
                if (session == null)
                    return m_Text;

                string current = session.text;
                m_Text = current ?? "";
                return m_Text;
            }
            set
            {
                m_Text = value ?? "";

                INowTouchKeyboardSession session = m_Session;
                if (session != null)
                    session.text = m_Text;
            }
        }

        /// <summary>
        /// Whether the keyboard is on screen or sliding in. Setting it false closes the keyboard, which is how NowUI
        /// releases one before dropping the reference.
        /// </summary>
        public bool active
        {
            get
            {
                INowTouchKeyboardSession session = m_Session;
                return session != null && session.active;
            }
            set
            {
                INowTouchKeyboardSession session = m_Session;
                if (session != null)
                    session.active = value;
            }
        }

        /// <summary>Lifecycle state. <see cref="Status.Canceled"/> when there is no host keyboard behind this object.</summary>
        public Status status
        {
            get
            {
                INowTouchKeyboardSession session = m_Session;
                return session == null ? Status.Canceled : session.status;
            }
        }

        /// <summary>The layout this keyboard was opened with.</summary>
        public TouchScreenKeyboardType type => m_Type;

        /// <summary>
        /// The maximum number of characters, 0 meaning unlimited. Stored rather than forwarded: the host contract
        /// carries no limit, and NowUI enforces its own limit on the field it mirrors into.
        /// </summary>
        public int characterLimit
        {
            get => m_CharacterLimit;
            set => m_CharacterLimit = value;
        }

        // The eight explicit overloads of GC §9. Unity declares no optional parameters here, and neither may the shim:
        // optional parameters would change the emitted call sites and the public API dump.

        public static TouchScreenKeyboard Open(string text)
        {
            return new TouchScreenKeyboard(text, TouchScreenKeyboardType.Default, true, false, false, false, "", 0);
        }

        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType)
        {
            return new TouchScreenKeyboard(text, keyboardType, true, false, false, false, "", 0);
        }

        public static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType, bool autocorrection)
        {
            return new TouchScreenKeyboard(text, keyboardType, autocorrection, false, false, false, "", 0);
        }

        public static TouchScreenKeyboard Open(
            string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline)
        {
            return new TouchScreenKeyboard(text, keyboardType, autocorrection, multiline, false, false, "", 0);
        }

        public static TouchScreenKeyboard Open(
            string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline, bool secure)
        {
            return new TouchScreenKeyboard(text, keyboardType, autocorrection, multiline, secure, false, "", 0);
        }

        public static TouchScreenKeyboard Open(
            string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline, bool secure,
            bool alert)
        {
            return new TouchScreenKeyboard(text, keyboardType, autocorrection, multiline, secure, alert, "", 0);
        }

        public static TouchScreenKeyboard Open(
            string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline, bool secure,
            bool alert, string textPlaceholder)
        {
            return new TouchScreenKeyboard(
                text, keyboardType, autocorrection, multiline, secure, alert, textPlaceholder, 0);
        }

        public static TouchScreenKeyboard Open(
            string text, TouchScreenKeyboardType keyboardType, bool autocorrection, bool multiline, bool secure,
            bool alert, string textPlaceholder, int characterLimit)
        {
            return new TouchScreenKeyboard(
                text, keyboardType, autocorrection, multiline, secure, alert, textPlaceholder, characterLimit);
        }
    }
}
