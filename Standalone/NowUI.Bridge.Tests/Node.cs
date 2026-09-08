// Running js/run.mjs from the managed test run.
//
// The JavaScript half of the bridge is a pure data structure - no DOM, no wasm, no typed array that crosses a
// boundary - so it can be checked under node, and it should be checked from the SAME command that checks the
// managed half. A bridge whose two halves are tested by two commands is a bridge whose two halves get tested at
// two different times.
//
// A machine with no node on PATH gets an Assert.Ignore with the command it would have run, not a failure: the
// managed tests are the ones this project owns, and turning "node is not installed" into a red build would make
// the suite lie about what is broken.

using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace NowUI.Bridge.Tests
{
    internal static class Node
    {
        /// <summary>The repository's Standalone/NowUI.Bridge.Tests/js directory, found from the test assembly.</summary>
        public static string ScriptPath
        {
            get
            {
                for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "Standalone", "NowUI.Bridge.Tests", "js", "run.mjs");
                    if (File.Exists(candidate)) return candidate;

                    // Also reachable from the project directory itself when the assembly sits under bin/.
                    string sibling = Path.Combine(dir.FullName, "js", "run.mjs");
                    if (File.Exists(sibling)) return sibling;
                }

                return null;
            }
        }

        /// <summary>Runs run.mjs with the given arguments. Ignores the test when node is not on PATH.</summary>
        public static string Run(params string[] arguments)
        {
            string script = ScriptPath;
            if (script == null)
                Assert.Ignore("js/run.mjs was not found from " + AppContext.BaseDirectory + ".");

            var info = new ProcessStartInfo("node")
            {
                WorkingDirectory = Path.GetDirectoryName(script),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            info.ArgumentList.Add(script);
            foreach (string argument in arguments) info.ArgumentList.Add(argument);

            Process process;
            try
            {
                process = Process.Start(info);
            }
            catch (Exception e)
            {
                Assert.Ignore("node is not on PATH, so the JavaScript half of W2/W3 was not run here. " +
                              "Run it directly with: node \"" + script + "\". (" + e.Message + ")");
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                Assert.Fail("node run.mjs " + string.Join(" ", arguments) + " exited " + process.ExitCode + ".\n" +
                            output + "\n" + error);

            return output;
        }
    }
}
