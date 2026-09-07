// Unit U5 (services) semantics suite: UnityEngine.{Time, Screen, Application, Debug, SystemInfo, QualitySettings,
// Resources, TouchScreenKeyboard, ExpressionEvaluator, ColorUtility}.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.7, §4.4, §6.1, §6.6, §8 (U5).
// Behaviour spec: Docs/Standalone/GradientCurveSemantics.md §6 (ColorUtility), §8 (ExpressionEvaluator), §9
// (TouchScreenKeyboard); Docs/Standalone/UnityDependencyInventory.md §A.3.
using System;
using System.Collections.Generic;
using System.Globalization;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests.Services
{
    // ---------------------------------------------------------------------------------------------------------
    // Test doubles
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>A clock the test moves by hand, so "live" time can be observed without sleeping.</summary>
    internal sealed class FakeClock : INowClock
    {
        public double seconds;

        public double realtimeSeconds => seconds;
    }

    /// <summary>One captured log call.</summary>
    internal readonly struct LogRecord
    {
        public readonly LogType type;
        public readonly string message;
        public readonly Exception exception;
        public readonly UnityEngine.Object context;

        public LogRecord(LogType type, string message, Exception exception, UnityEngine.Object context)
        {
            this.type = type;
            this.message = message;
            this.exception = exception;
            this.context = context;
        }
    }

    internal sealed class RecordingLogger : INowLogger
    {
        public readonly List<LogRecord> records = new List<LogRecord>();

        public void Log(LogType type, string message, Exception exception, UnityEngine.Object context)
        {
            records.Add(new LogRecord(type, message, exception, context));
        }
    }

    /// <summary>A provider that answers exactly one path, so a wrong-path or wrong-type lookup is visibly null.</summary>
    internal sealed class SinglePathResourceProvider : INowResourceProvider
    {
        public string path;
        public UnityEngine.Object asset;
        public int loadCalls;

        public UnityEngine.Object Load(string path, Type type)
        {
            loadCalls++;
            return path == this.path ? asset : null;
        }

        public UnityEngine.Shader FindShader(string name) => null;
    }

    internal sealed class FakeKeyboardSession : INowTouchKeyboardSession
    {
        public TouchScreenKeyboard.Status statusValue = TouchScreenKeyboard.Status.Visible;
        public string textValue = "";
        public bool activeValue = true;

        public TouchScreenKeyboard.Status status => statusValue;

        public string text
        {
            get => textValue;
            set => textValue = value;
        }

        public bool active
        {
            get => activeValue;
            set => activeValue = value;
        }
    }

    internal sealed class FakeTouchKeyboard : INowTouchKeyboard
    {
        public readonly List<FakeKeyboardSession> opened = new List<FakeKeyboardSession>();
        public string lastText;
        public TouchScreenKeyboardType lastType;
        public bool lastAutocorrection;
        public bool lastMultiline;
        public bool lastSecure;

        public INowTouchKeyboardSession Open(
            string text, TouchScreenKeyboardType type, bool autocorrection, bool multiline, bool secure)
        {
            lastText = text;
            lastType = type;
            lastAutocorrection = autocorrection;
            lastMultiline = multiline;
            lastSecure = secure;

            FakeKeyboardSession session = new FakeKeyboardSession { textValue = text ?? "" };
            opened.Add(session);
            return session;
        }
    }

    /// <summary>A host whose every service is a public field, so a fixture can vary one at a time.</summary>
    internal sealed class TestHost : INowHostServices
    {
        public FakeClock testClock = new FakeClock();
        public NowScreenInfo screenInfo = new NowScreenInfo(1920, 1080, 96f);
        public INowLogger testLogger = new RecordingLogger();
        public INowClipboard testClipboard;
        public INowTouchKeyboard testTouchKeyboard;
        public INowResourceProvider testResources = NowEmptyResourceProvider.instance;
        public INowImageDecoder testImageDecoder;
        public INowFetchProvider testFetch;
        public RuntimePlatform testPlatform = RuntimePlatform.LinuxPlayer;
        public string testPersistentDataPath = "/tmp/nowui-test";
        public string testDataPath = "/tmp/nowui-test-data";
        public string[] testLayerNames = new string[32];

        public INowClock clock => testClock;
        public NowScreenInfo screen => screenInfo;
        public INowLogger logger => testLogger;
        public INowClipboard clipboard => testClipboard;
        public INowTouchKeyboard touchKeyboard => testTouchKeyboard;
        public INowResourceProvider resources => testResources;
        public INowImageDecoder imageDecoder => testImageDecoder;
        public INowFetchProvider fetch => testFetch;
        public RuntimePlatform platform => testPlatform;
        public string persistentDataPath => testPersistentDataPath;
        public string dataPath => testDataPath;
        public string[] layerNames => testLayerNames;
    }

    /// <summary>An asset a resource provider can hand back.</summary>
    internal sealed class FakeAsset : UnityEngine.Object
    {
    }

    /// <summary>A different asset type, so a type-mismatched <c>Load&lt;T&gt;</c> has something to fail against.</summary>
    internal sealed class OtherFakeAsset : UnityEngine.Object
    {
    }

    /// <summary>A message whose <c>ToString</c> throws, proving a discarded log never formats its argument.</summary>
    internal sealed class ExplodingMessage
    {
        public override string ToString()
        {
            throw new InvalidOperationException("ToString must not be called when nothing consumes the log.");
        }
    }

    /// <summary>Installs a <see cref="TestHost"/> for the duration of a fixture and restores the defaults after.</summary>
    public abstract class ServiceFixture
    {
        internal TestHost host;
        internal RecordingLogger logger;

        [SetUp]
        public void InstallTestHost()
        {
            host = new TestHost();
            logger = new RecordingLogger();
            host.testLogger = logger;
            NowRuntime.Initialize(host, null);
        }

        [TearDown]
        public void RestoreDefaults()
        {
            // Passing null restores a DefaultHostServices and a NullRenderBackend, so a fixture in another unit that
            // runs after this one sees the same process state it would have seen alone.
            NowRuntime.Initialize(null, null);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // ColorUtility - GradientCurveSemantics.md §6
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class ColorUtilityTests
    {
        /// <summary>The full 23-name table of GC §6.1 step 3, as (name, r, g, b, a).</summary>
        private static readonly object[] s_NamedColorCases =
        {
            new object[] { "red", 0xFF, 0x00, 0x00, 0xFF },
            new object[] { "cyan", 0x00, 0xFF, 0xFF, 0xFF },
            new object[] { "blue", 0x00, 0x00, 0xFF, 0xFF },
            new object[] { "darkblue", 0x00, 0x00, 0x8B, 0xFF },
            new object[] { "lightblue", 0xAD, 0xD8, 0xE6, 0xFF },
            new object[] { "purple", 0x80, 0x00, 0x80, 0xFF },
            new object[] { "yellow", 0xFF, 0xFF, 0x00, 0xFF },
            new object[] { "lime", 0x00, 0xFF, 0x00, 0xFF },
            new object[] { "fuchsia", 0xFF, 0x00, 0xFF, 0xFF },
            new object[] { "white", 0xFF, 0xFF, 0xFF, 0xFF },
            new object[] { "silver", 0xC0, 0xC0, 0xC0, 0xFF },
            new object[] { "grey", 0x80, 0x80, 0x80, 0xFF },
            new object[] { "black", 0x00, 0x00, 0x00, 0xFF },
            new object[] { "orange", 0xFF, 0xA5, 0x00, 0xFF },
            new object[] { "brown", 0xA5, 0x2A, 0x2A, 0xFF },
            new object[] { "maroon", 0x80, 0x00, 0x00, 0xFF },
            new object[] { "green", 0x00, 0x80, 0x00, 0xFF },
            new object[] { "olive", 0x80, 0x80, 0x00, 0xFF },
            new object[] { "navy", 0x00, 0x00, 0x80, 0xFF },
            new object[] { "teal", 0x00, 0x80, 0x80, 0xFF },
            new object[] { "aqua", 0x00, 0xFF, 0xFF, 0xFF },
            new object[] { "magenta", 0xFF, 0x00, 0xFF, 0xFF },
            new object[] { "transparent", 0x00, 0x00, 0x00, 0x00 },
        };

        private static void AssertChannels(Color color, int r, int g, int b, int a)
        {
            Assert.Multiple(() =>
            {
                Assert.That(color.r, Is.EqualTo(r / 255f), "r");
                Assert.That(color.g, Is.EqualTo(g / 255f), "g");
                Assert.That(color.b, Is.EqualTo(b / 255f), "b");
                Assert.That(color.a, Is.EqualTo(a / 255f), "a");
            });
        }

        [TestCaseSource(nameof(s_NamedColorCases))]
        public void NamedColorParses(string name, int r, int g, int b, int a)
        {
            Assert.That(ColorUtility.TryParseHtmlString(name, out Color color), Is.True);
            AssertChannels(color, r, g, b, a);
        }

        [TestCase("RED", 0xFF, 0x00, 0x00)]
        [TestCase("Red", 0xFF, 0x00, 0x00)]
        [TestCase("DarkBlue", 0x00, 0x00, 0x8B)]
        [TestCase("DARKBLUE", 0x00, 0x00, 0x8B)]
        [TestCase("LightBlue", 0xAD, 0xD8, 0xE6)]
        public void NameMatchingIsCaseInsensitive(string name, int r, int g, int b)
        {
            Assert.That(ColorUtility.TryParseHtmlString(name, out Color color), Is.True);
            AssertChannels(color, r, g, b, 0xFF);
        }

        [TestCase(" red ")]
        [TestCase("\tred\n")]
        [TestCase("  #ff0000  ")]
        [TestCase("\r\n#F00 ")]
        public void InputIsTrimmedBeforeParsing(string input)
        {
            Assert.That(ColorUtility.TryParseHtmlString(input, out Color color), Is.True);
            AssertChannels(color, 0xFF, 0x00, 0x00, 0xFF);
        }

        [TestCase("#f00", 0xFF, 0x00, 0x00, 0xFF)]
        [TestCase("#F00", 0xFF, 0x00, 0x00, 0xFF)]
        [TestCase("#abc", 0xAA, 0xBB, 0xCC, 0xFF)]
        [TestCase("#f008", 0xFF, 0x00, 0x00, 0x88)]
        [TestCase("#FF00", 0xFF, 0xFF, 0x00, 0x00)]
        [TestCase("#ff0000", 0xFF, 0x00, 0x00, 0xFF)]
        [TestCase("#00FF80", 0x00, 0xFF, 0x80, 0xFF)]
        [TestCase("#ff000080", 0xFF, 0x00, 0x00, 0x80)]
        [TestCase("#0A0b0C0d", 0x0A, 0x0B, 0x0C, 0x0D)]
        public void HexFormsParse(string input, int r, int g, int b, int a)
        {
            Assert.That(ColorUtility.TryParseHtmlString(input, out Color color), Is.True);
            AssertChannels(color, r, g, b, a);
        }

        /// <summary>
        /// GC §6.1 step 4: the byte-to-float conversion is <c>byte / 255f</c>, and 0x80 must land on the exact
        /// single-precision value 0x3F008081. A double-precision divide would produce a different bit pattern.
        /// </summary>
        [Test]
        public void ByteToFloatConversionIsBitExact()
        {
            Assert.That(ColorUtility.TryParseHtmlString("#808080", out Color color), Is.True);

            int bits = BitConverter.SingleToInt32Bits(color.r);
            Assert.That(bits, Is.EqualTo(unchecked((int)0x3F008081)), "0x80 / 255f");
            Assert.That(color.r, Is.EqualTo(color.g).And.EqualTo(color.b));
        }

        /// <summary>Every rejected form of GC §6.1, §10.3. All of them must also report white.</summary>
        [TestCase(null, TestName = "Fails_Null")]
        [TestCase("", TestName = "Fails_Empty")]
        [TestCase("   ", TestName = "Fails_Whitespace")]
        [TestCase("#", TestName = "Fails_HashOnly")]
        [TestCase("#f", TestName = "Fails_OneDigit")]
        [TestCase("#ff", TestName = "Fails_TwoDigits")]
        [TestCase("#FF000", TestName = "Fails_FiveDigits")]
        [TestCase("#FF00000", TestName = "Fails_SevenDigits")]
        [TestCase("#FF0000FFF", TestName = "Fails_NineDigits")]
        [TestCase("##ff0000", TestName = "Fails_SecondHash")]
        [TestCase("#ff00zz", TestName = "Fails_NonHexCharacters")]
        [TestCase("#ff0000 ff", TestName = "Fails_EmbeddedSpace")]
        [TestCase("#ＦＦ0000", TestName = "Fails_FullWidthDigits")]
        [TestCase("#0xff0000", TestName = "Fails_HexPrefix")]
        [TestCase("ff0000", TestName = "Fails_BareHexSix")]
        [TestCase("f00", TestName = "Fails_BareHexThree")]
        [TestCase("gray", TestName = "Fails_Gray")]
        [TestCase("pink", TestName = "Fails_Pink")]
        [TestCase("clear", TestName = "Fails_Clear")]
        [TestCase("light blue", TestName = "Fails_LightBlueWithSpace")]
        [TestCase("rgb(255,0,0)", TestName = "Fails_RgbFunction")]
        public void RejectedInputsFailAndReportWhite(string input)
        {
            // Seeded with a value that is neither white nor default, so the out parameter must actually be written.
            Color color = new Color(0.25f, 0.5f, 0.75f, 0.125f);

            Assert.That(ColorUtility.TryParseHtmlString(input, out color), Is.False);
            AssertChannels(color, 0xFF, 0xFF, 0xFF, 0xFF);
        }

        [Test]
        public void ToHtmlStringMatchesVerifiedValues()
        {
            Color a = new Color(1f, 0.5f, 0.25f, 0.75f);
            Assert.Multiple(() =>
            {
                Assert.That(ColorUtility.ToHtmlStringRGB(a), Is.EqualTo("FF8040"));
                Assert.That(ColorUtility.ToHtmlStringRGBA(a), Is.EqualTo("FF8040BF"));
                Assert.That(
                    ColorUtility.ToHtmlStringRGBA(new Color(0.1f, 0.2f, 0.3f, 0.4f)),
                    Is.EqualTo("1A334C66"));
            });
        }

        /// <summary>
        /// GC §6.2's rounding table. 0.5 → 127.5 → 128 is the banker's-rounding case, and 0.3 → "4C" (not "4D") is
        /// the one that only comes out right if the multiply stays in single precision.
        /// </summary>
        [TestCase(0.5f, "80")]
        [TestCase(0.49999f, "7F")]
        [TestCase(0.0019f, "00")]
        [TestCase(0.00196f, "00")]
        [TestCase(0.002f, "01")]
        [TestCase(0.3f, "4C")]
        [TestCase(0f, "00")]
        [TestCase(1f, "FF")]
        public void ChannelRounding(float channel, string expected)
        {
            string hex = ColorUtility.ToHtmlStringRGB(new Color(channel, 0f, 0f, 1f));
            Assert.That(hex.Substring(0, 2), Is.EqualTo(expected));
        }

        /// <summary>
        /// GC §6.2 / §10.2: Mono's out-of-range cast makes every non-finite channel "00", and .NET's saturating cast
        /// would make +INF "FF". Out-of-range finite values still clamp normally.
        /// </summary>
        [TestCase(float.NaN, "00")]
        [TestCase(float.PositiveInfinity, "00")]
        [TestCase(float.NegativeInfinity, "00")]
        [TestCase(2f, "FF")]
        [TestCase(-1f, "00")]
        public void NonFiniteAndOutOfRangeChannels(float channel, string expected)
        {
            string hex = ColorUtility.ToHtmlStringRGB(new Color(channel, 0f, 0f, 1f));
            Assert.That(hex.Substring(0, 2), Is.EqualTo(expected));
        }

        /// <summary>The type is a plain class with static members, not a static or sealed class (GC §6).</summary>
        [Test]
        public void TypeShapeMatchesUnity()
        {
            Type type = typeof(ColorUtility);
            Assert.Multiple(() =>
            {
                Assert.That(type.IsSealed, Is.False, "ColorUtility must not be sealed");
                Assert.That(type.IsAbstract, Is.False, "ColorUtility must not be static");
            });
        }

        /// <summary>Formatting must not follow the current culture: a log or a markup string is locale-independent.</summary>
        [Test]
        public void ToHtmlStringIsCultureIndependent()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(ColorUtility.ToHtmlStringRGBA(new Color(0.1f, 0.2f, 0.3f, 0.4f)), Is.EqualTo("1A334C66"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Debug - design §3.7
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class DebugServiceTests : ServiceFixture
    {
        /// <summary>The format design §3.7 pins: <c>NowTextPreprocessorTests</c> matches on it exactly.</summary>
        [Test]
        public void LogExceptionFormatsTypeColonMessage()
        {
            InvalidOperationException exception = new InvalidOperationException("boom");
            UnityEngine.Debug.LogException(exception);

            Assert.That(logger.records, Has.Count.EqualTo(1));
            LogRecord record = logger.records[0];
            Assert.Multiple(() =>
            {
                Assert.That(record.message, Is.EqualTo("InvalidOperationException: boom"));
                Assert.That(record.type, Is.EqualTo(LogType.Exception));
                Assert.That(record.exception, Is.SameAs(exception));
            });
        }

        /// <summary>The message is the exception's own, never <c>ToString()</c> with its stack trace appended.</summary>
        [Test]
        public void LogExceptionDoesNotIncludeStackTrace()
        {
            Exception thrown;
            try
            {
                throw new ArgumentException("bad argument");
            }
            catch (Exception e)
            {
                thrown = e;
            }

            UnityEngine.Debug.LogException(thrown);

            Assert.That(logger.records[0].message, Is.EqualTo("ArgumentException: bad argument"));
            Assert.That(logger.records[0].message, Does.Not.Contain("at "));
        }

        [Test]
        public void LogExceptionCarriesContext()
        {
            FakeAsset context = new FakeAsset();
            UnityEngine.Debug.LogException(new Exception("x"), context);

            Assert.That(logger.records[0].context, Is.SameAs(context));
        }

        [Test]
        public void SeveritiesMapToLogTypes()
        {
            UnityEngine.Debug.Log("a");
            UnityEngine.Debug.LogWarning("b");
            UnityEngine.Debug.LogError("c");
            UnityEngine.Debug.LogAssertion("d");

            Assert.That(
                new[] { logger.records[0].type, logger.records[1].type, logger.records[2].type, logger.records[3].type },
                Is.EqualTo(new[] { LogType.Log, LogType.Warning, LogType.Error, LogType.Assert }));
        }

        [Test]
        public void ContextOverloadsForwardTheContext()
        {
            FakeAsset context = new FakeAsset();
            UnityEngine.Debug.Log("a", context);
            UnityEngine.Debug.LogWarning("b", context);
            UnityEngine.Debug.LogError("c", context);
            UnityEngine.Debug.LogAssertion("d", context);

            foreach (LogRecord record in logger.records)
                Assert.That(record.context, Is.SameAs(context));
        }

        [Test]
        public void NullMessageLogsUnitysNullText()
        {
            UnityEngine.Debug.Log(null);
            Assert.That(logger.records[0].message, Is.EqualTo("Null"));
        }

        /// <summary>
        /// Design §3.7: nothing is formatted when no sink consumes the level. The message's <c>ToString</c> throws, so
        /// a shim that formatted eagerly would surface the exception here.
        /// </summary>
        [Test]
        public void NothingIsFormattedWhenTheHostHasNoLogger()
        {
            host.testLogger = null;

            Assert.DoesNotThrow(() => UnityEngine.Debug.Log(new ExplodingMessage()));
            Assert.DoesNotThrow(() => UnityEngine.Debug.LogWarning(new ExplodingMessage()));
            Assert.DoesNotThrow(() => UnityEngine.Debug.LogError(new ExplodingMessage()));
            Assert.DoesNotThrow(() => UnityEngine.Debug.LogAssertion(new ExplodingMessage()));
            Assert.DoesNotThrow(() => UnityEngine.Debug.LogFormat("{0}", new ExplodingMessage()));
            Assert.DoesNotThrow(() => UnityEngine.Debug.LogException(new Exception("x")));
            Assert.DoesNotThrow(() => UnityEngine.Debug.Assert(false, "message"));
        }

        [Test]
        public void FormatOverloadsUseTheInvariantCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                UnityEngine.Debug.LogFormat("value={0}", 1.5);
                UnityEngine.Debug.LogWarningFormat("value={0}", 1.5);
                UnityEngine.Debug.LogErrorFormat("value={0}", 1.5);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }

            Assert.Multiple(() =>
            {
                Assert.That(logger.records[0].message, Is.EqualTo("value=1.5"));
                Assert.That(logger.records[1].message, Is.EqualTo("value=1.5"));
                Assert.That(logger.records[2].message, Is.EqualTo("value=1.5"));
                Assert.That(logger.records[0].type, Is.EqualTo(LogType.Log));
                Assert.That(logger.records[1].type, Is.EqualTo(LogType.Warning));
                Assert.That(logger.records[2].type, Is.EqualTo(LogType.Error));
            });
        }

        /// <summary>A logging call must never be the thing that throws out of a frame.</summary>
        [Test]
        public void MalformedFormatStringDoesNotThrow()
        {
            Assert.DoesNotThrow(() => UnityEngine.Debug.LogFormat("{0} {1}", 1));
            Assert.That(logger.records[0].message, Is.EqualTo("{0} {1}"));
        }

        [Test]
        public void AssertLogsOnlyWhenTheConditionFails()
        {
            UnityEngine.Debug.Assert(true);
            UnityEngine.Debug.Assert(true, "not logged");
            Assert.That(logger.records, Is.Empty);

            UnityEngine.Debug.Assert(false);
            UnityEngine.Debug.Assert(false, "explained");

            Assert.Multiple(() =>
            {
                Assert.That(logger.records, Has.Count.EqualTo(2));
                Assert.That(logger.records[0].type, Is.EqualTo(LogType.Assert));
                Assert.That(logger.records[0].message, Is.EqualTo("Assertion failed"));
                Assert.That(logger.records[1].message, Is.EqualTo("explained"));
            });
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Time - design §3.7, §6.1
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class TimeServiceTests : ServiceFixture
    {
        /// <summary>Hazard H.9: exactly one increment per host frame, and no other writer anywhere.</summary>
        [Test]
        public void FrameCountAdvancesExactlyOncePerBeginFrame()
        {
            int start = UnityEngine.Time.frameCount;

            NowRuntime.BeginFrame();
            Assert.That(UnityEngine.Time.frameCount, Is.EqualTo(start + 1));

            // Reading it repeatedly, and ending the frame, must not move it.
            Assert.That(UnityEngine.Time.frameCount, Is.EqualTo(start + 1));
            NowRuntime.EndFrame();
            Assert.That(UnityEngine.Time.frameCount, Is.EqualTo(start + 1));

            NowRuntime.BeginFrame();
            NowRuntime.EndFrame();
            Assert.That(UnityEngine.Time.frameCount, Is.EqualTo(start + 2));
        }

        /// <summary>
        /// Design §6.1: <c>realtimeSinceStartup</c> reads the clock at call time. Six suite tests sleep inside one
        /// frame and compare, so a frame-latched value would report zero elapsed time and fail them.
        /// </summary>
        [Test]
        public void RealtimeSinceStartupIsLiveWithinAFrame()
        {
            host.testClock.seconds = 10d;
            NowRuntime.BeginFrame();

            Assert.That(UnityEngine.Time.realtimeSinceStartupAsDouble, Is.EqualTo(0d).Within(1e-12));

            host.testClock.seconds = 10.5d;

            Assert.Multiple(() =>
            {
                Assert.That(UnityEngine.Time.realtimeSinceStartupAsDouble, Is.EqualTo(0.5d).Within(1e-12));
                Assert.That(UnityEngine.Time.realtimeSinceStartup, Is.EqualTo(0.5f).Within(1e-6f));

                // ...while the frame-latched fields stay put until the next BeginFrame.
                Assert.That(UnityEngine.Time.timeAsDouble, Is.EqualTo(0d));
                Assert.That(UnityEngine.Time.time, Is.EqualTo(0f));
            });
        }

        [Test]
        public void TimeAndDeltaTimeAreLatchedPerFrame()
        {
            host.testClock.seconds = 100d;
            NowRuntime.BeginFrame();

            // First frame: no previous frame to measure against.
            Assert.That(UnityEngine.Time.deltaTime, Is.EqualTo(0f));

            host.testClock.seconds = 100.25d;
            NowRuntime.BeginFrame();

            Assert.Multiple(() =>
            {
                Assert.That(UnityEngine.Time.deltaTime, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(UnityEngine.Time.unscaledDeltaTime, Is.EqualTo(UnityEngine.Time.deltaTime));
                Assert.That(UnityEngine.Time.smoothDeltaTime, Is.EqualTo(UnityEngine.Time.deltaTime));
                Assert.That(UnityEngine.Time.timeAsDouble, Is.EqualTo(0.25d).Within(1e-12));
                Assert.That(UnityEngine.Time.unscaledTime, Is.EqualTo(UnityEngine.Time.time));
            });
        }

        /// <summary>
        /// The <c>SmoothDamp</c> overloads that omit a delta read <c>EngineClock</c>, so the frame boundary has to
        /// push the same value into it that <c>Time.deltaTime</c> reports.
        /// </summary>
        [Test]
        public void EngineClockDeltaTimeTracksTimeDeltaTime()
        {
            host.testClock.seconds = 5d;
            NowRuntime.BeginFrame();
            host.testClock.seconds = 5.125d;
            NowRuntime.BeginFrame();

            Assert.That(EngineClock.deltaTime, Is.EqualTo(UnityEngine.Time.deltaTime));
            Assert.That(EngineClock.deltaTime, Is.EqualTo(0.125f).Within(1e-6f));
        }

        /// <summary>A host clock that steps backwards must not run animation in reverse (design §6.1).</summary>
        [Test]
        public void ABackwardsClockNeverProducesNegativeTime()
        {
            host.testClock.seconds = 50d;
            NowRuntime.BeginFrame();
            host.testClock.seconds = 40d;
            NowRuntime.BeginFrame();

            Assert.Multiple(() =>
            {
                Assert.That(UnityEngine.Time.deltaTime, Is.EqualTo(0f));
                Assert.That(UnityEngine.Time.realtimeSinceStartupAsDouble, Is.GreaterThanOrEqualTo(0d));
            });
        }

        [Test]
        public void ConstantsMatchUnityDefaults()
        {
            Assert.Multiple(() =>
            {
                Assert.That(UnityEngine.Time.fixedDeltaTime, Is.EqualTo(0.02f));
                Assert.That(UnityEngine.Time.timeScale, Is.EqualTo(1f));
            });
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Screen - design §3.7
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class ScreenServiceTests : ServiceFixture
    {
        [Test]
        public void GeometryComesFromTheHost()
        {
            host.screenInfo = new NowScreenInfo(800, 600, 144f, new Rect(0f, 20f, 800f, 560f));

            Assert.Multiple(() =>
            {
                Assert.That(UnityEngine.Screen.width, Is.EqualTo(800));
                Assert.That(UnityEngine.Screen.height, Is.EqualTo(600));
                Assert.That(UnityEngine.Screen.dpi, Is.EqualTo(144f));
                Assert.That(UnityEngine.Screen.safeArea.x, Is.EqualTo(0f));
                Assert.That(UnityEngine.Screen.safeArea.y, Is.EqualTo(20f), "bottom-left origin");
                Assert.That(UnityEngine.Screen.safeArea.width, Is.EqualTo(800f));
                Assert.That(UnityEngine.Screen.safeArea.height, Is.EqualTo(560f));
            });
        }

        /// <summary>Design §3.7: re-read on every access, so a resize needs no notification plumbing.</summary>
        [Test]
        public void GeometryIsReReadOnEveryAccess()
        {
            host.screenInfo = new NowScreenInfo(400, 300, 96f);
            Assert.That(UnityEngine.Screen.width, Is.EqualTo(400));

            host.screenInfo = new NowScreenInfo(1280, 720, 96f);
            Assert.Multiple(() =>
            {
                Assert.That(UnityEngine.Screen.width, Is.EqualTo(1280));
                Assert.That(UnityEngine.Screen.height, Is.EqualTo(720));
            });
        }

        /// <summary>A host that omits a safe area gets the full rect, in the bottom-left convention.</summary>
        [Test]
        public void DefaultSafeAreaIsTheWholeScreen()
        {
            host.screenInfo = new NowScreenInfo(1920, 1080, 96f);

            Rect area = UnityEngine.Screen.safeArea;
            Assert.Multiple(() =>
            {
                Assert.That(area.x, Is.EqualTo(0f));
                Assert.That(area.y, Is.EqualTo(0f));
                Assert.That(area.width, Is.EqualTo(1920f));
                Assert.That(area.height, Is.EqualTo(1080f));
            });
        }

        [Test]
        public void OrientationFollowsTheHostAspect()
        {
            host.screenInfo = new NowScreenInfo(1920, 1080, 96f);
            Assert.That(UnityEngine.Screen.orientation, Is.EqualTo(ScreenOrientation.LandscapeLeft));

            host.screenInfo = new NowScreenInfo(1080, 1920, 96f);
            Assert.That(UnityEngine.Screen.orientation, Is.EqualTo(ScreenOrientation.Portrait));
        }

        [Test]
        public void CurrentResolutionReportsTheHostSurface()
        {
            host.screenInfo = new NowScreenInfo(2560, 1440, 96f);

            Resolution resolution = UnityEngine.Screen.currentResolution;
            Assert.Multiple(() =>
            {
                Assert.That(resolution.width, Is.EqualTo(2560));
                Assert.That(resolution.height, Is.EqualTo(1440));
                Assert.That(resolution.refreshRateRatio.value, Is.EqualTo(60d));
            });
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Application - design §3.7, §6.6
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class ApplicationServiceTests : ServiceFixture
    {
        [Test]
        public void PathsAndPlatformComeFromTheHost()
        {
            host.testPlatform = RuntimePlatform.WebGLPlayer;
            host.testPersistentDataPath = "/persist";
            host.testDataPath = "/data";

            Assert.Multiple(() =>
            {
                Assert.That(UnityEngine.Application.platform, Is.EqualTo(RuntimePlatform.WebGLPlayer));
                Assert.That(UnityEngine.Application.persistentDataPath, Is.EqualTo("/persist"));
                Assert.That(UnityEngine.Application.dataPath, Is.EqualTo("/data"));
                Assert.That(UnityEngine.Application.streamingAssetsPath, Does.Contain("StreamingAssets"));
            });
        }

        [Test]
        public void IsPlayingFollowsTheRuntime()
        {
            bool previous = NowRuntime.isPlaying;
            try
            {
                NowRuntime.isPlaying = true;
                Assert.That(UnityEngine.Application.isPlaying, Is.True);

                NowRuntime.isPlaying = false;
                Assert.That(UnityEngine.Application.isPlaying, Is.False);
            }
            finally
            {
                NowRuntime.isPlaying = previous;
            }
        }

        [Test]
        public void IsEditorIsAlwaysFalse()
        {
            Assert.That(UnityEngine.Application.isEditor, Is.False);
            Assert.That(UnityEngine.Application.isBatchMode, Is.False);
        }

        [Test]
        public void UnityVersionIsTheStandaloneSentinel()
        {
            Assert.That(UnityEngine.Application.unityVersion, Is.EqualTo("0.0.0-nowui-standalone"));
            Assert.That(UnityEngine.Application.version, Is.Not.Null.And.Not.Empty);
        }

        [TestCase(RuntimePlatform.Android, true)]
        [TestCase(RuntimePlatform.IPhonePlayer, true)]
        [TestCase(RuntimePlatform.tvOS, true)]
        [TestCase(RuntimePlatform.WindowsPlayer, false)]
        [TestCase(RuntimePlatform.OSXPlayer, false)]
        [TestCase(RuntimePlatform.LinuxPlayer, false)]
        [TestCase(RuntimePlatform.WebGLPlayer, false)]
        public void IsMobilePlatformFollowsThePlatform(RuntimePlatform platform, bool expected)
        {
            host.testPlatform = platform;
            Assert.That(UnityEngine.Application.isMobilePlatform, Is.EqualTo(expected));
        }

        [Test]
        public void TargetFrameRateDefaultsToUnitysSentinel()
        {
            int previous = UnityEngine.Application.targetFrameRate;
            try
            {
                Assert.That(previous, Is.EqualTo(-1));
                UnityEngine.Application.targetFrameRate = 30;
                Assert.That(UnityEngine.Application.targetFrameRate, Is.EqualTo(30));
            }
            finally
            {
                UnityEngine.Application.targetFrameRate = previous;
            }
        }

        /// <summary>Design §6.6: <c>NowRuntime.Shutdown()</c> is what raises <c>Application.quitting</c>.</summary>
        [Test]
        public void ShutdownRaisesQuitting()
        {
            int raised = 0;
            Action handler = () => raised++;

            UnityEngine.Application.quitting += handler;
            try
            {
                NowRuntime.Shutdown();
                Assert.That(raised, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Application.quitting -= handler;
            }
        }

        /// <summary>A <c>wantsToQuit</c> handler that refuses cancels the quit entirely, as in Unity.</summary>
        [Test]
        public void WantsToQuitCanVetoQuit()
        {
            int quitting = 0;
            Action quittingHandler = () => quitting++;
            Func<bool> veto = () => false;

            UnityEngine.Application.quitting += quittingHandler;
            UnityEngine.Application.wantsToQuit += veto;
            try
            {
                UnityEngine.Application.Quit();
                Assert.That(quitting, Is.Zero, "a vetoed quit must not raise quitting");
            }
            finally
            {
                UnityEngine.Application.wantsToQuit -= veto;
                UnityEngine.Application.quitting -= quittingHandler;
            }
        }

        [Test]
        public void QuitProceedsWhenNothingObjects()
        {
            int quitting = 0;
            Action quittingHandler = () => quitting++;
            Func<bool> allow = () => true;

            UnityEngine.Application.quitting += quittingHandler;
            UnityEngine.Application.wantsToQuit += allow;
            try
            {
                UnityEngine.Application.Quit();
                Assert.That(quitting, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Application.wantsToQuit -= allow;
                UnityEngine.Application.quitting -= quittingHandler;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // QualitySettings - design §3.7, H.14
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class QualitySettingsServiceTests : ServiceFixture
    {
        /// <summary>H.14: Gamma by default, matching this project's ProjectSettings.</summary>
        [Test]
        public void ActiveColorSpaceDefaultsToGammaAndFollowsTheRuntime()
        {
            ColorSpace previous = NowRuntime.colorSpace;
            try
            {
                Assert.That(NowRuntime.colorSpace, Is.EqualTo(ColorSpace.Gamma), "the runtime default");
                Assert.That(UnityEngine.QualitySettings.activeColorSpace, Is.EqualTo(ColorSpace.Gamma));

                NowRuntime.colorSpace = ColorSpace.Linear;
                Assert.That(UnityEngine.QualitySettings.activeColorSpace, Is.EqualTo(ColorSpace.Linear));
            }
            finally
            {
                NowRuntime.colorSpace = previous;
            }
        }

        [Test]
        public void SettingsRoundTrip()
        {
            int aa = UnityEngine.QualitySettings.antiAliasing;
            int vsync = UnityEngine.QualitySettings.vSyncCount;
            try
            {
                UnityEngine.QualitySettings.antiAliasing = 4;
                UnityEngine.QualitySettings.vSyncCount = 0;

                Assert.Multiple(() =>
                {
                    Assert.That(UnityEngine.QualitySettings.antiAliasing, Is.EqualTo(4));
                    Assert.That(UnityEngine.QualitySettings.vSyncCount, Is.EqualTo(0));
                });
            }
            finally
            {
                UnityEngine.QualitySettings.antiAliasing = aa;
                UnityEngine.QualitySettings.vSyncCount = vsync;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // SystemInfo - design §3.7, §4.1
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class SystemInfoServiceTests : ServiceFixture
    {
        /// <summary>Every graphics answer is the backend's capability struct, read live rather than cached.</summary>
        [Test]
        public void ValuesComeFromTheBackendCaps()
        {
            NowRenderCaps caps = NowRuntime.backend.caps;

            Assert.Multiple(() =>
            {
                Assert.That(UnityEngine.SystemInfo.maxTextureSize, Is.EqualTo(caps.maxTextureSize));
                Assert.That(UnityEngine.SystemInfo.supportsInstancing, Is.EqualTo(caps.supportsInstancing));
                Assert.That(
                    UnityEngine.SystemInfo.supportsMultisampledTextures,
                    Is.EqualTo(caps.supportsMultisampledTextures));
                Assert.That(UnityEngine.SystemInfo.graphicsDeviceName, Is.EqualTo(caps.deviceName));
                Assert.That(UnityEngine.SystemInfo.graphicsDeviceType, Is.EqualTo(caps.deviceType));
                Assert.That(UnityEngine.SystemInfo.graphicsMemorySize, Is.EqualTo(caps.graphicsMemorySizeMb));
                Assert.That(
                    UnityEngine.SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32),
                    Is.EqualTo(caps.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32)));
                Assert.That(
                    UnityEngine.SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32),
                    Is.EqualTo(caps.SupportsTextureFormat(TextureFormat.RGBA32)));
            });
        }

        /// <summary>WebGL2 has no compute shaders; NowUI reads the flag only to take its non-compute path.</summary>
        [Test]
        public void ComputeShadersAreNeverSupported()
        {
            Assert.That(UnityEngine.SystemInfo.supportsComputeShaders, Is.False);
        }

        [Test]
        public void OperatingSystemIsReported()
        {
            Assert.That(UnityEngine.SystemInfo.operatingSystem, Is.Not.Null.And.Not.Empty);
        }

        /// <summary>
        /// The sample count is a power of two, at least 1, and never more than the descriptor asked for - which is
        /// what a driver query guarantees and what the caller's clamping assumes.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(64)]
        public void SupportedMsaaSampleCountIsAPowerOfTwoWithinTheRequest(int requested)
        {
            RenderTextureDescriptor descriptor = default;
            descriptor.msaaSamples = requested;

            int samples = UnityEngine.SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);

            Assert.Multiple(() =>
            {
                Assert.That(samples, Is.GreaterThanOrEqualTo(1));
                Assert.That(samples & (samples - 1), Is.Zero, "must be a power of two");
                Assert.That(samples, Is.LessThanOrEqualTo(Math.Max(1, requested)));
            });
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Resources - design §3.7, §4.4
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class ResourcesServiceTests : ServiceFixture
    {
        private SinglePathResourceProvider provider;
        private FakeAsset asset;

        [SetUp]
        public void InstallProvider()
        {
            asset = new FakeAsset();
            provider = new SinglePathResourceProvider { path = "NowUI/NotoSans", asset = asset };
            host.testResources = provider;
        }

        /// <summary>A provider must return the same instance for the same path: core code caches by reference.</summary>
        [Test]
        public void LoadReturnsTheProvidersInstance()
        {
            FakeAsset first = UnityEngine.Resources.Load<FakeAsset>("NowUI/NotoSans");
            FakeAsset second = UnityEngine.Resources.Load<FakeAsset>("NowUI/NotoSans");

            Assert.That(first, Is.SameAs(asset));
            Assert.That(second, Is.SameAs(asset));
        }

        [Test]
        public void MissingPathLoadsNull()
        {
            Assert.That(UnityEngine.Resources.Load<FakeAsset>("NowUI/Nope"), Is.Null);
        }

        /// <summary>Unity's <c>Load&lt;T&gt;</c> returns null rather than throwing when the asset is another type.</summary>
        [Test]
        public void TypeMismatchLoadsNull()
        {
            Assert.That(UnityEngine.Resources.Load<OtherFakeAsset>("NowUI/NotoSans"), Is.Null);
        }

        [Test]
        public void NonGenericOverloadsReachTheProvider()
        {
            Assert.That(UnityEngine.Resources.Load("NowUI/NotoSans"), Is.SameAs(asset));
            Assert.That(UnityEngine.Resources.Load("NowUI/NotoSans", typeof(FakeAsset)), Is.SameAs(asset));
        }

        [Test]
        public void LoadAllReportsTheAssetOrNothing()
        {
            FakeAsset[] found = UnityEngine.Resources.LoadAll<FakeAsset>("NowUI/NotoSans");
            Assert.That(found, Has.Length.EqualTo(1));
            Assert.That(found[0], Is.SameAs(asset));

            Assert.That(UnityEngine.Resources.LoadAll<FakeAsset>("NowUI/Nope"), Is.Empty);
        }

        /// <summary>Unloading must not take the provider's instance away: it has to keep returning the same one.</summary>
        [Test]
        public void UnloadingDoesNotDisturbTheProvider()
        {
            UnityEngine.Resources.UnloadAsset(asset);
            UnityEngine.Resources.UnloadUnusedAssets();

            Assert.That(UnityEngine.Resources.Load<FakeAsset>("NowUI/NotoSans"), Is.SameAs(asset));
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // TouchScreenKeyboard - GradientCurveSemantics.md §9, design §3.7
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class TouchScreenKeyboardTests : ServiceFixture
    {
        [Test]
        public void StatusValuesMatchUnity()
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)TouchScreenKeyboard.Status.Visible, Is.EqualTo(0));
                Assert.That((int)TouchScreenKeyboard.Status.Done, Is.EqualTo(1));
                Assert.That((int)TouchScreenKeyboard.Status.Canceled, Is.EqualTo(2));
                Assert.That((int)TouchScreenKeyboard.Status.LostFocus, Is.EqualTo(3));
            });
        }

        /// <summary>Design §3.7: no host keyboard means unsupported, which is the M1 branch NowUI takes.</summary>
        [Test]
        public void IsSupportedIsFalseWithoutAHostKeyboard()
        {
            Assert.That(host.touchKeyboard, Is.Null);
            Assert.That(TouchScreenKeyboard.isSupported, Is.False);
        }

        [Test]
        public void IsSupportedIsTrueWhenTheHostProvidesOne()
        {
            host.testTouchKeyboard = new FakeTouchKeyboard();
            Assert.That(TouchScreenKeyboard.isSupported, Is.True);
        }

        [Test]
        public void OpenForwardsToTheHostKeyboard()
        {
            FakeTouchKeyboard keyboard = new FakeTouchKeyboard();
            host.testTouchKeyboard = keyboard;

            TouchScreenKeyboard opened = TouchScreenKeyboard.Open(
                "hello", TouchScreenKeyboardType.EmailAddress, false, true);

            Assert.Multiple(() =>
            {
                Assert.That(opened, Is.Not.Null);
                Assert.That(keyboard.lastText, Is.EqualTo("hello"));
                Assert.That(keyboard.lastType, Is.EqualTo(TouchScreenKeyboardType.EmailAddress));
                Assert.That(keyboard.lastAutocorrection, Is.False);
                Assert.That(keyboard.lastMultiline, Is.True);
                Assert.That(keyboard.lastSecure, Is.False);
                Assert.That(opened.type, Is.EqualTo(TouchScreenKeyboardType.EmailAddress));
            });
        }

        /// <summary>Unity's defaults for the short overloads: autocorrection on, everything else off (GC §9).</summary>
        [Test]
        public void ShortOverloadsUseUnitysDefaults()
        {
            FakeTouchKeyboard keyboard = new FakeTouchKeyboard();
            host.testTouchKeyboard = keyboard;

            TouchScreenKeyboard.Open("x");

            Assert.Multiple(() =>
            {
                Assert.That(keyboard.lastType, Is.EqualTo(TouchScreenKeyboardType.Default));
                Assert.That(keyboard.lastAutocorrection, Is.True);
                Assert.That(keyboard.lastMultiline, Is.False);
                Assert.That(keyboard.lastSecure, Is.False);
            });
        }

        /// <summary>All eight overloads exist and none of them is an optional-parameter stand-in (GC §9).</summary>
        [Test]
        public void EightExplicitOpenOverloadsWithNoOptionalParameters()
        {
            System.Reflection.MethodInfo[] methods = typeof(TouchScreenKeyboard).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            int count = 0;
            foreach (System.Reflection.MethodInfo method in methods)
            {
                if (method.Name != "Open")
                    continue;

                count++;
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                    Assert.That(parameter.IsOptional, Is.False, method.ToString());
            }

            Assert.That(count, Is.EqualTo(8));
        }

        [Test]
        public void TextAndActiveTrackTheSession()
        {
            FakeTouchKeyboard keyboard = new FakeTouchKeyboard();
            host.testTouchKeyboard = keyboard;

            TouchScreenKeyboard opened = TouchScreenKeyboard.Open("start", TouchScreenKeyboardType.Default);
            FakeKeyboardSession session = keyboard.opened[0];

            Assert.That(opened.text, Is.EqualTo("start"));
            Assert.That(opened.status, Is.EqualTo(TouchScreenKeyboard.Status.Visible));
            Assert.That(opened.active, Is.True);

            session.textValue = "typed";
            Assert.That(opened.text, Is.EqualTo("typed"));

            opened.text = "replaced";
            Assert.That(session.textValue, Is.EqualTo("replaced"));

            session.statusValue = TouchScreenKeyboard.Status.Done;
            Assert.That(opened.status, Is.EqualTo(TouchScreenKeyboard.Status.Done));

            opened.active = false;
            Assert.That(session.activeValue, Is.False);
        }

        /// <summary>
        /// The documented divergence: with no host keyboard the object reports a cancelled, inactive session instead
        /// of throwing the way Unity's unsupported-platform object does.
        /// </summary>
        [Test]
        public void WithoutAHostKeyboardTheSessionIsInertRatherThanThrowing()
        {
            TouchScreenKeyboard opened = TouchScreenKeyboard.Open("text", TouchScreenKeyboardType.Default);

            Assert.Multiple(() =>
            {
                Assert.That(opened, Is.Not.Null, "Unity's Open never returns null");
                Assert.That(opened.status, Is.EqualTo(TouchScreenKeyboard.Status.Canceled));
                Assert.That(opened.active, Is.False);
                Assert.That(opened.text, Is.EqualTo("text"));
                Assert.That(TouchScreenKeyboard.area, Is.EqualTo(Rect.zero));
                Assert.That(TouchScreenKeyboard.visible, Is.False);
            });
        }

        [Test]
        public void CharacterLimitRoundTrips()
        {
            TouchScreenKeyboard opened = TouchScreenKeyboard.Open(
                "", TouchScreenKeyboardType.Default, true, false, false, false, "", 12);

            Assert.That(opened.characterLimit, Is.EqualTo(12));
            opened.characterLimit = 40;
            Assert.That(opened.characterLimit, Is.EqualTo(40));
        }

        /// <summary>Not sealed, and not a <c>UnityEngine.Object</c>: core code plain-null-checks it (GC §9).</summary>
        [Test]
        public void TypeShapeMatchesUnity()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(TouchScreenKeyboard).IsSealed, Is.False);
                Assert.That(typeof(UnityEngine.Object).IsAssignableFrom(typeof(TouchScreenKeyboard)), Is.False);
            });
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // ExpressionEvaluator - design §3.7, §12.5
    // ---------------------------------------------------------------------------------------------------------

    [TestFixture]
    public class ExpressionEvaluatorTests
    {
        /// <summary>
        /// Design §3.7: always false, which routes <c>NowNumericExpression</c> to its own bounded parser. §12.5 keeps
        /// the question of whether that parser agrees with Unity's on all 61 golden values open until U27 measures it.
        /// </summary>
        [TestCase("1+2*3")]
        [TestCase("pi")]
        [TestCase("sqrt(16)")]
        [TestCase("")]
        [TestCase("not an expression")]
        public void EvaluateAlwaysFailsAndLeavesTheDefault(string expression)
        {
            Assert.That(ExpressionEvaluator.Evaluate(expression, out double value), Is.False);
            Assert.That(value, Is.EqualTo(0d));

            Assert.That(ExpressionEvaluator.Evaluate(expression, out int intValue), Is.False);
            Assert.That(intValue, Is.Zero);
        }

        [Test]
        public void NullExpressionDoesNotThrow()
        {
            Assert.DoesNotThrow(() => ExpressionEvaluator.Evaluate(null, out double _));
        }

        /// <summary>A class with static members, not a static class, exactly as in Unity (GC §8).</summary>
        [Test]
        public void TypeShapeMatchesUnity()
        {
            Assert.Multiple(() =>
            {
                Assert.That(typeof(ExpressionEvaluator).IsAbstract, Is.False);
                Assert.That(typeof(ExpressionEvaluator).IsSealed, Is.False);
            });
        }
    }
}
