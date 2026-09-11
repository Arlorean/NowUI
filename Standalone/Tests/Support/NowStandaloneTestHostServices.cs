// The INowHostServices the test run installs.
//
// It is DefaultHostServices with three substitutions and nothing else, because every difference from the default host
// is a difference the tests can see:
//
//   logger     -> NowRecordingLogger, so LogAssert can match and the log policy can fail a test (test plan 4.4).
//   resources  -> NowStandaloneTestResources, so Resources.Load returns the material templates instead of null and
//                 LoadRequiredResource does not log an error that would fail the first drawing test (R1, R2).
//   clipboard  -> an in-memory clipboard, so the NowClipboard default delegates have somewhere to go. The tests that
//                 care (NowTextAreaTests.CopyAndCutKeepNewlines) swap the delegates themselves; this only keeps the
//                 default path from being a null-clipboard no-op that a later test might notice.
//
// Everything else - the Stopwatch clock, 1920x1080 at 96 dpi, the OS-mapped desktop RuntimePlatform, the temp
// persistentDataPath, Unity's default 32-entry layer table - is delegated to DefaultHostServices verbatim.
//
// New file of ours. Design: Docs/Standalone/StandaloneCoreDesign.md sections 4.4 and 7.2.
using System;
using NowUI.Engine;

namespace NowUI.Standalone.Tests
{
    /// <summary>Host services for the engine-free run of NowUI's own Unity test suite.</summary>
    public sealed class NowStandaloneTestHostServices : INowHostServices
    {
        private readonly DefaultHostServices m_Defaults = new DefaultHostServices();
        private readonly INowLogger m_Logger;
        private readonly INowResourceProvider m_Resources;
        private readonly INowClipboard m_Clipboard = new NowMemoryClipboard();

        public NowStandaloneTestHostServices(INowLogger logger, INowResourceProvider resources)
        {
            m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        public INowClock clock => m_Defaults.clock;

        public NowScreenInfo screen => m_Defaults.screen;

        public INowLogger logger => m_Logger;

        public INowClipboard clipboard => m_Clipboard;

        /// <summary>
        /// Null: <c>TouchScreenKeyboard.isSupported</c> is false, which is what the desktop editor reports and what
        /// every text test in the subset assumes.
        /// </summary>
        public INowTouchKeyboard touchKeyboard => null;

        public INowResourceProvider resources => m_Resources;

        /// <summary>Null: no test in the M1 subset decodes an image, and a decoder would only add a way to differ.</summary>
        public INowImageDecoder imageDecoder => null;

        /// <summary>Null: remote content fails fast rather than reaching the network from a test run.</summary>
        public INowFetchProvider fetch => null;

        public UnityEngine.RuntimePlatform platform => m_Defaults.platform;

        public string persistentDataPath => m_Defaults.persistentDataPath;

        public string dataPath => m_Defaults.dataPath;

        /// <summary>
        /// Unity's default layer table (Default / TransparentFX / Ignore Raycast / "" / Water / UI, then 26 empty
        /// slots). The test plan (section 6 item 5) suggests 32 empty strings; the editor the oracle run was recorded
        /// in has the default table, so this matches the oracle rather than the document. NowMaskField skips empty
        /// names, so this is the closer of the two to what NowInspectorTests saw in Unity.
        /// </summary>
        public string[] layerNames => m_Defaults.layerNames;
    }
}
