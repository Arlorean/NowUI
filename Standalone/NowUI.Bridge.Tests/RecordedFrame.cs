// Frames the REAL JavaScript recorder produced, loaded into a BridgeRecorder.
//
// This is the load-bearing shape of this whole test assembly and it is worth one paragraph. ProtocolTests builds
// frames in C# with TestFrame, which proves the validator right about frames a C# writer produced and proves
// nothing about whether wwwroot/nowui/recorder.js writes what the managed validator expects. Everything that
// wants that second property goes through here: node records a frame through the actual recorder, writes the
// bytes to disk, and the managed side feeds them through the same NEED_MORE growth handshake the browser uses.
//
// The frames themselves are declared in js/run.mjs, one per mode, so the two halves cannot drift into recording
// different applications.

using System;
using System.IO;
using NowUI.Bridge;
using NUnit.Framework;

namespace NowUI.Bridge.Tests
{
    /// <summary>One frame, recorded by <c>recorder.js</c> under node and handed to a <see cref="BridgeRecorder"/>.</summary>
    internal static class RecordedFrame
    {
        /// <summary>
        /// Records <paramref name="mode"/> through node and returns the recorder that holds it, validated and
        /// with its strings interned - so the caller can replay it immediately.
        /// </summary>
        public static BridgeRecorder Load(string mode, out int usedSlots)
        {
            BridgeRecorder recorder = LoadRaw(mode, out usedSlots);
            recorder.Preamble(usedSlots);
            return recorder;
        }

        /// <summary>The same, without the preamble, for the tests that assert what the validator refuses.</summary>
        /// <param name="onRecord">
        /// Invoked on every crossing, including a NEED_MORE retry. W4's acceptance that the author's draw function
        /// runs once per frame is asserted by counting these.
        /// </param>
        public static BridgeRecorder LoadRaw(string mode, out int usedSlots, Action onRecord = null)
        {
            string path = Path.Combine(
                Path.GetTempPath(), "nowui-bridge-" + mode + "-" + Guid.NewGuid().ToString("N") + ".bin");

            try
            {
                Node.Run("--emit-frame", path, mode);

                byte[] blob = File.ReadAllBytes(path);
                int used = BitConverter.ToInt32(blob, 0);
                int textBytes = BitConverter.ToInt32(blob, 4);

                var ops = new int[used];
                Buffer.BlockCopy(blob, 8, ops, 0, used * 4);
                var text = new byte[textBytes];
                Buffer.BlockCopy(blob, 8 + used * 4, text, 0, textBytes);

                var recorder = new BridgeRecorder();

                RecordFunc feed = (target, targetText, results, resultsText, resultSlots, frame) =>
                {
                    if (onRecord != null) onRecord();

                    if (used > target.Length || text.Length > targetText.Length)
                    {
                        target[0] = used;
                        target[1] = text.Length;
                        return Abi.NeedMore;
                    }

                    ops.AsSpan().CopyTo(target);
                    text.AsSpan().CopyTo(targetText);
                    return used;
                };

                usedSlots = recorder.Record(feed, 1);
                return recorder;
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>Every opcode in the frame's op stream, in order. What "the six opcodes drew" is asserted on.</summary>
        public static int[] Opcodes(BridgeRecorder recorder)
        {
            var codes = new System.Collections.Generic.List<int>();

            for (int slot = recorder.opStart; slot < recorder.opEnd;)
            {
                int header = recorder.Slot(slot);
                codes.Add(header & 0xFFFF);
                slot += 1 + ((header >> 16) & 0xFFFF);
            }

            return codes.ToArray();
        }

        /// <summary>Asserts that node is available, since a frame recorded by C# would defeat the point.</summary>
        public static void RequireNode()
        {
            if (Node.ScriptPath == null)
                Assert.Ignore("js/run.mjs was not found, so no JavaScript-recorded frame could be loaded.");
        }
    }
}
