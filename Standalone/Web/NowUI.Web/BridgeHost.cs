// The browser end of the bridge. Docs/Standalone/M3-Spec.md section 5.7.
//
// `?bridge=MODE` runs a JavaScript-authored UI instead of the C# feature gallery, through exactly one boundary
// crossing per frame. Everything else about the page - the resources, the WebGL2 backend, the input bridge, the
// clear - is unchanged; this file replaces one line of Program.Frame(), the line that draws.
//
// The frame ordering below is section 5.7's, complete:
//
//     Poll / BeginFrame / Drain / clear        unchanged, Program.cs
//     used = Record(...)                       ONE call out. JavaScript runs the author's draw function, ONCE,
//                                              and last frame's result table rides back in the same call.
//     if (used == NEED_MORE) grow and retry    inside BridgeRecorder.Record
//     Preamble(used)                           intern the new strings, validate (section 5.5)
//     using (Now.StartUI(dpr)) RunFrame()      the root scope, then RunMeasured (2 passes) or Area (1)
//     EndFrame                                 unchanged, Program.cs
//
// THE THING THIS ORDER EXISTS TO PREVENT. Record is outside Now.StartUI and outside RunMeasured. Design B's frame
// diagram put it inside RunMeasured's delegate; RunMeasured calls its delegate TWICE (NowLayout.cs:1578-1590), so
// the author's JavaScript draw function would run twice per frame - reading state twice, firing every handler
// twice, and paying the JavaScript cost twice - and nothing about the resulting UI would look wrong. Only the
// second click would.
//
// The branch itself lives in BridgeReplay.RunFrame rather than here, so that the tests in
// Standalone/NowUI.Bridge.Tests exercise the real ordering instead of a re-implementation of it. What this file
// owns is everything around it: the crossing, the refusal, and the report.

using System;
using System.Threading.Tasks;
using NowUI;
using NowUI.Bridge;

namespace NowUI.Web
{
    /// <summary>Drives one JavaScript-authored frame per <c>requestAnimationFrame</c>.</summary>
    internal static class BridgeHost
    {
        /// <summary>
        /// The ground a JavaScript-authored page is cleared to: the theme's own Background token.
        /// </summary>
        /// <remarks>
        /// Not a cosmetic choice, and measured rather than assumed. The gallery paints its own panels over
        /// <c>WebApp.k_Ground</c>, so nothing there depends on the clear colour matching the theme; a JavaScript
        /// app paints nothing of its own and owns the whole surface. The first capture of W2's hello frame put the
        /// theme's near-black Text colour on that dark ground and the word was there but unreadable - correct, and
        /// useless as evidence. The theme decides both colours, so letting it decide the ground too is what makes
        /// them agree.
        /// </remarks>
        /// <remarks>
        /// <para>It is the ground the LAST frame ended on, not the one the current theme reports right now, and
        /// that one word is the whole of a defect. The clear runs BEFORE the author's draw function
        /// (WebApp.Frame), so at clear time an <c>ui.theme('dark')</c> scope has not opened yet and
        /// <c>NowTheme.themeAsset</c> is still whatever the frame ended on last time. Reading it live therefore
        /// cleared to the DEFAULT theme's background forever, while everything inside the scope drew in the dark
        /// theme's colours: light text on a light ground, present and invisible. Text inside a
        /// <c>ui.card</c> survived, because a card paints its own background, which is exactly the shape that
        /// made it look like a text bug rather than a clear bug.</para>
        /// <para>Sampling at the end of the frame instead costs one frame of lag when the author SWITCHES theme,
        /// which is invisible at 60 Hz and self-correcting, and costs nothing at all in the ordinary case where
        /// the theme is the same every frame.</para>
        /// </remarks>
        internal static UnityEngine.Color ground => s_Ground;

        /// <summary>Seeded from the default theme so frame 1 clears to something sane before any frame has run.</summary>
        private static UnityEngine.Color s_Ground = NowTheme.themeAsset.GetColor(NowColorToken.Background);

        private static BridgeRecorder s_Recorder;
        private static BridgeReplay s_Replay;
        private static int s_Frame;
        private static bool s_Failed;
        private static string s_Mode;
        private static string s_FirstRefusal;

        /// <summary>True once <see cref="StartAsync"/> has brought the bridge up.</summary>
        internal static bool active => s_Replay != null;

        /// <summary>
        /// Imports the two JavaScript modules and installs the draw function. Called after the input bridge is up,
        /// because the first frame runs immediately afterwards and a draw function is what it needs.
        /// </summary>
        internal static async Task StartAsync(string mode)
        {
            s_Mode = mode;

            await JSHostImport(BridgeInterop.BridgeModuleName, BridgeInterop.BridgeModulePath).ConfigureAwait(false);
            await JSHostImport(BridgeInterop.AppModuleName, BridgeInterop.AppModulePath).ConfigureAwait(false);

            string installed = BridgeInterop.Bridge.Install(mode);

            s_Recorder = new BridgeRecorder();
            s_Replay = new BridgeReplay(s_Recorder, message => BrowserInterop.Log(1, message));
            s_Frame = 0;

            BrowserInterop.Log(0, "[NowUI] bridge up, mode '" + installed + "', surface hash 0x" +
                Abi.SurfaceHash.ToString("X8") + ".");
        }

        /// <summary>
        /// W6's <c>?app=NAME</c>: brings the bridge up over an author's own JavaScript file, served as-is from
        /// <c>wwwroot/NAME.js</c>. Section 1's application is <c>?app=app</c>.
        /// </summary>
        /// <remarks>
        /// There is no <c>install</c> call here, and that is the difference from <see cref="StartAsync"/> rather
        /// than an omission. An author's file has no entry point to call: it imports <c>nowui.js</c> and calls
        /// <c>start(draw)</c> at module scope, so IMPORTING it is what installs the draw function. That is the
        /// shape section 1 promises - "app.js is served as-is and nowui.js is a static file beside it" - and it
        /// only works because both files reach the same <c>bridge.js</c> module instance: the browser's module
        /// registry is keyed by resolved URL, and <c>../nowui/bridge.js</c> from the runtime's own script and
        /// <c>./nowui/nowui.js -> ./bridge.js</c> from the author's file resolve to the same one.
        /// </remarks>
        internal static async Task StartAppAsync(string app)
        {
            string name = Sanitise(app);

            s_Mode = "app:" + name;

            await JSHostImport(BridgeInterop.BridgeModuleName, BridgeInterop.BridgeModulePath).ConfigureAwait(false);
            await JSHostImport(BridgeInterop.AuthorAppModuleName, "../" + name + ".js").ConfigureAwait(false);

            s_Recorder = new BridgeRecorder();
            s_Replay = new BridgeReplay(s_Recorder, message => BrowserInterop.Log(1, message));
            s_Frame = 0;

            BrowserInterop.Log(0, "[NowUI] bridge up on wwwroot/" + name + ".js, surface hash 0x" +
                Abi.SurfaceHash.ToString("X8") + ".");
        }

        /// <summary>
        /// A query parameter becomes a URL here, so it is restricted to a bare file name rather than trusted.
        /// Nothing in <c>wwwroot</c> is secret and the whole directory is already served, so this is about
        /// producing a comprehensible error for a typo rather than about containment.
        /// </summary>
        private static string Sanitise(string app)
        {
            if (string.IsNullOrEmpty(app))
                throw new ArgumentException("?app= needs a file name: ?app=app serves wwwroot/app.js.");

            foreach (char c in app)
            {
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                    throw new ArgumentException(
                        "?app=" + app + " is not a file name. It may hold letters, digits, '-' and '_', and it " +
                        "names a file directly inside wwwroot: ?app=app serves wwwroot/app.js.");
            }

            return app;
        }

        private static Task JSHostImport(string moduleName, string modulePath)
        {
            return System.Runtime.InteropServices.JavaScript.JSHost.ImportAsync(moduleName, modulePath);
        }

        /// <summary>One bridge frame, called from <c>WebApp.Frame</c> in place of the gallery's draw.</summary>
        internal static void Frame(float devicePixelRatio)
        {
            if (s_Failed)
                return;

            ++s_Frame;

            int used;
            try
            {
                // ONE crossing. JavaScript runs the author's draw function inside it, and both the op stream and
                // last frame's result table move in the same call.
                used = s_Recorder.Record(BridgeInterop.Bridge.Record, s_Frame);

                // Everything below the validator is unreachable from a malformed frame, which is the whole of
                // "NowUI is never touched": Preamble throws before Now.StartUI is called.
                s_Recorder.Preamble(used);
            }
            catch (NowBridgeProtocolException e)
            {
                // Latched, and reported once. A malformed buffer is malformed every frame, and sixty identical
                // slot offsets a second would bury the first one - which is the only useful one.
                s_Failed = true;
                s_FirstRefusal = e.Message;
                BrowserInterop.Log(2, "[NowUI] " + e.Message);
                Report();
                return;
            }

            using (Now.StartUI(devicePixelRatio))
            {
                s_Replay.screen = Now.screenMask;

                // Section 5.7's two branches, both of them inside RunFrame: exactLayout ? RunMeasured : Area.
                // The result table is written by the last pass and sealed when RunFrame returns, so the table
                // that crosses at the START of the next frame describes the pixels drawn at the end of this one.
                s_Replay.RunFrame();

                // The replay recorded this while the author's theme scope was still open; it closes before
                // RunFrame returns, so sampling NowTheme here would read the default back. See `ground`.
                s_Ground = s_Replay.frameGround ?? NowTheme.themeAsset.GetColor(NowColorToken.Background);
            }

            // The report is what a headless run reads. Written every frame while the count is small, so a capture
            // that lands on frame 1 and one that lands on frame 40 both find it, and then left alone.
            if (s_Frame <= 3 || s_Frame % 60 == 0)
                Report();
        }

        /// <summary>What the last frame did, on the page and in the console, for a headless run to read.</summary>
        private static void Report()
        {
            string text =
                "NowUI M3 W2-W6 - the op stream, identity, the frame, the results and the surface\n" +
                "===============================================================================\n\n" +
                "mode          " + s_Mode + "\n" +
                "frame         " + s_Frame + "\n" +
                "surface hash  0x" + Abi.SurfaceHash.ToString("X8") + " (managed)\n";

            if (s_FirstRefusal != null)
            {
                text +=
                    "\nTHE FRAME WAS REFUSED BY THE VALIDATOR, AND NOWUI WAS NEVER TOUCHED.\n\n" +
                    "  " + s_FirstRefusal + "\n\n" +
                    "  ops decoded this session: " + s_Replay.decodedOps + "\n" +
                    "  controls drawn:           " + s_Replay.decodedControls + "\n";
            }
            else
            {
                text +=
                    "slots crossed " + s_Recorder.used + "\n" +
                    "text bytes    " + s_Recorder.frame[Abi.HdrTextBytes] + "\n" +
                    "interned      " + s_Recorder.internedCount + "\n" +
                    "exactLayout   " + s_Recorder.exactLayout + "\n" +
                    "faulted       " + s_Recorder.faulted + "\n" +
                    "ops decoded   " + s_Replay.decodedOps + "\n" +
                    "controls      " + s_Replay.decodedControls + "\n" +

                    // W4's acceptance, on the page: two passes under exactLayout and one without it. The author's
                    // draw function is not in this count and never will be - it ran in JavaScript, once, before
                    // Now.StartUI was opened.
                    "replay passes " + s_Replay.passes + "\n" +

                    // W5's, likewise. `suppressed` is the number of result writes the measure pass refused; under
                    // exactLayout it is one per record and without it is zero, and that is the whole of
                    // "a result written during a measure pass never reaches JavaScript".
                    "results       " + s_Replay.results.recordCount + " record(s), " +
                    s_Replay.results.sealedSlots + " slot(s), " +
                    s_Replay.results.suppressedWrites + " suppressed\n";
            }

            text += "\njavascript    " + BridgeInterop.Bridge.Status() + "\n";

            BridgeInterop.Bridge.Report(text);
        }
    }
}
