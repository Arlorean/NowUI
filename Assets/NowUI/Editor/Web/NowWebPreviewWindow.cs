using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NowUI.Editor.Web
{
    /// <summary>
    /// <c>Tools &gt; NowUI &gt; Web Preview</c>: one window, one port, one folder of the user's own JavaScript.
    /// </summary>
    /// <remarks>
    /// The whole feature is "click this and NowUI is running in your browser, from your machine, with your own
    /// application". Everything in this window exists to make that true or to explain, in a sentence the user
    /// can copy, why it is not. Nothing here throws to the console and nothing here opens a modal dialog: a
    /// dialog you cannot copy out of is not an error report.
    /// </remarks>
    internal sealed class NowWebPreviewWindow : EditorWindow
    {
        private const string SeededKey = "NowUI.WebPreview.Seeded";

        private string m_App = "app";
        private bool m_Watch = true;
        private bool m_ShowReport;
        private string m_BundleSummary;
        private string m_BundleCommit;
        private Vector2 m_Scroll;
        private string m_Notice;

        [MenuItem("Tools/NowUI/Web Preview", false, 300)]
        private static void Open()
        {
            var window = GetWindow<NowWebPreviewWindow>(false, "NowUI Web", true);
            window.minSize = new Vector2(420f, 320f);
            window.FirstOpen();
        }

        [MenuItem("Tools/NowUI/Stop Web Preview", false, 301)]
        private static void StopMenu()
        {
            NowWebPreviewServer.Stop();
            foreach (var window in Resources.FindObjectsOfTypeAll<NowWebPreviewWindow>()) window.Repaint();
        }

        [MenuItem("Tools/NowUI/Stop Web Preview", true)]
        private static bool StopMenuValidate()
        {
            return NowWebPreviewServer.IsRunning;
        }

        /// <summary>
        /// The first click does all of it: find the bundle, seed the user's folder, bind the port, open the
        /// browser. There is deliberately no second step for the user to discover.
        /// </summary>
        private void FirstOpen()
        {
            string bundle = NowWebPreviewPaths.FindBundleRoot();

            // Once, ever. The package writes into the user's folder exactly one time so that an application they
            // have since edited can never be overwritten by an upgrade or by opening this window again.
            if (!EditorPrefs.GetBool(SeededKey, false))
            {
                if (NowWebPreviewPaths.SeedApps(bundle))
                    m_Notice = "Created " + NowWebPreviewPaths.UserRoot + " with a copy of the sample application. " +
                               "That folder is yours; NowUI will not write to it again.";
                EditorPrefs.SetBool(SeededKey, true);
            }

            if (!NowWebPreviewServer.IsRunning && bundle != null)
            {
                if (NowWebPreviewServer.Start())
                    Application.OpenURL(BuildUrl());
            }

            LoadBundleStamp();
        }

        private void OnEnable()
        {
            LoadBundleStamp();
        }

        private void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            DrawStatus();
            EditorGUILayout.Space();
            DrawApp();
            EditorGUILayout.Space();
            DrawCaptures();
            EditorGUILayout.Space();
            DrawBundle();
            DrawProblems();

            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------------------------------------- status

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);

            if (NowWebPreviewServer.IsRunning)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.SelectableLabel("Serving " + NowWebPreviewServer.url,
                        EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (GUILayout.Button("Copy", GUILayout.Width(52f)))
                        EditorGUIUtility.systemCopyBuffer = NowWebPreviewServer.url;
                    if (GUILayout.Button("Stop", GUILayout.Width(52f)))
                        NowWebPreviewServer.Stop();
                }
            }
            else
            {
                EditorGUILayout.LabelField("Stopped.");
                if (GUILayout.Button("Start"))
                {
                    if (NowWebPreviewServer.Start()) LoadBundleStamp();
                }
            }
        }

        // ------------------------------------------------------------------------------------------------- app

        private void DrawApp()
        {
            EditorGUILayout.LabelField("Your application", EditorStyles.boldLabel);

            string[] user = NowWebPreviewPaths.ListUserApps();
            var labels = new string[user.Length + 1];
            for (int i = 0; i < user.Length; i++) labels[i] = user[i] + "   (yours)";
            labels[user.Length] = "app   (the shipped sample)";

            int current = Array.IndexOf(user, m_App);
            if (current < 0) current = user.Length;

            int chosen = EditorGUILayout.Popup("Serve", current, labels);
            m_App = chosen < user.Length ? user[chosen] : "app";

            m_Watch = EditorGUILayout.ToggleLeft(
                "Reload the page when the file changes (?watch=1)", m_Watch);
            m_ShowReport = EditorGUILayout.ToggleLeft(
                "Show the bridge's diagnostic panel over the app (?report=1)", m_ShowReport);

            using (new EditorGUI.DisabledScope(!NowWebPreviewServer.IsRunning))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open my app")) Application.OpenURL(BuildUrl());
                if (GUILayout.Button("Open the gallery")) Application.OpenURL(NowWebPreviewServer.url);
            }

            string appFile = Path.Combine(NowWebPreviewPaths.AppsRoot, m_App + ".js");
            EditorGUILayout.SelectableLabel(appFile, EditorStyles.miniLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reveal", GUILayout.Width(70f)))
                    EditorUtility.RevealInFinder(File.Exists(appFile) ? appFile : NowWebPreviewPaths.AppsRoot);

                using (new EditorGUI.DisabledScope(!File.Exists(appFile)))
                {
                    if (GUILayout.Button("Edit", GUILayout.Width(70f)))
                    {
                        // Handed to the OS, which knows which editor the user wants. NowUI does not ship one.
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appFile) { UseShellExecute = true }); }
                        catch (Exception e) { m_Notice = "Could not open " + appFile + ": " + e.Message; }
                    }
                }
            }

            if (!File.Exists(appFile))
            {
                EditorGUILayout.HelpBox(
                    "There is no " + m_App + ".js in your folder, so the preview will serve the sample of that " +
                    "name from inside the package instead. Put a file there and it wins.", MessageType.Info);
            }
        }

        // -------------------------------------------------------------------------------------------- captures

        /// <summary>
        /// The picture half of the feature: the browser records itself and the Editor writes the file here.
        /// </summary>
        /// <remarks>
        /// Two buttons rather than a recorder UI, because the browser is the recorder. They copy a URL - the same
        /// URL an AI agent would compose - so "show me what you built" is a paste into the address bar, and the
        /// file lands in a folder this window can open. Nothing captures on its own: a preview that wrote files
        /// nobody asked for would be a preview that filled a repository.
        /// </remarks>
        private void DrawCaptures()
        {
            EditorGUILayout.LabelField("Captures", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!NowWebPreviewServer.IsRunning))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy a still URL"))
                    EditorGUIUtility.systemCopyBuffer = BuildCaptureUrl("&shot=1&name=" + m_App + "-shot");
                if (GUILayout.Button("Copy a 5s clip URL"))
                    EditorGUIUtility.systemCopyBuffer =
                        BuildCaptureUrl("&clip=5&fps=12&scale=0.5&name=" + m_App + "-clip");
            }

            EditorGUILayout.SelectableLabel(NowWebPreviewCapture.CapturesRoot, EditorStyles.miniLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reveal", GUILayout.Width(70f)))
                {
                    string folder = NowWebPreviewCapture.CapturesRoot;
                    try { Directory.CreateDirectory(folder); } catch (Exception) { }
                    EditorUtility.RevealInFinder(
                        File.Exists(NowWebPreviewCapture.lastCapture) ? NowWebPreviewCapture.lastCapture : folder);
                }

                string last = NowWebPreviewCapture.lastCapture;
                EditorGUILayout.LabelField(
                    last == null
                        ? "Nothing captured yet."
                        : Path.GetFileName(last) + "  ·  " + NowWebPreviewCapture.lastCaptureAt.ToString("HH:mm:ss"),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.HelpBox(
                "Open one of those URLs and keep the browser window visible and in front. A browser suspends " +
                "drawing entirely in a hidden or minimised tab, so a capture taken there would be blank - NowUI " +
                "counts the frames and writes a .txt saying so rather than an empty picture.\n\n" +
                "Documentation~/WebPreview.md has the flags and what each browser can record.",
                MessageType.None);
        }

        // ---------------------------------------------------------------------------------------------- bundle

        private void DrawBundle()
        {
            EditorGUILayout.LabelField("Bundle", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(m_BundleSummary ?? "not found", EditorStyles.miniLabel);

            if (!string.IsNullOrEmpty(m_BundleCommit))
            {
                string head = HeadShort();
                if (!string.IsNullOrEmpty(head) && !head.StartsWith(m_BundleCommit, StringComparison.Ordinal) &&
                    !m_BundleCommit.StartsWith(head, StringComparison.Ordinal))
                {
                    EditorGUILayout.HelpBox(
                        "This bundle was built at commit " + m_BundleCommit + ", and your checkout is at " + head +
                        ". The browser build is a precompiled artifact, so it lags any Runtime change made since. " +
                        "Rebuild it with Tools/Build-NowUIWebBundle.ps1.", MessageType.Info);
                }
            }
        }

        private void DrawProblems()
        {
            if (NowWebPreviewPaths.FindBundleRoot() == null)
            {
                var text = new StringBuilder();
                text.AppendLine("The NowUI web bundle is not installed, so there is nothing to serve.");
                text.AppendLine();
                text.AppendLine("Looked in:");
                foreach (string candidate in NowWebPreviewPaths.BundleCandidates())
                    text.Append("  ").AppendLine(candidate);
                text.AppendLine();
                text.AppendLine("Build it with Tools/Build-NowUIWebBundle.ps1, or download " +
                                "NowUI-Web-<version>.zip from the release and unzip it to " +
                                Path.Combine(NowWebPreviewPaths.UserRoot, "WebBundle") + ".");

                EditorGUILayout.HelpBox(text.ToString(), MessageType.Error);
                if (GUILayout.Button("Copy those paths"))
                    EditorGUIUtility.systemCopyBuffer = text.ToString();
            }

            if (!string.IsNullOrEmpty(NowWebPreviewServer.lastError))
                EditorGUILayout.HelpBox(NowWebPreviewServer.lastError, MessageType.Warning);

            if (!string.IsNullOrEmpty(m_Notice))
                EditorGUILayout.HelpBox(m_Notice, MessageType.Info);
        }

        // ----------------------------------------------------------------------------------------------- glue

        /// <summary>
        /// The URL the buttons open.
        /// </summary>
        /// <remarks>
        /// <c>report=0</c> is carried deliberately: the bridge's diagnostic <c>&lt;pre&gt;</c> is instrumentation
        /// for the bridge itself and it covers the bottom 45% of the canvas, which is right when the report IS
        /// the picture and wrong when the picture is the user's application. The checkbox above turns it back on.
        /// The <c>v=</c> stamp only freshens the top-level document; the server's ETag and no-cache headers are
        /// what actually guarantee fresh sub-resources.
        /// </remarks>
        private string BuildUrl()
        {
            var url = new StringBuilder(NowWebPreviewServer.url ?? ("http://127.0.0.1:" + NowWebPreviewServer.DefaultPort + "/"));
            url.Append("?app=").Append(m_App);
            url.Append(m_ShowReport ? "&report=1" : "&report=0");
            if (m_Watch) url.Append("&watch=1");
            url.Append("&v=").Append(DateTime.UtcNow.Ticks.ToString("x"));
            return url.ToString();
        }

        /// <summary>
        /// <see cref="BuildUrl"/> plus capture flags, minus the watch poll: a page that reloaded itself halfway
        /// through a recording would produce a clip of its own start-up.
        /// </summary>
        private string BuildCaptureUrl(string flags)
        {
            var url = new StringBuilder(NowWebPreviewServer.url ??
                ("http://127.0.0.1:" + NowWebPreviewServer.DefaultPort + "/"));
            url.Append("?app=").Append(m_App);
            url.Append("&report=0");
            // Pinned rather than inherited: on a 2x display the drawing buffer is four times the pixels, and a
            // capture whose size depends on which monitor the browser happened to open on is not reproducible.
            url.Append("&dpr=1");
            url.Append(flags);
            url.Append("&v=").Append(DateTime.UtcNow.Ticks.ToString("x"));
            return url.ToString();
        }

        private void LoadBundleStamp()
        {
            m_BundleSummary = null;
            m_BundleCommit = null;

            string root = NowWebPreviewPaths.FindBundleRoot();
            if (root == null) return;

            m_BundleSummary = root;

            string stampPath = Path.Combine(root, "bundle.json");
            if (!File.Exists(stampPath)) return;

            try
            {
                var stamp = JsonUtility.FromJson<BundleStamp>(File.ReadAllText(stampPath));
                if (stamp == null) return;

                m_BundleCommit = stamp.commit;
                m_BundleSummary = "NowUI " + stamp.nowuiVersion + "  ·  " + stamp.commit + "  ·  " +
                                  stamp.builtUtc + "  ·  surface " + stamp.surfaceHash + "  ·  " +
                                  stamp.files + " files, " + (stamp.bytes / 1048576f).ToString("0.00") + " MiB" +
                                  "\n" + root;
            }
            catch (Exception)
            {
                // A bundle without a readable stamp still serves perfectly well. The path alone is enough.
            }
        }

        private static string HeadShort()
        {
            try
            {
                string head = Path.Combine(NowWebPreviewPaths.ProjectRoot, ".git/HEAD");
                if (!File.Exists(head)) return null;

                string content = File.ReadAllText(head).Trim();
                if (content.StartsWith("ref:", StringComparison.Ordinal))
                {
                    string refPath = Path.Combine(Path.Combine(NowWebPreviewPaths.ProjectRoot, ".git"),
                        content.Substring(4).Trim().Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(refPath)) return null;
                    content = File.ReadAllText(refPath).Trim();
                }

                return content.Length >= 7 ? content.Substring(0, 7) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [Serializable]
        private sealed class BundleStamp
        {
            public string nowuiVersion;
            public string commit;
            public string builtUtc;
            public string surfaceHash;
            public int files;
            public long bytes;
        }
    }
}
