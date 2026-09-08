// The headless NowUI host these tests run inside.
//
// It is Standalone/Tests/Support/NowStandaloneTestHost.cs with the parts that belong to NowUI's own Unity suite
// removed: no LogAssert shim, no per-test Unity log policy, no PerformanceTesting stub. What remains is the three
// facts every test in this assembly depends on - a resource provider that can serve NotoSans and the material
// templates, a null render backend, and one Time.frameCount advance per test so that NowUI's frame-keyed
// registries roll over between tests the way they do between editor updates.
//
// The frame advance matters more here than it looks. NowControls.EnterIdScope stamps _idScopeStartedAt with
// Time.frameCount, NowLayout keys its whole cache on the frame, and NowFocus arbitrates one frame late; a suite
// that never advanced the frame would let one test's identity scopes look "still open" to the next.

using System;
using NowUI;
using NowUI.Engine;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEngine;

[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]
[assembly: NowUI.Bridge.Tests.BridgeFrame]

namespace NowUI.Bridge.Tests
{
    /// <summary>Brackets the whole assembly. Namespace-less fixtures are not used here, but NUnit still applies it.</summary>
    [SetUpFixture]
    public sealed class BridgeTestHost
    {
        private static NowRecordingRenderBackend s_Backend;

        /// <summary>
        /// The op log W4's acceptance diffs. Wrapped around the same <see cref="NullRenderBackend"/> the suite
        /// always used, so nothing about the frames these tests run has changed - only that they are now written
        /// down.
        /// </summary>
        public static NowRecordingRenderBackend backend => s_Backend;

        /// <summary>
        /// Whether the real material and font fixtures were found. False turns W4's draw-list diff into a
        /// comparison of two empty logs, which is why every test that depends on it says so out loud rather than
        /// passing quietly.
        /// </summary>
        public static bool hasResources { get; private set; }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            NowRuntime.isPlaying = false;
            NowRuntime.colorSpace = ColorSpace.Gamma;

            s_Backend = new NowRecordingRenderBackend();
            NowRuntime.Initialize(new BridgeHostServices(), s_Backend);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            NowRuntime.Shutdown();
        }

        internal static void BeforeTest()
        {
            NowRuntime.BeginFrame();
            if (s_Backend != null) s_Backend.Clear();
        }

        /// <summary>
        /// DefaultHostServices with a logger that writes to the test output, and the real material/font fixtures
        /// when they are on disk.
        /// </summary>
        /// <remarks>
        /// <para>W2 and W3 ran font-free, and that was right for them: NowUI's identity resolution runs before
        /// anything is measured or drawn, so a font-free host made that independence a measured property rather
        /// than a claim.</para>
        /// <para>W4 cannot. Its acceptance is that <c>exactLayout</c> on and off produce the same DRAW LIST
        /// against <c>NowRecordingRenderBackend</c>, and a font-free host produces no draw list at all - measured,
        /// not assumed: a frame drawing a label, a button and a text field logs exactly two ops,
        /// <c>BeginFrame(1)</c> and <c>EndFrame()</c>, because <c>Resources/NowUI/UIMaterial</c> comes back null
        /// and every mesh path is skipped. Two empty logs comparing equal is not evidence of anything.</para>
        /// <para>So the fixtures exported for <c>Standalone/Tests</c> are reused. They are reached through that
        /// project's public <see cref="NowStandaloneTestResources"/> because the shim's <c>Shader</c> constructor
        /// is internal to <c>NowUI.Engine</c> and visible only to the assembly literally named <c>Tests</c>
        /// (<c>Graphics/Shader.cs:38</c>) - so the provider cannot be re-implemented here, and widening that grant
        /// would mean editing <c>NowUI.Engine</c>, which this milestone does not touch.</para>
        /// <para>When the fixtures are absent the host falls back to serving nothing and
        /// <see cref="hasResources"/> is false, so the suite still runs everywhere - it just says which
        /// assertions went vacuous.</para>
        /// </remarks>
        private sealed class BridgeHostServices : INowHostServices
        {
            private readonly DefaultHostServices m_Defaults = new DefaultHostServices();
            private readonly INowLogger m_Logger = new TestOutputLogger();
            private readonly INowResourceProvider m_Resources = ResolveResources();

            public INowClock clock => m_Defaults.clock;
            public NowScreenInfo screen => m_Defaults.screen;
            public INowLogger logger => m_Logger;
            public INowClipboard clipboard => m_Defaults.clipboard;
            public INowTouchKeyboard touchKeyboard => null;
            public INowResourceProvider resources => m_Resources;
            public INowImageDecoder imageDecoder => null;
            public INowFetchProvider fetch => null;
            public RuntimePlatform platform => m_Defaults.platform;
            public string persistentDataPath => m_Defaults.persistentDataPath;
            public string dataPath => m_Defaults.dataPath;
            public string[] layerNames => m_Defaults.layerNames;

            private static INowResourceProvider ResolveResources()
            {
                string root = FindFixtureRoot();

                if (root != null)
                {
                    try
                    {
                        var resources = new NowUI.Standalone.Tests.NowStandaloneTestResources(root);
                        hasResources = true;
                        return resources;
                    }
                    catch (Exception e)
                    {
                        // A malformed or partial fixture set. Reported, not swallowed, and not fatal: the
                        // identity and protocol assertions do not need it.
                        Console.WriteLine("[NowUI.Bridge.Tests] the fixtures at " + root +
                                          " did not load, so the draw-list assertions will be vacuous: " + e.Message);
                    }
                }

                hasResources = false;
                return new EmptyResources();
            }

            /// <summary>
            /// Walks up from the test assembly to <c>Standalone/Tests/Fixtures</c>, the way Node.cs finds
            /// run.mjs. Null when it is not there, which is what turns <see cref="hasResources"/> off.
            /// </summary>
            private static string FindFixtureRoot()
            {
                for (var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                {
                    string candidate = System.IO.Path.Combine(dir.FullName, "Standalone", "Tests", "Fixtures");
                    if (System.IO.Directory.Exists(candidate)) return candidate;

                    string sibling = System.IO.Path.Combine(dir.FullName, "Tests", "Fixtures");
                    if (System.IO.Directory.Exists(sibling)) return sibling;
                }

                return null;
            }
        }

        /// <summary>
        /// A provider that serves nothing. It exists rather than a null because <c>Resources.Load</c> dereferences
        /// the provider without a null check (NowUI.Engine/Services/Resources.cs:25), and a NullReferenceException
        /// out of <c>Now.StartUI</c> would say nothing about the bridge. Serving nothing is fine:
        /// <c>Now.LoadRequiredResource</c> logs one error per missing path and returns null, and every path in
        /// NowUI that this assembly reaches is written to survive that.
        /// </summary>
        private sealed class EmptyResources : INowResourceProvider
        {
            public UnityEngine.Object Load(string path, Type type) => null;

            public Shader FindShader(string name) => null;
        }

        private sealed class TestOutputLogger : INowLogger
        {
            public void Log(LogType type, string message, Exception exception, UnityEngine.Object context)
            {
                TestContext.WriteLine("[NowUI " + type + "] " + message + (exception != null ? "\n" + exception : ""));
            }
        }
    }

    /// <summary>One Time.frameCount advance per test. Applied at the assembly level, above.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class BridgeFrameAttribute : Attribute, ITestAction
    {
        public ActionTargets Targets => ActionTargets.Test;

        public void BeforeTest(ITest test)
        {
            BridgeTestHost.BeforeTest();
        }

        public void AfterTest(ITest test)
        {
        }
    }
}
