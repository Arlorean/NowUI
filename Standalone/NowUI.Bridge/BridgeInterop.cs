// W1 - the transport spike. Docs/Standalone/M3-Spec.md section 5.1 and section 9 (W1).
//
// The whole ABI rests on one assumption about the JavaScript-to-WebAssembly boundary: that a
// [JSMarshalAs<JSType.MemoryView>] Span<T> parameter gives JavaScript an object it can WRITE INTO with set(), and
// READ OUT OF with slice(), for the duration of the call. All three M3 designs assumed instead that JavaScript gets
// a zero-copy typed array it may retain; the repository's own comments say otherwise
// (WebGL2Backend.cs:1941-1945, nowui-gl.js:3668-3673), and section 5.1 is built around one copy in each direction.
//
// This file proves the mechanism and measures it. It declares nothing the final bridge will not need: the shapes
// here are the shapes of section 5.1's Record(), minus the op stream that W2 puts inside them.
//
// The double[] fallback that WebInput.Drain already proves (WebInput.cs:401-405) is declared here too, and is
// measured either way, so that section 5.1's cost table has both numbers rather than an adjective.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading.Tasks;

namespace NowUI.Bridge
{
    /// <summary>
    /// The JavaScript side of the bridge, in one place so the boundary crossings can be counted by reading it -
    /// the same convention <c>WebGL2Backend.Interop</c> and <c>WebInput.Interop</c> follow.
    /// </summary>
    public static partial class BridgeInterop
    {
        /// <summary>The module name the <c>[JSImport]</c>s below bind against.</summary>
        public const string ModuleName = "nowui-spike";

        /// <summary>
        /// Where <see cref="TransportSpike.RunAsync"/> looks for <c>spike.js</c>.
        /// </summary>
        /// <remarks>
        /// One level up, for the reason <c>WebGL2Backend.DefaultModulePath</c> records: <c>JSHost.ImportAsync</c>
        /// resolves a relative specifier against the runtime's own script under <c>_framework/</c>, so
        /// <c>"./nowui/spike.js"</c> would ask for <c>_framework/nowui/spike.js</c> and 404.
        /// </remarks>
        public const string DefaultModulePath = "../nowui/spike.js";

        /// <summary>Every call the spike makes across the boundary. Nine, and none of them retain anything.</summary>
        internal static partial class Interop
        {
            /// <summary>
            /// What a <c>MemoryView</c> actually is, described by JavaScript: constructor name, the members that
            /// exist on it, and whether <c>set</c> is callable. The evidence for W1's first half.
            /// </summary>
            [JSImport("probe", ModuleName)]
            public static partial string Probe(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<byte> opsText);

            /// <summary>Crosses the boundary and returns. Isolates marshalling cost from copy cost.</summary>
            [JSImport("noop", ModuleName)]
            public static partial int Noop(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<int> results,
                int count);

            /// <summary><c>ops.set(...)</c> only - the JavaScript-to-wasm direction on its own.</summary>
            [JSImport("setOnly", ModuleName)]
            public static partial int SetOnly(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<int> results,
                int count);

            /// <summary><c>results.slice(...)</c> only - the wasm-to-JavaScript direction on its own.</summary>
            [JSImport("sliceOnly", ModuleName)]
            public static partial int SliceOnly(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<int> results,
                int count);

            /// <summary>Both, in the order section 5.1's <c>record</c> does them. This is the per-frame number.</summary>
            [JSImport("copy", ModuleName)]
            public static partial int Copy(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<int> results,
                int count);

            /// <summary><c>results.copyTo(...)</c> only - the same direction as slice(), with no allocation.</summary>
            [JSImport("copyToOnly", ModuleName)]
            public static partial int CopyToOnly(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<int> results,
                int count);

            /// <summary><c>copyTo</c> + <c>set</c> - section 5.1's frame with the readback allocation removed.</summary>
            [JSImport("copyFast", ModuleName)]
            public static partial int CopyFast(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<int> results,
                int count);

            /// <summary>A frame at section 5.1's own predicted sizes: ~7 KB out, ~600 bytes back, in one call.</summary>
            [JSImport("frame", ModuleName)]
            public static partial int Frame(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<int> results,
                int opsCount,
                int resultCount);

            /// <summary>
            /// The correctness half: JavaScript slices <paramref name="results"/> out, byte-compares it against the
            /// pattern it regenerates from <paramref name="seed"/>, and sets the bytes it read back into
            /// <paramref name="ops"/>. Returns 0 when its own comparison was clean.
            /// </summary>
            [JSImport("roundTrip", ModuleName)]
            public static partial int RoundTrip(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<int> results,
                int count,
                int seed,
                int corruptAt);

            /// <summary>
            /// The <c>NEED_MORE</c> path of section 5.1: a two-element <c>set()</c> into a large view, to prove a
            /// partial write lands where it is asked to and leaves every slot after it alone.
            /// </summary>
            [JSImport("partialSet", ModuleName)]
            public static partial int PartialSet(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                int a,
                int b,
                int offset);

            /// <summary>The fallback, outbound: JavaScript builds an array, the marshaller converts it element-wise.</summary>
            [JSImport("fallbackOut", ModuleName)]
            [return: JSMarshalAs<JSType.Array<JSType.Number>>]
            public static partial double[] FallbackOut(int count);

            /// <summary>The fallback, inbound.</summary>
            [JSImport("fallbackIn", ModuleName)]
            public static partial int FallbackIn(
                [JSMarshalAs<JSType.Array<JSType.Number>>] double[] data);

            /// <summary>Puts the report where a headless <c>--dump-dom</c> can read it.</summary>
            [JSImport("report", ModuleName)]
            public static partial void Report(string text);
        }

        // ------------------------------------------------------------------------------------------------ W2
        //
        // The real bridge. W1 above was a measurement; everything below is the ABI of section 5, and it is one
        // function wide: `Record` is the only crossing the bridge adds per frame, and that number is constant -
        // it does not grow with the number of controls, the depth of the UI, or the number of results read.

        /// <summary>The module <c>wwwroot/nowui/bridge.js</c> registers as.</summary>
        public const string BridgeModuleName = "nowui-bridge";

        /// <summary>
        /// Where <c>bridge.js</c> is fetched from. One level up, for the reason
        /// <see cref="DefaultModulePath"/> records: <c>JSHost.ImportAsync</c> resolves a relative specifier
        /// against the runtime's own script under <c>_framework/</c>.
        /// </summary>
        public const string BridgeModulePath = "../nowui/bridge.js";

        /// <summary>The module holding the draw function, which W6 replaces with <c>wwwroot/app.js</c>.</summary>
        public const string AppModuleName = "nowui-app";

        public const string AppModulePath = "../nowui/app.stub.js";

        /// <summary>
        /// W6. The module name an author's own file registers as under <c>?app=NAME</c>. It has no
        /// <c>[JSImport]</c> against it and never will: an application does not export an entry point, it calls
        /// <c>start</c> at module scope (section 1), so importing it IS running it. The name exists only because
        /// <c>JSHost.ImportAsync</c> requires one.
        /// </summary>
        public const string AuthorAppModuleName = "nowui-author-app";

        /// <summary>
        /// Every call the bridge makes across the boundary. Four, and only the first is per-frame.
        /// </summary>
        /// <remarks>
        /// Public, unlike W1's <see cref="Interop"/>: the browser host owns the frame ordering of section 5.7 and
        /// therefore has to be able to call <c>Record</c>. Counting the crossings by reading one file only works
        /// if they all live in it.
        /// </remarks>
        public static partial class Bridge
        {
            /// <summary>
            /// Section 5.1. JavaScript runs the author's draw function and <c>set()</c>s the recorded op stream
            /// and its string bytes into the two managed views; the result table rides back in the same call, so
            /// it costs no crossing of its own. Returns the slot count, or <see cref="Abi.NeedMore"/> after
            /// writing the two sizes the managed buffers must grow to into the first two slots.
            /// </summary>
            /// <remarks>
            /// Nothing is retained across the call on either side, which is what makes <c>Span&lt;T&gt;</c>
            /// correct here: a <c>MemoryView</c> is valid only for the duration of the call
            /// (<c>nowui-gl.js:3668-3673</c>), and W1 measured that both <c>set()</c> and <c>slice()</c> work
            /// on one.
            /// </remarks>
            [JSImport("record", BridgeModuleName)]
            public static partial int Record(
                [JSMarshalAs<JSType.MemoryView>] Span<int> ops,
                [JSMarshalAs<JSType.MemoryView>] Span<byte> opsText,
                [JSMarshalAs<JSType.MemoryView>] Span<int> results,
                [JSMarshalAs<JSType.MemoryView>] Span<byte> resultsText,
                int resultSlots,
                int frame);

            /// <summary>What the last frame did, as JSON. The half of the evidence that is not pixels.</summary>
            [JSImport("status", BridgeModuleName)]
            public static partial string Status();

            /// <summary>Puts a report where a headless <c>--dump-dom</c> can read it.</summary>
            [JSImport("report", BridgeModuleName)]
            public static partial void Report(string text);

            /// <summary>Installs the draw function for the requested <c>?bridge=</c> mode.</summary>
            [JSImport("install", AppModuleName)]
            public static partial string Install(string mode);
        }
    }

    /// <summary>
    /// W1's acceptance run: a green byte-comparison round trip at 1 MB, and a measured per-frame copy cost at 8 KB
    /// and at 1 MB, against the <c>double[]</c> fallback measured the same way.
    /// </summary>
    public static class TransportSpike
    {
        /// <summary>8 KB of ops - a realistic frame by section 5.1's own estimate ("~7 KB out").</summary>
        private const int k_Small = 8 * 1024 / 4;

        /// <summary>1 MB - W1's stated stress size.</summary>
        private const int k_Large = 1024 * 1024 / 4;

        /// <summary>How long each timing loop is asked to run before its average is trusted.</summary>
        private const double k_TargetMs = 250d;

        /// <summary>
        /// Imports <c>spike.js</c>, runs every check and - when <paramref name="measure"/> - every measurement,
        /// and returns the report.
        /// </summary>
        /// <param name="measure">
        /// False runs the correctness half only. That mode exists because of a browser fact that cost an hour here
        /// and is worth writing down: under <c>--virtual-time-budget</c>, which is the only way a headless
        /// <c>--dump-dom</c> or <c>--screenshot</c> can wait for an asynchronous page, Chrome FREEZES the clock for
        /// the duration of synchronous JavaScript. <c>Date.now()</c> and <c>performance.now()</c> both return the
        /// same value before and after a 300 ms busy loop - verified with a standalone probe page, which hung
        /// forever on `while (Date.now() &lt; end)`. Every timing in section 4 would read zero, and the adaptive
        /// loop below would spin to its iteration cap. So the correctness half is what a fresh-profile headless run
        /// verifies, and the measurements are taken in a browser with a real clock.
        /// </param>
        public static async Task<string> RunAsync(bool measure = true, string modulePath = BridgeInterop.DefaultModulePath)
        {
            await JSHost.ImportAsync(BridgeInterop.ModuleName, modulePath).ConfigureAwait(false);

            var report = new StringBuilder();
            report.Append("NowUI M3 W1 - transport spike\n");
            report.Append("=============================\n\n");

            // ---- 1. What a MemoryView is, according to JavaScript ------------------------------------------
            int[] probeOps = new int[16];
            byte[] probeText = new byte[16];
            string shape = BridgeInterop.Interop.Probe(probeOps.AsSpan(), probeText.AsSpan());
            report.Append("[1] MemoryView shape, as JavaScript sees it\n");
            report.Append("    ").Append(shape.Replace("\n", "\n    ")).Append('\n');

            bool setWorks = shape.Contains("setWrote=true");
            report.Append("    VERDICT: MemoryView.set() on a [JSImport] Span<int> parameter is ");
            report.Append(setWorks ? "AVAILABLE AND EFFECTIVE" : "NOT USABLE - the double[] fallback applies");
            report.Append(".\n");
            report.Append("    probeOps after set: ");
            report.Append(probeOps[0]).Append(',').Append(probeOps[1]).Append(',').Append(probeOps[2]);
            report.Append(" (expected 111,222,333)\n");
            report.Append("    probeText after set: ");
            report.Append(probeText[0]).Append(',').Append(probeText[1]).Append(',').Append(probeText[2]);
            report.Append(" (expected 78,79,87 = 'NOW')\n\n");

            // ---- 2. The NEED_MORE partial-write path -------------------------------------------------------
            int[] partial = new int[k_Small];
            for (int i = 0; i < partial.Length; ++i) partial[i] = -7;
            int partialRc = BridgeInterop.Interop.PartialSet(partial.AsSpan(), 4242, 8484, 0);
            bool partialOk = partialRc == 0 && partial[0] == 4242 && partial[1] == 8484 && partial[2] == -7
                             && partial[partial.Length - 1] == -7;
            report.Append("[2] Partial set() - section 5.1's NEED_MORE path\n");
            report.Append("    two ints into a 2048-slot view: slot0=").Append(partial[0]);
            report.Append(" slot1=").Append(partial[1]);
            report.Append(" slot2=").Append(partial[2]);
            report.Append(" slot2047=").Append(partial[partial.Length - 1]);
            report.Append("  -> ").Append(partialOk ? "PASS" : "FAIL").Append('\n');

            // set() with a target offset, which the generated recorder may want later.
            int offsetRc = BridgeInterop.Interop.PartialSet(partial.AsSpan(), 111, 222, 5);
            bool offsetOk = offsetRc == 0 && partial[5] == 111 && partial[6] == 222;
            report.Append("    set(source, targetOffset=5): slot5=").Append(partial[5]);
            report.Append(" slot6=").Append(partial[6]);
            report.Append("  -> ").Append(offsetOk ? "PASS" : (offsetRc != 0 ? "UNSUPPORTED (rc=" + offsetRc + ")" : "FAIL"));
            report.Append("\n\n");

            // ---- 3. The byte-comparison round trip ---------------------------------------------------------
            report.Append("[3] Round trip, byte-compared at both ends\n");
            report.Append(RoundTripCheck(k_Small, "8 KB   ", 0x5EEDu));
            report.Append(RoundTripCheck(k_Large, "1 MB   ", 0xC0FFEEu));
            report.Append(RoundTripCheck(k_Large, "1 MB #2", 0xDEADBEEFu));
            report.Append(RoundTripCheck(k_Large, "1 MB #3", 0xC0FFEEu, corruptAt: 700_001));
            report.Append('\n');

            // ---- 4. The measurements -----------------------------------------------------------------------
            if (!measure)
            {
                report.Append("[4] Measurements SKIPPED (?spike=verify). Chrome's --virtual-time-budget freezes the\n");
                report.Append("    clock during synchronous JavaScript, so every figure would read zero. This mode\n");
                report.Append("    is the one a fresh-profile headless run uses; the numbers come from ?spike=1 in\n");
                report.Append("    a browser with a real clock.\n");
            }
            else
            {
                report.Append("[4] Per-frame cost. Each figure is one boundary crossing, averaged over the\n");
                report.Append("    iteration count shown. Timed managed-side with Stopwatch, so it includes\n");
                report.Append("    everything a real frame pays: marshalling, the call, and the copies.\n\n");
                report.Append("    size    what                                 ms/call     MB/s   iters\n");
                report.Append("    ------  -----------------------------------  ---------  ------  -----\n");
                report.Append(MeasureSize(k_Small, "8 KB"));
                report.Append(MeasureSize(k_Large, "1 MB"));
                report.Append(MeasureFrame());

                report.Append("    'harness' is an empty delegate call, measured the same way, so the cost of\n");
                report.Append("    the measurement loop itself is visible rather than folded into the figures.\n");
                report.Append("    'noop' crosses the boundary and returns; copy cost is copy minus noop.\n");
                report.Append("    A figure marked FROZEN means the clock did not advance - see RunAsync's remarks.\n");
            }

            string text = report.ToString();
            BridgeInterop.Interop.Report(text);
            return text;
        }

        // ------------------------------------------------------------------------------------------- round trip

        /// <summary>
        /// Fills the results buffer with a seeded pattern, has JavaScript slice it out, byte-compare it against the
        /// same pattern regenerated in JavaScript, and set the bytes it read straight back into the ops buffer;
        /// then byte-compares the ops buffer here. Both directions, both ends, no shortcut through int equality.
        /// </summary>
        private static string RoundTripCheck(int count, string label, uint seed, int corruptAt = -1)
        {
            int[] results = new int[count];
            int[] ops = new int[count];
            Fill(results, count, seed);

            // Poison the destination so "it was already right" cannot pass.
            for (int i = 0; i < count; ++i) ops[i] = unchecked((int)0xA5A5A5A5);

            int rc = BridgeInterop.Interop.RoundTrip(
                ops.AsSpan(), results.AsSpan(), count, unchecked((int)seed), corruptAt);

            ReadOnlySpan<byte> got = MemoryMarshal.AsBytes(new ReadOnlySpan<int>(ops, 0, count));
            ReadOnlySpan<byte> want = MemoryMarshal.AsBytes(new ReadOnlySpan<int>(results, 0, count));

            int firstDiff = -1;
            for (int i = 0; i < want.Length; ++i)
            {
                if (got[i] != want[i]) { firstDiff = i; break; }
            }

            bool green = rc == 0 && firstDiff < 0;

            // A corrupted run is expected to come back FAIL; reporting it as a pass would mean the comparator is
            // the thing that is broken.
            bool expectedGreen = corruptAt < 0;
            bool asExpected = green == expectedGreen && (corruptAt < 0 || firstDiff == corruptAt);

            var line = new StringBuilder();
            line.Append("    ").Append(label).Append("  ").Append(count).Append(" ints, ");
            line.Append(want.Length).Append(" bytes  seed 0x").Append(seed.ToString("X8"));
            line.Append("  -> ").Append(green ? "PASS" : "FAIL");
            if (rc != 0) line.Append(" (JS side reported ").Append(rc).Append(')');
            if (firstDiff >= 0) line.Append(" (first differing byte at ").Append(firstDiff).Append(')');
            if (corruptAt >= 0)
            {
                line.Append("  [negative control: FAIL at byte ").Append(corruptAt).Append(" expected -> ");
                line.Append(asExpected ? "CORRECT" : "COMPARATOR IS BROKEN").Append(']');
            }
            line.Append('\n');
            return line.ToString();
        }

        /// <summary>The pattern. An LCG, so JavaScript regenerates it exactly with Math.imul and no shared data.</summary>
        private static void Fill(int[] buffer, int count, uint seed)
        {
            uint x = seed;
            for (int i = 0; i < count; ++i)
            {
                x = unchecked(x * 1664525u + 1013904223u);
                buffer[i] = unchecked((int)x);
            }
        }

        // ------------------------------------------------------------------------------------------ measurement

        private static string MeasureSize(int count, string label)
        {
            int bytes = count * 4;
            int[] ops = new int[count];
            int[] results = new int[count];
            Fill(results, count, 0x1234u);

            var rows = new StringBuilder();

            Measurement harness = Measure(static () => { }, 60d);
            Measurement noop = Measure(() => BridgeInterop.Interop.Noop(ops.AsSpan(), results.AsSpan(), count));
            Measurement setOnly = Measure(() => BridgeInterop.Interop.SetOnly(ops.AsSpan(), results.AsSpan(), count));
            Measurement sliceOnly = Measure(() => BridgeInterop.Interop.SliceOnly(ops.AsSpan(), results.AsSpan(), count));
            Measurement copyToOnly = Measure(() => BridgeInterop.Interop.CopyToOnly(ops.AsSpan(), results.AsSpan(), count));
            Measurement copy = Measure(() => BridgeInterop.Interop.Copy(ops.AsSpan(), results.AsSpan(), count));
            Measurement copyFast = Measure(() => BridgeInterop.Interop.CopyFast(ops.AsSpan(), results.AsSpan(), count));

            // The fallback, at the same payload: `count` numbers each way. A double[] carries the same int values
            // in twice the bytes, which is part of what it costs.
            double[] fallbackBuffer = new double[count];
            for (int i = 0; i < count; ++i) fallbackBuffer[i] = results[i];

            Measurement fbOut = Measure(() => BridgeInterop.Interop.FallbackOut(count), 120d);
            Measurement fbIn = Measure(() => BridgeInterop.Interop.FallbackIn(fallbackBuffer), 120d);

            rows.Append(Row(label, "harness (empty delegate)", harness, 0));
            rows.Append(Row(label, "noop crossing, no copy", noop, 0));
            rows.Append(Row(label, "set() only  JS -> wasm", setOnly, bytes));
            rows.Append(Row(label, "slice() only  wasm -> JS", sliceOnly, bytes));
            rows.Append(Row(label, "copyTo() only  wasm -> JS", copyToOnly, bytes));
            rows.Append(Row(label, "copy: set + slice  [5.1 AS WRITTEN]", copy, bytes * 2));
            rows.Append(Row(label, "copy: set + copyTo  [RECOMMENDED]", copyFast, bytes * 2));
            rows.Append(Row(label, "fallback double[] out  JS -> wasm", fbOut, bytes));
            rows.Append(Row(label, "fallback double[] in  wasm -> JS", fbIn, bytes));

            double copyOnly = copy.MsPerCall - noop.MsPerCall;
            rows.Append("    ").Append(label.PadRight(6)).Append("  ");
            rows.Append("(copy minus noop = ").Append(copyOnly.ToString("F6")).Append(" ms of actual memcpy)");
            rows.Append('\n');
            rows.Append('\n');
            return rows.ToString();
        }

        /// <summary>
        /// The figure section 5.1 actually needs: one crossing carrying the payload it predicts for a real frame -
        /// "~7 KB out and ~600 bytes back". The two rows above are a stress test and a unit cost; this is the one
        /// that answers "what does the bridge cost per frame".
        /// </summary>
        private static string MeasureFrame()
        {
            const int opsCount = 7 * 1024 / 4;      // ~7 KB of ops
            const int resultCount = 600 / 4;        // ~600 bytes of results

            int[] ops = new int[opsCount];
            int[] results = new int[resultCount];
            Fill(results, resultCount, 0x77u);

            Measurement m = Measure(() =>
                BridgeInterop.Interop.Frame(ops.AsSpan(), results.AsSpan(), opsCount, resultCount));

            var rows = new StringBuilder();
            rows.Append(Row("frame", "7 KB out + 600 B back, one call", m, opsCount * 4 + resultCount * 4));
            rows.Append("    frame   (that is ").Append((m.MsPerCall / 16.667d * 100d).ToString("F3"));
            rows.Append("% of a 60 Hz frame budget)\n\n");
            return rows.ToString();
        }

        private static string Row(string size, string what, Measurement m, int bytes)
        {
            var line = new StringBuilder();
            line.Append("    ").Append(size.PadRight(6)).Append("  ").Append(what.PadRight(35)).Append("  ");
            line.Append((m.Frozen ? "FROZEN" : m.MsPerCall.ToString("F6")).PadLeft(9)).Append("  ");
            if (bytes > 0 && m.MsPerCall > 0d && !m.Frozen)
            {
                double mbPerSecond = bytes / (m.MsPerCall / 1000d) / (1024d * 1024d);
                line.Append(mbPerSecond.ToString("F0").PadLeft(6));
            }
            else
            {
                line.Append("     -");
            }
            line.Append("  ").Append(m.Iterations.ToString().PadLeft(5)).Append('\n');
            return line.ToString();
        }

        private readonly struct Measurement
        {
            public Measurement(double msPerCall, int iterations, bool frozen)
            {
                MsPerCall = msPerCall;
                Iterations = iterations;
                Frozen = frozen;
            }

            public double MsPerCall { get; }
            public int Iterations { get; }

            /// <summary>The loop hit its cap with the clock still reading zero. The figure means nothing.</summary>
            public bool Frozen { get; }
        }

        /// <summary>
        /// Doubles the iteration count until the loop runs long enough to be above the browser's clock resolution,
        /// then reports the average. Chrome coarsens performance.now() on a page that is not cross-origin isolated,
        /// so a single-call timing here would be a quantisation artefact rather than a measurement.
        /// </summary>
        private static Measurement Measure(Action body, double targetMs = k_TargetMs)
        {
            for (int i = 0; i < 8; ++i) body();     // bind the import, warm the interpreter

            // The cap is low on purpose. A frozen clock is a real possibility here (see RunAsync's remarks), and
            // the failure it produces without a cap is not a wrong number, it is a page that never finishes.
            const int maxIterations = 1 << 17;

            int iterations = 4;
            while (true)
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < iterations; ++i) body();
                sw.Stop();

                double elapsed = sw.Elapsed.TotalMilliseconds;
                if (elapsed >= targetMs)
                    return new Measurement(elapsed / iterations, iterations, false);

                if (iterations >= maxIterations)
                    return new Measurement(elapsed / iterations, iterations, elapsed <= 0d);

                double factor = elapsed <= 0.01d ? 8d : Math.Min(8d, Math.Max(2d, targetMs / elapsed));
                iterations = (int)Math.Min(maxIterations, Math.Max(iterations + 1, iterations * factor));
            }
        }
    }
}
