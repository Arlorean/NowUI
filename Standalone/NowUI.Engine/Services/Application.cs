// Mirrors UnityEngine.Application.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 member list, §6.6 quitting and shutdown).
// Behaviour spec: Docs/Standalone/UnityDependencyInventory.md (§A.3 "UnityEngine.Application", §C.9 isPlaying).
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// Process-level facts about the running application. Everything that a host can differ on comes from
    /// <c>NowRuntime.host</c>; what only the runtime knows comes from <c>NowRuntime</c> itself.
    /// </summary>
    public static class Application
    {
        private static Func<bool> s_WantsToQuit;
        private static Action s_Quitting;
        private static string s_Version;
        private static bool s_HasSystemLanguage;
        private static SystemLanguage s_SystemLanguage;
        private static int s_TargetFrameRate = -1;

        static Application()
        {
            // NowRuntime raises its internal quit event first and knows nothing about this class; forwarding here in
            // the static constructor is what connects the two (design §6.6). It runs the first time anything touches
            // Application - and subscribing to Application.quitting is itself such a touch, so a subscriber can never
            // be registered before this hook exists.
            NowRuntime.onEngineQuitting += RaiseQuitting;
        }

        /// <summary>
        /// Whether the runtime behaves like a player. The core selects <c>Destroy</c> versus <c>DestroyImmediate</c>
        /// on this (§C.9) and gates its edit-mode paths on it; the standalone tests host sets it false.
        /// </summary>
        public static bool isPlaying => NowRuntime.isPlaying;

        /// <summary>
        /// The platform the host reports: an OS-mapped desktop <i>player</i> value by default (design §3.2), never an
        /// editor value. A browser host sets <c>WebGLPlayer</c> through its own host services.
        /// </summary>
        public static RuntimePlatform platform => NowRuntime.host.platform;

        /// <summary>A writable per-user directory; the temp directory's <c>NowUI</c> folder by default.</summary>
        public static string persistentDataPath => NowRuntime.host.persistentDataPath;

        /// <summary>The directory the application was loaded from.</summary>
        public static string dataPath => NowRuntime.host.dataPath;

        /// <summary>
        /// Unity's <c>StreamingAssets</c> folder beside <see cref="dataPath"/>. Composed rather than asked of the
        /// host, because Unity composes it the same way and the standalone build ships no such folder.
        /// </summary>
        public static string streamingAssetsPath => Path.Combine(NowRuntime.host.dataPath, "StreamingAssets");

        /// <summary>
        /// Always false. There is no editor in the standalone build; core code that branches on this must take its
        /// player path, which is exactly what the Unity build does in a player.
        /// </summary>
        public static bool isEditor => false;

        /// <summary>
        /// Always false. A batch-mode Unity player runs with no graphics device and no frame loop; a standalone host
        /// that has driven a NowUI frame is by definition not in that state.
        /// </summary>
        public static bool isBatchMode => false;

        /// <summary>
        /// Whether the reported platform is a handheld one. Derived from <see cref="platform"/> rather than stored, so
        /// a host that switches platform after startup stays consistent.
        /// </summary>
        public static bool isMobilePlatform
        {
            get
            {
                switch (NowRuntime.host.platform)
                {
                    case RuntimePlatform.IPhonePlayer:
                    case RuntimePlatform.Android:
                    case RuntimePlatform.tvOS:
                    case RuntimePlatform.VisionOS:
                    case RuntimePlatform.WSAPlayerX86:
                    case RuntimePlatform.WSAPlayerX64:
                    case RuntimePlatform.WSAPlayerARM:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// The frame rate the host is asked to target; -1 (Unity's default) means "as fast as the platform allows".
        /// The shim does not drive a frame loop, so this is a value a host reads, not one the shim acts on.
        /// </summary>
        public static int targetFrameRate
        {
            get => s_TargetFrameRate;
            set => s_TargetFrameRate = value;
        }

        /// <summary>
        /// A fixed sentinel: there is no Unity here, and a plausible-looking version number would be a lie that
        /// version-gating code would act on (design §3.7).
        /// </summary>
        public static string unityVersion => "0.0.0-nowui-standalone";

        /// <summary>
        /// The application's own version, taken from the entry assembly's informational version and cached. Falls back
        /// to "0.0.0" where there is no entry assembly at all (a native host, or a wasm runtime that does not expose
        /// one), because Unity's <c>Application.version</c> is never null.
        /// </summary>
        public static string version
        {
            get
            {
                string cached = s_Version;
                if (cached != null)
                    return cached;

                cached = ReadEntryAssemblyVersion();
                s_Version = cached;
                return cached;
            }
        }

        /// <summary>
        /// The OS language, mapped from the current culture once and cached, as Unity samples it once at startup.
        /// <c>Unknown</c> for anything the enum does not name.
        /// </summary>
        public static SystemLanguage systemLanguage
        {
            get
            {
                if (s_HasSystemLanguage)
                    return s_SystemLanguage;

                s_SystemLanguage = MapSystemLanguage(CultureInfo.CurrentCulture);
                s_HasSystemLanguage = true;
                return s_SystemLanguage;
            }
        }

        /// <summary>
        /// Raised as the runtime shuts down, before anything is destroyed (design §6.6) - where <c>NowFilePicker</c>
        /// releases its thumbnails. Raised by <c>NowRuntime.Shutdown()</c>, never by a frame.
        /// </summary>
        public static event Action quitting
        {
            add => s_Quitting += value;
            remove => s_Quitting -= value;
        }

        /// <summary>
        /// Consulted by <see cref="Quit"/>: any handler returning false cancels the quit, exactly as in Unity. All
        /// handlers run even after one has refused, because Unity's does too and handlers use it to record state.
        /// </summary>
        public static event Func<bool> wantsToQuit
        {
            add => s_WantsToQuit += value;
            remove => s_WantsToQuit -= value;
        }

        /// <summary>
        /// Asks the runtime to shut down: polls <see cref="wantsToQuit"/> and, if nothing refuses, calls
        /// <c>NowRuntime.Shutdown()</c> - which raises <see cref="quitting"/> and tears the runtime down (§6.6).
        /// Unlike Unity's, this returns only after the shutdown has completed; there is no player loop to defer to.
        /// </summary>
        public static void Quit()
        {
            if (!PollWantsToQuit())
                return;

            NowRuntime.Shutdown();
        }

        private static bool PollWantsToQuit()
        {
            Func<bool> handlers = s_WantsToQuit;
            if (handlers == null)
                return true;

            bool allow = true;
            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                // Every handler is invoked even once one has refused: Unity does the same, and a handler that is
                // skipped cannot do the bookkeeping it registered for.
                if (!((Func<bool>)list[i])())
                    allow = false;
            }

            return allow;
        }

        private static void RaiseQuitting()
        {
            Action quit = s_Quitting;
            if (quit != null)
                quit();
        }

        private static string ReadEntryAssemblyVersion()
        {
            Assembly entry = Assembly.GetEntryAssembly();
            if (entry == null)
                return "0.0.0";

            AssemblyInformationalVersionAttribute informational =
                entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informational != null && !string.IsNullOrEmpty(informational.InformationalVersion))
                return informational.InformationalVersion;

            AssemblyName name = entry.GetName();
            return name.Version == null ? "0.0.0" : name.Version.ToString();
        }

        /// <summary>
        /// Maps a culture onto Unity's <see cref="SystemLanguage"/>. Chinese is special-cased before the plain
        /// two-letter map because Unity distinguishes simplified from traditional and the ISO code does not.
        /// </summary>
        private static SystemLanguage MapSystemLanguage(CultureInfo culture)
        {
            if (culture == null)
                return SystemLanguage.Unknown;

            string name = culture.Name;
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                if (name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("TW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("HK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("MO", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SystemLanguage.ChineseTraditional;
                }

                return name.Length <= 2 ? SystemLanguage.Chinese : SystemLanguage.ChineseSimplified;
            }

            switch (culture.TwoLetterISOLanguageName)
            {
                case "af": return SystemLanguage.Afrikaans;
                case "ar": return SystemLanguage.Arabic;
                case "eu": return SystemLanguage.Basque;
                case "be": return SystemLanguage.Belarusian;
                case "bg": return SystemLanguage.Bulgarian;
                case "ca": return SystemLanguage.Catalan;
                case "cs": return SystemLanguage.Czech;
                case "da": return SystemLanguage.Danish;
                case "nl": return SystemLanguage.Dutch;
                case "en": return SystemLanguage.English;
                case "et": return SystemLanguage.Estonian;
                case "fo": return SystemLanguage.Faroese;
                case "fi": return SystemLanguage.Finnish;
                case "fr": return SystemLanguage.French;
                case "de": return SystemLanguage.German;
                case "el": return SystemLanguage.Greek;
                case "he": return SystemLanguage.Hebrew;
                case "hu": return SystemLanguage.Hungarian;
                case "is": return SystemLanguage.Icelandic;
                case "id": return SystemLanguage.Indonesian;
                case "it": return SystemLanguage.Italian;
                case "ja": return SystemLanguage.Japanese;
                case "ko": return SystemLanguage.Korean;
                case "lv": return SystemLanguage.Latvian;
                case "lt": return SystemLanguage.Lithuanian;
                case "nb":
                case "nn":
                case "no": return SystemLanguage.Norwegian;
                case "pl": return SystemLanguage.Polish;
                case "pt": return SystemLanguage.Portuguese;
                case "ro": return SystemLanguage.Romanian;
                case "ru": return SystemLanguage.Russian;
                case "sr":
                case "hr":
                case "bs": return SystemLanguage.SerboCroatian;
                case "sk": return SystemLanguage.Slovak;
                case "sl": return SystemLanguage.Slovenian;
                case "es": return SystemLanguage.Spanish;
                case "sv": return SystemLanguage.Swedish;
                case "th": return SystemLanguage.Thai;
                case "tr": return SystemLanguage.Turkish;
                case "uk": return SystemLanguage.Ukrainian;
                case "vi": return SystemLanguage.Vietnamese;
                case "hi": return SystemLanguage.Hindi;
                default: return SystemLanguage.Unknown;
            }
        }
    }
}
