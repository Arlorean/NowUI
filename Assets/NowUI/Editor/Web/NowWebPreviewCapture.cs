using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;

namespace NowUI.Editor.Web
{
    /// <summary>
    /// The receiving half of a browser-side capture: where a still or a clip is written, what it may be called,
    /// what it may contain, and who is allowed to send one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page records ITSELF - <c>canvas.toBlob</c>, a hand-written WebP mux, or <c>MediaRecorder</c> - because
    /// nothing else is available to a stranger with the package installed: headless Chrome, ffmpeg, Python and
    /// Node all ship to nobody. What the browser cannot do on its own is put the bytes somewhere the user or an
    /// agent will find them, and this is that: one POST route on the preview server that already exists.
    /// </para>
    /// <para>
    /// Every decision here is about narrowing what an HTTP request can cause. The server is loopback-only, but
    /// loopback is not a boundary a browser respects: ANY page the user has open can issue a request to
    /// 127.0.0.1. So the destination folder is COMPUTED and never received, the extension is chosen from the
    /// content type rather than from a filename, the size is capped before a byte is read, and the request must
    /// carry a per-session token that only same-origin script can obtain.
    /// </para>
    /// <para>
    /// Deliberately free of <c>UnityEditor</c> and <c>UnityEngine</c> calls apart from the cached root: it runs on
    /// the server's background thread, where most of both APIs are illegal, and it is exercised by unit tests that
    /// never open a socket.
    /// </para>
    /// </remarks>
    internal static class NowWebPreviewCapture
    {
        /// <summary>
        /// The header the browser must send. A CUSTOM header, which is the point: a cross-origin fetch carrying
        /// one triggers a CORS preflight, the preview server answers no preflight, and the request never happens.
        /// A form post or an <c>img</c> tag cannot set it at all.
        /// </summary>
        internal const string TokenHeader = "X-NowUI-Capture";

        /// <summary>
        /// 32 MB. Far above any honest capture - the largest measured clip is under 300 KB, and the page caps
        /// itself at 8 MB - and far below anything that could fill a disk before the user noticed.
        /// </summary>
        internal const long MaxBytes = 32L * 1024L * 1024L;

        /// <summary>The folder name under <c>&lt;ProjectRoot&gt;/NowUI</c>, beside <c>apps</c>.</summary>
        internal const string FolderName = "captures";

        private static string s_Root;
        private static string s_Token;
        private static readonly object s_Gate = new object();

        /// <summary>The most recent file written, for the window to show. Null until one is.</summary>
        internal static string lastCapture { get; private set; }

        /// <summary>When <see cref="lastCapture"/> was written, in local time.</summary>
        internal static DateTime lastCaptureAt { get; private set; }

        /// <summary>
        /// <c>&lt;ProjectRoot&gt;/NowUI/captures</c>.
        /// </summary>
        /// <remarks>
        /// Beside <c>apps/</c> and for the same two reasons. Outside the package, because a package manager
        /// replaces that folder wholesale on upgrade and a user's recordings are not the package's to delete.
        /// Outside <c>Assets/</c>, because a .png or .webp dropped under Assets/ is an AssetDatabase import - a
        /// reimport and a .meta file per capture, for a file the Unity project never uses.
        /// </remarks>
        internal static string CapturesRoot
        {
            get
            {
                // Cached at Prepare() on the main thread. The fallback is for a caller that never prepared, and
                // for the tests; NowWebPreviewPaths.ProjectRoot reads Application.dataPath, which is the one
                // thing here that would rather not be touched from the accept loop.
                string cached = s_Root;
                if (!string.IsNullOrEmpty(cached)) return cached;
                return Path.Combine(NowWebPreviewPaths.UserRoot, FolderName);
            }
        }

        /// <summary>
        /// The capability the page has to present to write a file, minted fresh every time the server starts.
        /// </summary>
        /// <remarks>
        /// It is served from <c>/__nowui/token</c> with no CORS headers whatsoever, which is what makes it a
        /// secret: a page on another origin can ISSUE that request but cannot read the answer, so only script the
        /// preview server itself served can learn the value. Per-session rather than persisted, so a token that
        /// leaked into a log or a screenshot stops working when the Editor restarts.
        /// </remarks>
        internal static string Token
        {
            get
            {
                lock (s_Gate)
                {
                    if (string.IsNullOrEmpty(s_Token)) s_Token = MintToken();
                    return s_Token;
                }
            }
        }

        /// <summary>Called on the main thread as the server binds: caches the root and rolls a new token.</summary>
        internal static void Prepare()
        {
            s_Root = Path.Combine(NowWebPreviewPaths.UserRoot, FolderName);
            lock (s_Gate) { s_Token = MintToken(); }
        }

        private static string MintToken()
        {
            var bytes = new byte[24];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);

            var text = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        // ----------------------------------------------------------------------------------------- validation

        /// <summary>
        /// One path segment of letters, digits, <c>-</c> and <c>_</c>, at most 64 long. The same alphabet
        /// <c>NowWebPreviewServer.IsSafeAppName</c> allows, and for the same reason: it cannot spell a separator,
        /// a drive letter, a <c>..</c>, a UNC prefix, an alternate data stream or a Windows device name, so the
        /// combination with a computed folder cannot leave that folder.
        /// </summary>
        internal static bool IsSafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;

            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '_' || c == '-';
                if (!ok) return false;
            }

            // "-" and "--" resolve to a file, but a name that is nothing but punctuation is a mistake upstream.
            foreach (char c in name)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return true;
            }

            return false;
        }

        /// <summary>
        /// The extension for a content type, or null for a type this refuses to write.
        /// </summary>
        /// <remarks>
        /// The SERVER picks the extension, never the client, and that is the whole of the point. A filename in a
        /// request is a string an attacker writes; a content type is a string an attacker writes too, but this
        /// maps it through a closed list of six, so the worst a caller can achieve is the wrong one of six
        /// harmless extensions in a folder the user owns. There is no path in which a request names <c>.exe</c>,
        /// <c>.ps1</c>, <c>.cs</c> or <c>.meta</c>.
        /// </remarks>
        internal static string ExtensionFor(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return null;

            // "image/webp; charset=binary" and the like: the parameters are not part of the decision.
            int semicolon = contentType.IndexOf(';');
            string type = (semicolon < 0 ? contentType : contentType.Substring(0, semicolon)).Trim();

            switch (type.ToLowerInvariant())
            {
                case "image/png": return "png";
                case "image/webp": return "webp";
                case "image/gif": return "gif";
                case "video/webm": return "webm";
                case "video/mp4": return "mp4";
                // The report that lands INSTEAD of an image when the browser tab was not drawing. It is the
                // difference between an empty folder and an explanation.
                case "text/plain": return "txt";
                default: return null;
            }
        }

        // ---------------------------------------------------------------------------------------------- write

        /// <summary>
        /// Writes one capture into <paramref name="root"/>, or returns null with a sentence in
        /// <paramref name="error"/>. Never throws.
        /// </summary>
        /// <remarks>
        /// The resolved path is re-checked against the root after combining, even though the name alphabet
        /// already makes escape impossible. Two checks that agree cost nothing; one check that was wrong costs
        /// the user's project.
        /// </remarks>
        internal static string Write(string root, string name, string contentType, byte[] bytes, out string error)
        {
            error = null;

            if (!IsSafeName(name))
            {
                error = "The capture name may hold letters, digits, '-' and '_' only, and must contain at least " +
                        "one letter or digit.";
                return null;
            }

            string extension = ExtensionFor(contentType);
            if (extension == null)
            {
                error = "The NowUI Web Preview writes captures as PNG, WebP, GIF, WebM, MP4 or plain text. It " +
                        "was offered '" + contentType + "'.";
                return null;
            }

            if (bytes == null || bytes.Length == 0)
            {
                error = "The capture was empty, so nothing was written.";
                return null;
            }

            if (bytes.LongLength > MaxBytes)
            {
                error = "The capture was " + bytes.LongLength + " bytes; the limit is " + MaxBytes + ".";
                return null;
            }

            string destination = Path.Combine(root, name + "." + extension);
            if (!IsInside(root, destination))
            {
                error = "The capture would have been written outside " + root + ".";
                return null;
            }

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(destination, bytes);
            }
            catch (Exception e)
            {
                error = "Could not write " + destination + ": " + e.Message;
                return null;
            }

            lastCapture = destination;
            lastCaptureAt = DateTime.Now;
            return destination;
        }

        /// <summary>The same containment check the file server uses, kept here so this file stands alone.</summary>
        internal static bool IsInside(string root, string candidate)
        {
            try
            {
                string fullRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string full = Path.GetFullPath(candidate);
                return full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(full, fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
