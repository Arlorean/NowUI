// Tests for U1 (Engine/Enums/{HideFlags,KeyCode,RuntimePlatform,TextureEnums,DataEnums,ImguiEnums,Rendering}.cs)
// against StandaloneCoreDesign.md §3.2, UnityValueTypeSemantics.md §14, GradientCurveSemantics.md §2/§4.1/§4.3/§9
// and inventory §A.4/§A.5.
//
// The numeric values are the whole contract of these types (NowKeyInput does arithmetic on KeyCode; the mesh and
// command-buffer paths hand these numbers to a backend), so the tables below are asserted *exhaustively*: every
// declared member must have the expected value AND the member set must contain nothing else. The one exception is
// KeyCode, where 339 members are covered by pinned samples at every range boundary plus contiguity assertions for
// exactly the ranges NowKeyInput.FromIMGUIKeyCode range-maps.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class EnumValuesTests
    {
        // ---- helpers -------------------------------------------------------------------------------------------

        /// <summary>
        /// Asserts an enum's complete surface: int-backed, [Flags] exactly where Unity has it, every expected
        /// name/value pair present, and no member beyond the expected set.
        /// </summary>
        private static void AssertEnum<T>(bool flags, params (string Name, int Value)[] expected) where T : struct, Enum
        {
            Type t = typeof(T);
            Assert.Multiple(() =>
            {
                Assert.That(Enum.GetUnderlyingType(t), Is.EqualTo(typeof(int)), $"{t.Name} underlying type");
                Assert.That(t.IsDefined(typeof(FlagsAttribute), false), Is.EqualTo(flags), $"{t.Name} [Flags]");

                foreach ((string name, int value) in expected)
                {
                    bool defined = Enum.IsDefined(t, name);
                    Assert.That(defined, Is.True, $"{t.Name}.{name} is missing");
                    if (!defined) continue;
                    Assert.That(Convert.ToInt32(Enum.Parse(t, name)), Is.EqualTo(value), $"{t.Name}.{name}");
                }

                var extra = new HashSet<string>(Enum.GetNames(t), StringComparer.Ordinal);
                extra.ExceptWith(expected.Select(e => e.Name));
                Assert.That(extra, Is.Empty, $"{t.Name} has members the spec does not list");
                Assert.That(Enum.GetNames(t).Length, Is.EqualTo(expected.Length), $"{t.Name} member count");
            });
        }

        private static int V<T>(T value) where T : struct, Enum => Convert.ToInt32(value);

        // ---- HideFlags (design §3.2) ---------------------------------------------------------------------------

        [Test]
        public void HideFlags_MatchesUnity()
        {
            AssertEnum<HideFlags>(flags: true,
                ("None", 0),
                ("HideInHierarchy", 1),
                ("HideInInspector", 2),
                ("DontSaveInEditor", 4),
                ("NotEditable", 8),
                ("DontSaveInBuild", 16),
                ("DontUnloadUnusedAsset", 32),
                ("DontSave", 52),
                ("HideAndDontSave", 61));
        }

        [Test]
        public void HideFlags_CompositesAreUnitysOddCombinations()
        {
            // DontSave excludes the two "hide" bits; HideAndDontSave adds HideInHierarchy and NotEditable but NOT
            // HideInInspector, which is why it is 61 and not 63. Asserted so a "tidy-up" cannot silently change it.
            Assert.Multiple(() =>
            {
                Assert.That(HideFlags.DontSave,
                    Is.EqualTo(HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.DontUnloadUnusedAsset));
                Assert.That(HideFlags.HideAndDontSave,
                    Is.EqualTo(HideFlags.DontSave | HideFlags.HideInHierarchy | HideFlags.NotEditable));
                Assert.That(HideFlags.HideAndDontSave.HasFlag(HideFlags.HideInInspector), Is.False);
            });
        }

        // ---- KeyCode (design §3.2, inventory §A.5) -------------------------------------------------------------

        [Test]
        public void KeyCode_SampledValuesMatchUnity()
        {
            Assert.Multiple(() =>
            {
                // control / editing block (ASCII-derived)
                Assert.That(V(KeyCode.None), Is.EqualTo(0));
                Assert.That(V(KeyCode.Backspace), Is.EqualTo(8));
                Assert.That(V(KeyCode.Tab), Is.EqualTo(9));
                Assert.That(V(KeyCode.Clear), Is.EqualTo(12));
                Assert.That(V(KeyCode.Return), Is.EqualTo(13));
                Assert.That(V(KeyCode.Pause), Is.EqualTo(19));
                Assert.That(V(KeyCode.Escape), Is.EqualTo(27));
                Assert.That(V(KeyCode.Space), Is.EqualTo(32));
                Assert.That(V(KeyCode.Delete), Is.EqualTo(127));

                // printable ASCII keys carry their ASCII code; letters are LOWER case codes
                Assert.That(V(KeyCode.Alpha0), Is.EqualTo(48));
                Assert.That(V(KeyCode.Alpha9), Is.EqualTo(57));
                Assert.That(V(KeyCode.Quote), Is.EqualTo(39));
                Assert.That(V(KeyCode.Comma), Is.EqualTo(44));
                Assert.That(V(KeyCode.Minus), Is.EqualTo(45));
                Assert.That(V(KeyCode.Period), Is.EqualTo(46));
                Assert.That(V(KeyCode.Slash), Is.EqualTo(47));
                Assert.That(V(KeyCode.Semicolon), Is.EqualTo(59));
                Assert.That(V(KeyCode.Equals), Is.EqualTo(61));
                Assert.That(V(KeyCode.LeftBracket), Is.EqualTo(91));
                Assert.That(V(KeyCode.Backslash), Is.EqualTo(92));
                Assert.That(V(KeyCode.RightBracket), Is.EqualTo(93));
                Assert.That(V(KeyCode.BackQuote), Is.EqualTo(96));
                Assert.That(V(KeyCode.A), Is.EqualTo(97), "'a', not 'A'");
                Assert.That(V(KeyCode.Z), Is.EqualTo(122));
                Assert.That(V(KeyCode.Tilde), Is.EqualTo(126));

                // keypad / navigation block
                Assert.That(V(KeyCode.Keypad0), Is.EqualTo(256));
                Assert.That(V(KeyCode.Keypad9), Is.EqualTo(265));
                Assert.That(V(KeyCode.KeypadPeriod), Is.EqualTo(266));
                Assert.That(V(KeyCode.KeypadDivide), Is.EqualTo(267));
                Assert.That(V(KeyCode.KeypadMultiply), Is.EqualTo(268));
                Assert.That(V(KeyCode.KeypadMinus), Is.EqualTo(269));
                Assert.That(V(KeyCode.KeypadPlus), Is.EqualTo(270));
                Assert.That(V(KeyCode.KeypadEnter), Is.EqualTo(271));
                Assert.That(V(KeyCode.KeypadEquals), Is.EqualTo(272));
                Assert.That(V(KeyCode.UpArrow), Is.EqualTo(273));
                Assert.That(V(KeyCode.DownArrow), Is.EqualTo(274));
                Assert.That(V(KeyCode.RightArrow), Is.EqualTo(275));
                Assert.That(V(KeyCode.LeftArrow), Is.EqualTo(276));
                Assert.That(V(KeyCode.Insert), Is.EqualTo(277));
                Assert.That(V(KeyCode.Home), Is.EqualTo(278));
                Assert.That(V(KeyCode.End), Is.EqualTo(279));
                Assert.That(V(KeyCode.PageUp), Is.EqualTo(280));
                Assert.That(V(KeyCode.PageDown), Is.EqualTo(281));

                // function keys: two separate runs, 282..296 and 670..678
                Assert.That(V(KeyCode.F1), Is.EqualTo(282));
                Assert.That(V(KeyCode.F12), Is.EqualTo(293));
                Assert.That(V(KeyCode.F13), Is.EqualTo(294));
                Assert.That(V(KeyCode.F15), Is.EqualTo(296));
                Assert.That(V(KeyCode.F16), Is.EqualTo(670));
                Assert.That(V(KeyCode.F24), Is.EqualTo(678));

                // modifiers and system keys
                Assert.That(V(KeyCode.Numlock), Is.EqualTo(300));
                Assert.That(V(KeyCode.CapsLock), Is.EqualTo(301));
                Assert.That(V(KeyCode.ScrollLock), Is.EqualTo(302));
                Assert.That(V(KeyCode.RightShift), Is.EqualTo(303));
                Assert.That(V(KeyCode.LeftShift), Is.EqualTo(304));
                Assert.That(V(KeyCode.RightControl), Is.EqualTo(305));
                Assert.That(V(KeyCode.LeftControl), Is.EqualTo(306));
                Assert.That(V(KeyCode.RightAlt), Is.EqualTo(307));
                Assert.That(V(KeyCode.LeftAlt), Is.EqualTo(308));
                Assert.That(V(KeyCode.LeftWindows), Is.EqualTo(311));
                Assert.That(V(KeyCode.RightWindows), Is.EqualTo(312));
                Assert.That(V(KeyCode.AltGr), Is.EqualTo(313));
                Assert.That(V(KeyCode.Help), Is.EqualTo(315));
                Assert.That(V(KeyCode.Print), Is.EqualTo(316));
                Assert.That(V(KeyCode.SysReq), Is.EqualTo(317));
                Assert.That(V(KeyCode.Break), Is.EqualTo(318));
                Assert.That(V(KeyCode.Menu), Is.EqualTo(319));
                Assert.That(V(KeyCode.WheelUp), Is.EqualTo(321));
                Assert.That(V(KeyCode.WheelDown), Is.EqualTo(322));

                // mouse and joystick blocks (present so Enum.GetValues(typeof(KeyCode)) matches Unity)
                Assert.That(V(KeyCode.Mouse0), Is.EqualTo(323));
                Assert.That(V(KeyCode.Mouse6), Is.EqualTo(329));
                Assert.That(V(KeyCode.JoystickButton0), Is.EqualTo(330));
                Assert.That(V(KeyCode.JoystickButton19), Is.EqualTo(349));
                Assert.That(V(KeyCode.Joystick1Button0), Is.EqualTo(350));
                Assert.That(V(KeyCode.Joystick8Button19), Is.EqualTo(509));
            });
        }

        [Test]
        public void KeyCode_MetaKeyAliasesShareUnitysValues()
        {
            // One physical key, three historical spellings; and the RIGHT meta key is numerically lower than the left.
            Assert.Multiple(() =>
            {
                Assert.That(V(KeyCode.LeftMeta), Is.EqualTo(310));
                Assert.That(V(KeyCode.LeftCommand), Is.EqualTo(310));
                Assert.That(V(KeyCode.LeftApple), Is.EqualTo(310));
                Assert.That(V(KeyCode.RightMeta), Is.EqualTo(309));
                Assert.That(V(KeyCode.RightCommand), Is.EqualTo(309));
                Assert.That(V(KeyCode.RightApple), Is.EqualTo(309));
                Assert.That(V(KeyCode.RightCommand), Is.LessThan(V(KeyCode.LeftCommand)));
            });
        }

        [TestCase("A", "Z", 26)]                 // NowKeyInput.FromIMGUIKeyCode maps letters by offset
        [TestCase("Alpha0", "Alpha9", 10)]
        [TestCase("Keypad0", "Keypad9", 10)]
        [TestCase("F1", "F15", 15)]
        [TestCase("F16", "F24", 9)]
        [TestCase("Mouse0", "Mouse6", 7)]
        [TestCase("JoystickButton0", "JoystickButton19", 20)]
        public void KeyCode_RangesAreContiguous(string first, string last, int count)
        {
            // NowKeyInput does `(Key)((int)Key.X + ((int)keyCode - (int)KeyCode.X))` over these ranges, so a gap or a
            // reordering inside one of them would silently mis-map keys rather than fail to compile.
            int lo = Convert.ToInt32(Enum.Parse<KeyCode>(first));
            int hi = Convert.ToInt32(Enum.Parse<KeyCode>(last));
            Assert.That(hi - lo + 1, Is.EqualTo(count), $"{first}..{last} span");
            for (int i = lo; i <= hi; i++)
                Assert.That(Enum.IsDefined(typeof(KeyCode), i), Is.True, $"KeyCode {i} in {first}..{last}");
        }

        [Test]
        public void KeyCode_CoversEveryMemberInventoryA5Lists()
        {
            // The 34-member core list plus the host-only list from inventory §A.5, so a partial enum fails here rather
            // than at a random call site during U21's compile loop.
            string[] required =
            {
                "LeftArrow", "RightArrow", "UpArrow", "DownArrow", "A", "D", "W", "S", "Return", "KeypadEnter",
                "Space", "Escape", "Tab", "Backspace", "Delete", "Home", "End", "F2", "LeftShift", "RightShift",
                "LeftControl", "RightControl", "LeftCommand", "RightCommand", "LeftAlt", "RightAlt", "AltGr",
                "C", "V", "X", "Z", "Y", "Slash", "G",
                "BackQuote", "Quote", "Semicolon", "Comma", "Period", "Backslash", "LeftBracket", "RightBracket",
                "Minus", "Equals", "LeftWindows", "RightWindows", "Menu", "PageDown", "PageUp", "Insert", "CapsLock",
                "Numlock", "Print", "SysReq", "ScrollLock", "Pause", "Break", "KeypadDivide", "KeypadMultiply",
                "KeypadPlus", "KeypadMinus", "KeypadPeriod", "KeypadEquals",
            };
            Assert.Multiple(() =>
            {
                foreach (string name in required)
                    Assert.That(Enum.IsDefined(typeof(KeyCode), name), Is.True, $"KeyCode.{name}");
                for (int i = 1; i <= 24; i++)
                    Assert.That(Enum.IsDefined(typeof(KeyCode), "F" + i), Is.True, $"KeyCode.F{i}");
                for (char c = 'A'; c <= 'Z'; c++)
                    Assert.That(Enum.IsDefined(typeof(KeyCode), c.ToString()), Is.True, $"KeyCode.{c}");
                for (int i = 0; i <= 9; i++)
                {
                    Assert.That(Enum.IsDefined(typeof(KeyCode), "Alpha" + i), Is.True, $"KeyCode.Alpha{i}");
                    Assert.That(Enum.IsDefined(typeof(KeyCode), "Keypad" + i), Is.True, $"KeyCode.Keypad{i}");
                }
            });
        }

        [Test]
        public void KeyCode_IsIntBackedAndComplete()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Enum.GetUnderlyingType(typeof(KeyCode)), Is.EqualTo(typeof(int)));
                Assert.That(typeof(KeyCode).IsDefined(typeof(FlagsAttribute), false), Is.False);
                Assert.That(Enum.GetNames(typeof(KeyCode)).Length, Is.EqualTo(339),
                    "the complete Unity 6000.4 KeyCode, aliases included");
            });
        }

        // ---- RuntimePlatform (design §3.2) ---------------------------------------------------------------------

        [Test]
        public void RuntimePlatform_SampledValuesMatchUnity()
        {
            Assert.Multiple(() =>
            {
                // the six NowFilePickerUserFolders.Platform compares against
                Assert.That(V(RuntimePlatform.OSXEditor), Is.EqualTo(0));
                Assert.That(V(RuntimePlatform.OSXPlayer), Is.EqualTo(1));
                Assert.That(V(RuntimePlatform.WindowsPlayer), Is.EqualTo(2));
                Assert.That(V(RuntimePlatform.WindowsEditor), Is.EqualTo(7), "7, not 3 — slot 3 was OSXWebPlayer");
                Assert.That(V(RuntimePlatform.LinuxPlayer), Is.EqualTo(13));
                Assert.That(V(RuntimePlatform.LinuxEditor), Is.EqualTo(16));

                Assert.That(V(RuntimePlatform.Android), Is.EqualTo(11));
                Assert.That(V(RuntimePlatform.IPhonePlayer), Is.EqualTo(8));
                Assert.That(V(RuntimePlatform.WebGLPlayer), Is.EqualTo(17));
                Assert.That(V(RuntimePlatform.Switch), Is.EqualTo(32));
                Assert.That(V(RuntimePlatform.PS5), Is.EqualTo(38));
                Assert.That(V(RuntimePlatform.VisionOS), Is.EqualTo(50));
            });
        }

        [Test]
        public void RuntimePlatform_AliasesAndGapsAreUnityFaithful()
        {
            Assert.Multiple(() =>
            {
                Assert.That(V(RuntimePlatform.MetroPlayerX86), Is.EqualTo(V(RuntimePlatform.WSAPlayerX86)));
                Assert.That(V(RuntimePlatform.MetroPlayerX64), Is.EqualTo(V(RuntimePlatform.WSAPlayerX64)));
                Assert.That(V(RuntimePlatform.MetroPlayerARM), Is.EqualTo(V(RuntimePlatform.WSAPlayerARM)));
                Assert.That(V(RuntimePlatform.BB10Player), Is.EqualTo(V(RuntimePlatform.BlackBerryPlayer)));
                // two obsolete members share -1
                Assert.That(V(RuntimePlatform.CloudRendering), Is.EqualTo(-1));
                Assert.That(V(RuntimePlatform.GameCoreScarlett), Is.EqualTo(-1));
                // 6 and 14 are retired slots and must stay undefined
                Assert.That(Enum.IsDefined(typeof(RuntimePlatform), 6), Is.False);
                Assert.That(Enum.IsDefined(typeof(RuntimePlatform), 14), Is.False);
            });
        }

        // ---- TextureEnums (design §3.2, VT §14, inventory §A.4) ------------------------------------------------

        [Test]
        public void VRTextureUsage_MatchesUnity()
        {
            AssertEnum<VRTextureUsage>(flags: false,
                ("None", 0), ("OneEye", 1), ("TwoEyes", 2), ("DeviceSpecific", 3));
        }

        [Test]
        public void MeshTopology_MatchesUnityIncludingTheGapAtOne()
        {
            AssertEnum<MeshTopology>(flags: false,
                ("Triangles", 0), ("Quads", 2), ("Lines", 3), ("LineStrip", 4), ("Points", 5));
            Assert.That(Enum.IsDefined(typeof(MeshTopology), 1), Is.False, "slot 1 is Unity's removed quad strip");
        }

        [Test]
        public void SpriteMeshType_MatchesUnity()
        {
            AssertEnum<SpriteMeshType>(flags: false, ("FullRect", 0), ("Tight", 1));
        }

        [Test]
        public void GraphicsEnums_TextureSideMatchesSpecSection14()
        {
            // FilterMode / TextureWrapMode / RenderTextureReadWrite / CubemapFace and the two format enums live in
            // the sibling Enums/GraphicsEnums.cs (see the file-split note at the top of TextureEnums.cs). They are
            // part of the same §3.2 contract, so the values NowUI actually uses (inventory §A.4) are pinned here.
            Assert.Multiple(() =>
            {
                Assert.That(V(FilterMode.Point), Is.EqualTo(0));
                Assert.That(V(FilterMode.Bilinear), Is.EqualTo(1));
                Assert.That(V(FilterMode.Trilinear), Is.EqualTo(2));

                Assert.That(V(TextureWrapMode.Repeat), Is.EqualTo(0));
                Assert.That(V(TextureWrapMode.Clamp), Is.EqualTo(1));
                Assert.That(V(TextureWrapMode.Mirror), Is.EqualTo(2));
                Assert.That(V(TextureWrapMode.MirrorOnce), Is.EqualTo(3));

                Assert.That(V(TextureFormat.Alpha8), Is.EqualTo(1));
                Assert.That(V(TextureFormat.RGB24), Is.EqualTo(3));
                Assert.That(V(TextureFormat.RGBA32), Is.EqualTo(4));
                Assert.That(V(TextureFormat.ARGB32), Is.EqualTo(5));
                Assert.That(V(TextureFormat.RGBAHalf), Is.EqualTo(17));
                Assert.That(V(TextureFormat.RFloat), Is.EqualTo(18));
                Assert.That(V(TextureFormat.RGBAFloat), Is.EqualTo(20));
                Assert.That(V(TextureFormat.R8), Is.EqualTo(63));

                Assert.That(V(RenderTextureFormat.ARGB32), Is.EqualTo(0));
                Assert.That(V(RenderTextureFormat.ARGBHalf), Is.EqualTo(2));
                Assert.That(V(RenderTextureFormat.ARGBFloat), Is.EqualTo(11));
                Assert.That(V(RenderTextureFormat.RFloat), Is.EqualTo(14));
                Assert.That(V(RenderTextureFormat.RHalf), Is.EqualTo(15));
                Assert.That(V(RenderTextureFormat.R8), Is.EqualTo(16));
                Assert.That(V(RenderTextureFormat.RGB111110Float), Is.EqualTo(22), "note the gap at 21");
                Assert.That(Enum.IsDefined(typeof(RenderTextureFormat), 21), Is.False);

                Assert.That(V(RenderTextureReadWrite.Default), Is.EqualTo(0));
                Assert.That(V(RenderTextureReadWrite.Linear), Is.EqualTo(1));
                Assert.That(V(RenderTextureReadWrite.sRGB), Is.EqualTo(2));

                Assert.That(V(CubemapFace.Unknown), Is.EqualTo(-1));
                Assert.That(V(CubemapFace.PositiveX), Is.EqualTo(0));
                Assert.That(V(CubemapFace.NegativeZ), Is.EqualTo(5));

                Assert.That(V(ColorSpace.Uninitialized), Is.EqualTo(-1));
                Assert.That(V(ColorSpace.Gamma), Is.EqualTo(0));
                Assert.That(V(ColorSpace.Linear), Is.EqualTo(1));
            });
        }

        // ---- DataEnums (design §3.2, GC §2/§4.1/§4.3/§9) --------------------------------------------------------

        [Test]
        public void GradientMode_MatchesGcSection2()
        {
            AssertEnum<GradientMode>(flags: false, ("Blend", 0), ("Fixed", 1), ("PerceptualBlend", 2));
        }

        [Test]
        public void WrapMode_MatchesGcSection43_ClampAndOnceShareValueOne()
        {
            AssertEnum<WrapMode>(flags: false,
                ("Once", 1), ("Loop", 2), ("PingPong", 4), ("Default", 0), ("ClampForever", 8), ("Clamp", 1));
            Assert.Multiple(() =>
            {
                Assert.That(WrapMode.Clamp, Is.EqualTo(WrapMode.Once), "GC §4.3: Clamp and Once are the same value");
                // Looks like flags, is not attributed [Flags] in Unity — and the attribute is observable, because it
                // changes how an undefined combination formats.
                Assert.That(typeof(WrapMode).IsDefined(typeof(FlagsAttribute), false), Is.False);
                Assert.That(((WrapMode)6).ToString(), Is.EqualTo("6"), "no [Flags] => no 'Loop, PingPong' formatting");
            });
        }

        [Test]
        public void WeightedMode_MatchesGcSection41()
        {
            AssertEnum<WeightedMode>(flags: false, ("None", 0), ("In", 1), ("Out", 2), ("Both", 3));
            Assert.That(WeightedMode.Both, Is.EqualTo(WeightedMode.In | WeightedMode.Out));
        }

        [Test]
        public void LogType_MatchesUnity_SeverityDescending()
        {
            AssertEnum<LogType>(flags: false,
                ("Error", 0), ("Assert", 1), ("Warning", 2), ("Log", 3), ("Exception", 4));
            Assert.That(V(LogType.Error), Is.LessThan(V(LogType.Log)), "0 is the most severe, not the least");
        }

        [Test]
        public void LogOption_MatchesUnity()
        {
            AssertEnum<LogOption>(flags: false, ("None", 0), ("NoStacktrace", 1));
        }

        [Test]
        public void SystemLanguage_MatchesUnity_IncludingTheHugarianTypo()
        {
            AssertEnum<SystemLanguage>(flags: false,
                ("Afrikaans", 0), ("Arabic", 1), ("Basque", 2), ("Belarusian", 3), ("Bulgarian", 4),
                ("Catalan", 5), ("Chinese", 6), ("Czech", 7), ("Danish", 8), ("Dutch", 9),
                ("English", 10), ("Estonian", 11), ("Faroese", 12), ("Finnish", 13), ("French", 14),
                ("German", 15), ("Greek", 16), ("Hebrew", 17), ("Hugarian", 18), ("Icelandic", 19),
                ("Indonesian", 20), ("Italian", 21), ("Japanese", 22), ("Korean", 23), ("Latvian", 24),
                ("Lithuanian", 25), ("Norwegian", 26), ("Polish", 27), ("Portuguese", 28), ("Romanian", 29),
                ("Russian", 30), ("SerboCroatian", 31), ("Slovak", 32), ("Slovenian", 33), ("Spanish", 34),
                ("Swedish", 35), ("Thai", 36), ("Turkish", 37), ("Ukrainian", 38), ("Vietnamese", 39),
                ("ChineseSimplified", 40), ("ChineseTraditional", 41), ("Hindi", 42), ("Unknown", 43),
                ("Hungarian", 18));
            Assert.That(SystemLanguage.Hugarian, Is.EqualTo(SystemLanguage.Hungarian), "Unity's typo is an alias");
        }

        [Test]
        public void ScreenOrientation_MatchesDesignSubset()
        {
            AssertEnum<ScreenOrientation>(flags: false,
                ("Portrait", 1), ("PortraitUpsideDown", 2), ("LandscapeLeft", 3), ("LandscapeRight", 4),
                ("AutoRotation", 5));
            // consequence of omitting Unity's obsolete Unknown = 0: the default has no name
            Assert.That(Enum.IsDefined(typeof(ScreenOrientation), 0), Is.False);
        }

        [Test]
        public void RuntimeInitializeLoadType_MatchesUnity_NumberingIsNotExecutionOrder()
        {
            AssertEnum<RuntimeInitializeLoadType>(flags: false,
                ("AfterSceneLoad", 0), ("BeforeSceneLoad", 1), ("AfterAssembliesLoaded", 2),
                ("BeforeSplashScreen", 3), ("SubsystemRegistration", 4));
            Assert.That(V(RuntimeInitializeLoadType.SubsystemRegistration),
                Is.GreaterThan(V(RuntimeInitializeLoadType.AfterSceneLoad)),
                "SubsystemRegistration runs first but numbers last");
        }

        [Test]
        public void TouchScreenKeyboardType_MatchesGcSection9()
        {
            AssertEnum<TouchScreenKeyboardType>(flags: false,
                ("Default", 0), ("ASCIICapable", 1), ("NumbersAndPunctuation", 2), ("URL", 3), ("NumberPad", 4),
                ("PhonePad", 5), ("NamePhonePad", 6), ("EmailAddress", 7), ("NintendoNetworkAccount", 8),
                ("Social", 9), ("Search", 10), ("DecimalPad", 11), ("OneTimeCode", 12));
        }

        // ---- ImguiEnums (design §3.2, inventory §A.5) -----------------------------------------------------------

        [Test]
        public void EventType_MatchesUnity6000_4()
        {
            AssertEnum<EventType>(flags: false,
                ("MouseDown", 0), ("MouseUp", 1), ("MouseMove", 2), ("MouseDrag", 3), ("KeyDown", 4),
                ("KeyUp", 5), ("ScrollWheel", 6), ("Repaint", 7), ("Layout", 8), ("DragUpdated", 9),
                ("DragPerform", 10), ("Ignore", 11), ("Used", 12), ("ValidateCommand", 13), ("ExecuteCommand", 14),
                ("DragExited", 15), ("ContextClick", 16), ("MouseEnterWindow", 20), ("MouseLeaveWindow", 21),
                // Design §3.2 lists 80..85 for the touch block; Unity 6000.4 uses 30..35. The verified values win
                // (§3.2's own rule: "values are Unity's and are normative"). Reported to the lead.
                ("TouchDown", 30), ("TouchUp", 31), ("TouchMove", 32), ("TouchEnter", 33), ("TouchLeave", 34),
                ("TouchStationary", 35));
            Assert.Multiple(() =>
            {
                Assert.That(Enum.IsDefined(typeof(EventType), 17), Is.False, "17-19 are unused in Unity");
                Assert.That(Enum.IsDefined(typeof(EventType), 22), Is.False, "22-29 are unused in Unity");
            });
        }

        [Test]
        public void FocusType_MatchesUnity()
        {
            AssertEnum<FocusType>(flags: false, ("Native", 0), ("Keyboard", 1), ("Passive", 2));
        }

        [Test]
        public void ScaleMode_MatchesUnity()
        {
            AssertEnum<ScaleMode>(flags: false,
                ("StretchToFill", 0), ("ScaleAndCrop", 1), ("ScaleToFit", 2));
        }

        [Test]
        public void EventModifiers_MatchesUnity()
        {
            AssertEnum<EventModifiers>(flags: true,
                ("None", 0), ("Shift", 1), ("Control", 2), ("Alt", 4), ("Command", 8), ("Numeric", 16),
                ("CapsLock", 32), ("FunctionKey", 64));
        }

        [Test]
        public void TouchPhase_MatchesUnity_CanceledHasOneL()
        {
            AssertEnum<TouchPhase>(flags: false,
                ("Began", 0), ("Moved", 1), ("Stationary", 2), ("Ended", 3), ("Canceled", 4));
        }

        [Test]
        public void IMECompositionMode_MatchesUnity_DefaultIsAuto()
        {
            AssertEnum<IMECompositionMode>(flags: false, ("Auto", 0), ("On", 1), ("Off", 2));
            Assert.That(default(IMECompositionMode), Is.EqualTo(IMECompositionMode.Auto));
        }

        // ---- UnityEngine.Rendering (design §3.2, inventory §A.4) ------------------------------------------------

        [Test]
        public void TextureDimension_MatchesUnity_DefaultIsNoneNotUnknown()
        {
            AssertEnum<TextureDimension>(flags: false,
                ("Unknown", -1), ("None", 0), ("Any", 1), ("Tex2D", 2), ("Tex3D", 3), ("Cube", 4),
                ("Tex2DArray", 5), ("CubeArray", 6));
            Assert.That(default(TextureDimension), Is.EqualTo(TextureDimension.None));
        }

        [Test]
        public void IndexFormat_MatchesUnity()
        {
            AssertEnum<IndexFormat>(flags: false, ("UInt16", 0), ("UInt32", 1));
        }

        [Test]
        public void MeshUpdateFlags_MatchesDesignSubset()
        {
            AssertEnum<MeshUpdateFlags>(flags: true,
                ("Default", 0), ("DontValidateIndices", 1), ("DontResetBoneBounds", 2),
                ("DontNotifyMeshUsers", 4), ("DontRecalculateBounds", 8));
            // NowMesh's hot upload path passes exactly these three (inventory §A.4)
            Assert.That(V(MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers |
                          MeshUpdateFlags.DontRecalculateBounds), Is.EqualTo(13));
        }

        [Test]
        public void VertexAttribute_MatchesUnity_TexCoordsAreContiguous()
        {
            AssertEnum<VertexAttribute>(flags: false,
                ("Position", 0), ("Normal", 1), ("Tangent", 2), ("Color", 3),
                ("TexCoord0", 4), ("TexCoord1", 5), ("TexCoord2", 6), ("TexCoord3", 7),
                ("TexCoord4", 8), ("TexCoord5", 9), ("TexCoord6", 10), ("TexCoord7", 11),
                ("BlendWeight", 12), ("BlendIndices", 13));
            for (int channel = 0; channel <= 7; channel++)
                Assert.That(V(VertexAttribute.TexCoord0) + channel,
                    Is.EqualTo(Convert.ToInt32(Enum.Parse<VertexAttribute>("TexCoord" + channel))),
                    "a UV channel index must be addable to TexCoord0");
        }

        [Test]
        public void VertexAttributeFormat_MatchesUnity()
        {
            AssertEnum<VertexAttributeFormat>(flags: false,
                ("Float32", 0), ("Float16", 1), ("UNorm8", 2), ("SNorm8", 3), ("UNorm16", 4), ("SNorm16", 5),
                ("UInt8", 6), ("SInt8", 7), ("UInt16", 8), ("SInt16", 9), ("UInt32", 10), ("SInt32", 11));
        }

        [Test]
        public void BuiltinRenderTextureType_MatchesDesignSubset()
        {
            AssertEnum<BuiltinRenderTextureType>(flags: false,
                ("None", 0), ("CurrentActive", 1), ("CameraTarget", 2), ("Depth", 3), ("DepthNormals", 4),
                ("ResolvedDepth", 5));
        }

        [Test]
        public void GraphicsDeviceType_MatchesDesignSubset()
        {
            AssertEnum<GraphicsDeviceType>(flags: false,
                ("Direct3D11", 2), ("Null", 4), ("OpenGLES3", 11), ("Metal", 16), ("Direct3D12", 18),
                ("Vulkan", 21));
        }

        [Test]
        public void CompareFunction_MatchesUnity()
        {
            AssertEnum<CompareFunction>(flags: false,
                ("Disabled", 0), ("Never", 1), ("Less", 2), ("Equal", 3), ("LessEqual", 4), ("Greater", 5),
                ("NotEqual", 6), ("GreaterEqual", 7), ("Always", 8));
        }

        [Test]
        public void StencilOp_MatchesUnity()
        {
            AssertEnum<StencilOp>(flags: false,
                ("Keep", 0), ("Zero", 1), ("Replace", 2), ("IncrementSaturate", 3), ("DecrementSaturate", 4),
                ("Invert", 5), ("IncrementWrap", 6), ("DecrementWrap", 7));
        }

        [Test]
        public void ColorWriteMask_MatchesUnity_BitOrderIsReversed()
        {
            AssertEnum<ColorWriteMask>(flags: true,
                ("Alpha", 1), ("Blue", 2), ("Green", 4), ("Red", 8), ("All", 15));
            Assert.Multiple(() =>
            {
                Assert.That(V(ColorWriteMask.Alpha), Is.LessThan(V(ColorWriteMask.Red)), "Alpha is bit 0, Red is bit 3");
                Assert.That(ColorWriteMask.All, Is.EqualTo(ColorWriteMask.Red | ColorWriteMask.Green |
                                                           ColorWriteMask.Blue | ColorWriteMask.Alpha));
            });
        }

        [Test]
        public void ShadowCastingMode_MatchesUnity()
        {
            AssertEnum<ShadowCastingMode>(flags: false,
                ("Off", 0), ("On", 1), ("TwoSided", 2), ("ShadowsOnly", 3));
        }

        [Test]
        public void LightProbeUsage_MatchesUnity_ThreeIsUnused()
        {
            AssertEnum<LightProbeUsage>(flags: false,
                ("Off", 0), ("BlendProbes", 1), ("UseProxyVolume", 2), ("CustomProvided", 4));
            Assert.That(Enum.IsDefined(typeof(LightProbeUsage), 3), Is.False);
        }

        [Test]
        public void ReflectionProbeUsage_MatchesUnity()
        {
            AssertEnum<ReflectionProbeUsage>(flags: false,
                ("Off", 0), ("BlendProbes", 1), ("BlendProbesAndSkybox", 2), ("Simple", 3));
        }

        // ---- every enum U1 owns is public, int-backed and in Unity's namespace ----------------------------------

        [Test]
        public void AllU1Enums_ArePublicIntBackedAndInUnityNamespaces()
        {
            Type[] owned =
            {
                typeof(HideFlags), typeof(KeyCode), typeof(RuntimePlatform), typeof(VRTextureUsage),
                typeof(MeshTopology), typeof(SpriteMeshType), typeof(GradientMode), typeof(WrapMode),
                typeof(WeightedMode), typeof(LogType), typeof(LogOption), typeof(SystemLanguage),
                typeof(ScreenOrientation), typeof(RuntimeInitializeLoadType), typeof(TouchScreenKeyboardType),
                typeof(EventType), typeof(FocusType), typeof(ScaleMode), typeof(EventModifiers),
                typeof(TouchPhase), typeof(IMECompositionMode), typeof(TextureDimension), typeof(IndexFormat),
                typeof(MeshUpdateFlags), typeof(VertexAttribute), typeof(VertexAttributeFormat),
                typeof(BuiltinRenderTextureType), typeof(GraphicsDeviceType), typeof(CompareFunction),
                typeof(StencilOp), typeof(ColorWriteMask), typeof(ShadowCastingMode), typeof(LightProbeUsage),
                typeof(ReflectionProbeUsage),
            };
            Assert.Multiple(() =>
            {
                foreach (Type t in owned)
                {
                    Assert.That(t.IsPublic, Is.True, $"{t.FullName} must be public");
                    Assert.That(t.IsEnum, Is.True, $"{t.FullName} must be an enum");
                    Assert.That(Enum.GetUnderlyingType(t), Is.EqualTo(typeof(int)), $"{t.FullName} underlying type");
                    Assert.That(t.Namespace, Is.AnyOf("UnityEngine", "UnityEngine.Rendering"), t.FullName);
                }
            });
        }
    }
}
