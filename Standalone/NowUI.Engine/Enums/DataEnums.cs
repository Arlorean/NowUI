// Mirrors the UnityEngine data / diagnostics / platform enums for the NowUI standalone build:
// GradientMode, WrapMode, WeightedMode, LogType, LogOption, SystemLanguage, ScreenOrientation,
// RuntimeInitializeLoadType and TouchScreenKeyboardType.
// Governed by StandaloneCoreDesign.md §3.2 ("Enums/DataEnums.cs") and GradientCurveSemantics.md §2, §4.1, §4.3, §9;
// values verified against UnityEngine.CoreModule.dll from Unity 6000.4.0f1.

namespace UnityEngine
{
    /// <summary>
    /// How a <see cref="Gradient"/> interpolates between keys (GC §2, design §3.2).
    /// </summary>
    /// <remarks>
    /// Unity stores <c>Gradient.mode</c> in 8 bits, so out-of-range writes survive: setting <c>(GradientMode)(-1)</c>
    /// reads back as <c>255</c> and <c>7</c> reads back as <c>7</c> [GC §2, verified]. Evaluation of an undefined
    /// value is unspecified in Unity; per GC §2 the shim treats 1 as Fixed, 2 as PerceptualBlend and everything else
    /// as Blend. That decision lives in the Gradient implementation (U12), not here.
    /// </remarks>
    public enum GradientMode
    {
        /// <summary>Linear interpolation between adjacent keys.</summary>
        Blend = 0,
        /// <summary>Hold the previous key's value until the next key (point sampling).</summary>
        Fixed = 1,
        /// <summary>Blend in a perceptual (Oklab-like) space rather than linearly per channel.</summary>
        PerceptualBlend = 2,
    }

    /// <summary>
    /// How an <see cref="AnimationCurve"/> behaves outside its key range (GC §4.3, design §3.2).
    /// </summary>
    /// <remarks>
    /// <para><c>Clamp</c> and <c>Once</c> are the same value (1). That is Unity's, and it is why this enum cannot be
    /// pattern-matched exhaustively by name — two names, one number.</para>
    /// <para>The values look like flags (1/2/4/8) and Unity does bit tests on them internally, but the type is
    /// <em>not</em> <c>[Flags]</c>-attributed in Unity, so it is not attributed here either (the attribute is part
    /// of the observable API surface: it changes <c>ToString()</c> and <c>Enum.Format</c>).</para>
    /// <para>Curve behaviour (GC §5.7): <c>Default</c> evaluates as <c>Loop</c>, and <c>Once</c>/<c>Clamp</c> are
    /// silently stored as <c>ClampForever</c> by the curve. That is U12's business, not this file's.</para>
    /// </remarks>
    public enum WrapMode
    {
        Once = 1,
        Loop = 2,
        PingPong = 4,
        Default = 0,
        ClampForever = 8,
        Clamp = 1,
    }

    /// <summary>
    /// Which side(s) of a <see cref="Keyframe"/> use weighted tangents (GC §4.1, design §3.2).
    /// </summary>
    /// <remarks>
    /// Used as bits (<c>Both == In | Out</c>) and NowUI ors/ands the values directly, but — like
    /// <see cref="WrapMode"/> — Unity does not attribute it <c>[Flags]</c>, so neither does the shim.
    /// <c>Keyframe.weightedMode</c> stores the raw int, so values such as 7 and -1 round-trip (GC §4.2).
    /// </remarks>
    public enum WeightedMode
    {
        None = 0,
        In = 1,
        Out = 2,
        Both = 3,
    }

    /// <summary>
    /// Severity of a message passed to <c>Debug</c> / <c>ILogger</c> (design §3.2).
    /// </summary>
    /// <remarks>
    /// The ordering is severity-descending, not ascending: <c>Error</c> is 0 and <c>Log</c> is 3, so numeric
    /// comparison against a "minimum level" runs the opposite way from most logging frameworks.
    /// </remarks>
    public enum LogType
    {
        Error = 0,
        Assert = 1,
        Warning = 2,
        Log = 3,
        Exception = 4,
    }

    /// <summary>Stack-trace behaviour for a single log call (design §3.2).</summary>
    public enum LogOption
    {
        None = 0,
        NoStacktrace = 1,
    }

    /// <summary>
    /// The editor/player's language, as reported by <c>Application.systemLanguage</c> (design §3.2 — ship the
    /// complete enum).
    /// </summary>
    /// <remarks>
    /// <c>Hugarian</c> (sic) is Unity's original misspelling and is kept because removing it would break any code
    /// that names it; <c>Hungarian</c> is the corrected alias and carries the same value, 18.
    /// </remarks>
    public enum SystemLanguage
    {
        Afrikaans = 0,
        Arabic = 1,
        Basque = 2,
        Belarusian = 3,
        Bulgarian = 4,
        Catalan = 5,
        Chinese = 6,
        Czech = 7,
        Danish = 8,
        Dutch = 9,
        English = 10,
        Estonian = 11,
        Faroese = 12,
        Finnish = 13,
        French = 14,
        German = 15,
        Greek = 16,
        Hebrew = 17,
        Hugarian = 18,
        Icelandic = 19,
        Indonesian = 20,
        Italian = 21,
        Japanese = 22,
        Korean = 23,
        Latvian = 24,
        Lithuanian = 25,
        Norwegian = 26,
        Polish = 27,
        Portuguese = 28,
        Romanian = 29,
        Russian = 30,
        SerboCroatian = 31,
        Slovak = 32,
        Slovenian = 33,
        Spanish = 34,
        Swedish = 35,
        Thai = 36,
        Turkish = 37,
        Ukrainian = 38,
        Vietnamese = 39,
        ChineseSimplified = 40,
        ChineseTraditional = 41,
        Hindi = 42,
        Unknown = 43,
        Hungarian = 18,    }

    /// <summary>Screen orientation of a handheld device (design §3.2).</summary>
    /// <remarks>
    /// Design §3.2 lists <c>Portrait</c>..<c>AutoRotation</c>; Unity additionally declares the obsolete
    /// <c>Unknown = 0</c> and <c>Landscape = 3</c> (an alias of <c>LandscapeLeft</c>), which are omitted here per the
    /// design's omission rule (§3) so that a later reference to them surfaces as a compile error rather than a
    /// surprise. Note the consequence: <c>default(ScreenOrientation)</c> is 0 and therefore has no name.
    /// </remarks>
    public enum ScreenOrientation
    {
        Portrait = 1,
        PortraitUpsideDown = 2,
        LandscapeLeft = 3,
        LandscapeRight = 4,
        AutoRotation = 5,
    }

    /// <summary>
    /// When a <c>[RuntimeInitializeOnLoadMethod]</c> entry point runs (design §3.2).
    /// </summary>
    /// <remarks>
    /// The numbering is not the execution order: the actual order is <c>SubsystemRegistration</c> (4) →
    /// <c>AfterAssembliesLoaded</c> (2) → <c>BeforeSplashScreen</c> (3) → <c>BeforeSceneLoad</c> (1) →
    /// <c>AfterSceneLoad</c> (0), because <c>AfterSceneLoad</c> was the original single value and the earlier
    /// phases were appended later. The standalone host drives these explicitly, so the values matter only for
    /// round-tripping the attribute.
    /// </remarks>
    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0,
        BeforeSceneLoad = 1,
        AfterAssembliesLoaded = 2,
        BeforeSplashScreen = 3,
        SubsystemRegistration = 4,
    }

    /// <summary>
    /// Which soft keyboard layout <c>TouchScreenKeyboard.Open</c> requests (GC §9, design §3.2).
    /// </summary>
    /// <remarks>
    /// <c>NintendoNetworkAccount</c> is <c>[Obsolete]</c> in Unity but keeps slot 8, so the members after it are not
    /// renumbered. NowTextField / NowTextArea only ever pass <see cref="Default"/> and only behind an
    /// <c>isSupported</c> guard (GC §9).
    /// </remarks>
    public enum TouchScreenKeyboardType
    {
        Default = 0,
        ASCIICapable = 1,
        NumbersAndPunctuation = 2,
        URL = 3,
        NumberPad = 4,
        PhonePad = 5,
        NamePhonePad = 6,
        EmailAddress = 7,
        NintendoNetworkAccount = 8,
        Social = 9,
        Search = 10,
        DecimalPad = 11,
        OneTimeCode = 12,
    }
}
