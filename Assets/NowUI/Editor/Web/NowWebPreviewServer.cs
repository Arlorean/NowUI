using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace NowUI.Editor.Web
{
    /// <summary>
    /// A loopback static file server for the precompiled NowUI web bundle, run from inside the Unity Editor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists so a Unity developer who installed NowUI can see it running in a browser without installing
    /// a .NET SDK, without emscripten, without a terminal and without a network. The bundle it serves was built
    /// by <c>Tools/Build-NowUIWebBundle.ps1</c> on a maintainer's machine and shipped inside the package; all
    /// this does is put it on a socket.
    /// </para>
    /// <para>
    /// It serves ONE browser tab, not a load test, so every request is handled inline on one background thread.
    /// The whole point is to be small enough to read.
    /// </para>
    /// </remarks>
    internal static class NowWebPreviewServer
    {
        /// <summary>The port the window remembers, so the user can bookmark the URL.</summary>
        internal const string PortPrefKey = "NowUI.WebPreview.Port";

        /// <summary>Survives a domain reload, which is what brings the server back after a script recompile.</summary>
        private const string RunningKey = "NowUI.WebPreview.Running";

        internal const int DefaultPort = 8973;
        internal const int LastProbedPort = 8982;

        /// <summary>
        /// Total budget for the "is someone else on this port, and are they me?" sweep. Ten ports times a
        /// 250 ms timeout would be 2.5 s of frozen Editor in the pathological case where every port accepts a
        /// connection and never answers, so the sweep gives up and takes an ephemeral port instead.
        /// </summary>
        private const int ProbeBudgetMs = 750;

        /// <summary>
        /// Root-level module names the bundle owns. A request for one of these NEVER resolves against the
        /// user's folder, so an application accidentally named main.js cannot replace the page's boot script and
        /// produce a black canvas nobody can explain. Everything else at the root is the author's to shadow -
        /// which is what makes <c>?app=app</c> serve THEIR app.js.
        /// </summary>
        private static readonly HashSet<string> ReservedRootModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "main.js", "nowui-fetch.js", "nowui-gl.js", "nowui-input.js",
        };

        private static HttpListener s_Listener;
        private static Thread s_Thread;
        private static string s_BundleRoot;
        private static int s_Port;
        private static volatile bool s_Stopping;

        /// <summary>The last failure, for the window to render. Never thrown at the console.</summary>
        internal static string lastError { get; private set; }

        internal static bool IsRunning
        {
            get { return s_Listener != null && s_Listener.IsListening; }
        }

        internal static int port
        {
            get { return s_Port; }
        }

        internal static string url
        {
            get { return IsRunning ? "http://127.0.0.1:" + s_Port + "/" : null; }
        }

        internal static string bundleRoot
        {
            get { return s_BundleRoot; }
        }

        // -------------------------------------------------------------------------------------------- lifecycle

        [InitializeOnLoadMethod]
        private static void Init()
        {
            // Batch mode is every CI run and both test harness runs. Binding a socket inside a 1864-test EditMode
            // run is exactly the kind of thing that produces an intermittent failure instead of a clean one, so
            // this method does nothing at all there.
            if (Application.isBatchMode) return;

            AssemblyReloadEvents.beforeAssemblyReload += StopForReload;
            EditorApplication.quitting += Stop;

            // A recompile takes the listener down and this brings it straight back up, so the user's open tab
            // keeps working after one F5 instead of dying every time they touch a script.
            if (SessionState.GetBool(RunningKey, false))
                EditorApplication.delayCall += () => { if (!IsRunning) Start(); };
        }

        /// <summary>
        /// Brings the server up. Returns true on success; on failure <see cref="lastError"/> holds a sentence
        /// the window can show. Nothing here throws to the console - a stack trace in a log is not a bug report.
        /// </summary>
        internal static bool Start()
        {
            lastError = null;
            if (IsRunning) return true;

            s_BundleRoot = NowWebPreviewPaths.FindBundleRoot();
            if (s_BundleRoot == null)
            {
                lastError = "The NowUI web bundle is not installed.";
                return false;
            }

            int preferred = EditorPrefs.GetInt(PortPrefKey, DefaultPort);
            if (preferred < 1024 || preferred > 65535) preferred = DefaultPort;

            var sweep = new List<int> { preferred };
            for (int p = DefaultPort; p <= LastProbedPort; p++) if (p != preferred) sweep.Add(p);

            long budgetStart = DateTime.UtcNow.Ticks;
            foreach (int candidate in sweep)
            {
                if (IsPortTaken(candidate))
                {
                    // Someone is already here. If it is THIS project's own server (a second Editor instance of
                    // the same project), adopt it. If it is another project's, skip - otherwise project A's
                    // Editor would happily hand the user project B's application and nothing would say so.
                    bool budgetLeft = (DateTime.UtcNow.Ticks - budgetStart) / TimeSpan.TicksPerMillisecond < ProbeBudgetMs;
                    if (budgetLeft && IdentifiesAsThisProject(candidate))
                    {
                        s_Port = candidate;
                        SessionState.SetBool(RunningKey, true);
                        lastError = null;
                        return true;
                    }

                    continue;
                }

                if (TryBind(candidate)) return true;
            }

            // Every candidate was taken or refused. An ephemeral port still works; it just cannot be bookmarked.
            if (TryBind(0))
            {
                lastError = "Ports " + DefaultPort + "-" + LastProbedPort + " were all busy, so the preview took " +
                            "port " + s_Port + " instead. That port changes each time.";
                return true;
            }

            return false;
        }

        private static bool TryBind(int candidate)
        {
            var listener = new HttpListener();

            // 127.0.0.1 literally, never "localhost": localhost resolves to ::1 first on a dual-stack machine,
            // and a listener bound to one address with a browser resolving to the other is a connection refused
            // with no explanation. Never "+" or "*" either - those need admin on Windows AND would put the
            // user's project folder on the LAN.
            string prefix = "http://127.0.0.1:" + (candidate == 0 ? FreeEphemeralPort() : candidate) + "/";
            listener.Prefixes.Add(prefix);

            try
            {
                listener.Start();
            }
            catch (Exception e)
            {
                lastError = "Could not listen on " + prefix + ": " + e.Message;
                try { listener.Close(); } catch (Exception) { }
                return false;
            }

            s_Listener = listener;
            s_Port = int.Parse(prefix.Substring("http://127.0.0.1:".Length).TrimEnd('/'), CultureInfo.InvariantCulture);
            s_Stopping = false;

            // IsBackground is what stops the Editor hanging on quit if the accept loop is mid-GetContext.
            s_Thread = new Thread(AcceptLoop) { IsBackground = true, Name = "NowUI Web Preview" };
            s_Thread.Start();

            if (candidate != 0) EditorPrefs.SetInt(PortPrefKey, s_Port);
            SessionState.SetBool(RunningKey, true);
            return true;
        }

        private static int FreeEphemeralPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int assigned = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return assigned;
        }

        /// <summary>Stops deliberately: the server stays down across the next domain reload.</summary>
        internal static void Stop()
        {
            SessionState.SetBool(RunningKey, false);
            StopForReload();
        }

        /// <summary>
        /// Closes the listener WITHOUT clearing the "should be running" flag, so a script recompile takes the
        /// server down and <see cref="Init"/> brings it straight back up on the same port.
        /// </summary>
        internal static void StopForReload()
        {
            s_Stopping = true;

            var listener = s_Listener;
            s_Listener = null;
            if (listener != null)
            {
                try { listener.Stop(); } catch (Exception) { }
                try { listener.Close(); } catch (Exception) { }
            }

            var thread = s_Thread;
            s_Thread = null;
            if (thread != null && thread.IsAlive)
            {
                try { thread.Join(500); } catch (Exception) { }
            }
        }

        // ---------------------------------------------------------------------------------------------- probing

        private static bool IsPortTaken(int candidate)
        {
            var client = new TcpClient();
            try
            {
                IAsyncResult async = client.BeginConnect(IPAddress.Loopback, candidate, null, null);
                return async.AsyncWaitHandle.WaitOne(60) && client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                try { ((IDisposable)client).Dispose(); } catch (Exception) { }
            }
        }

        /// <summary>
        /// Asks whoever holds the port who they are, over a hand-written one-line HTTP/1.1 GET.
        /// </summary>
        /// <remarks>
        /// Sockets rather than HttpWebRequest or HttpClient, for two reasons that both cost nothing here: the
        /// former is marked obsolete on modern reference assemblies and the latter drags in another assembly
        /// this Editor-only code has no other use for. The request is 60 bytes and the answer is one line, so a
        /// full HTTP client would be doing nothing this does not.
        /// </remarks>
        private static bool IdentifiesAsThisProject(int candidate)
        {
            var client = new TcpClient();
            try
            {
                IAsyncResult connecting = client.BeginConnect(IPAddress.Loopback, candidate, null, null);
                if (!connecting.AsyncWaitHandle.WaitOne(120) || !client.Connected) return false;
                client.EndConnect(connecting);

                client.SendTimeout = 250;
                client.ReceiveTimeout = 250;

                const string crlf = "\r\n";
                byte[] request = Encoding.ASCII.GetBytes(
                    "GET /__nowui/id HTTP/1.1" + crlf + "Host: 127.0.0.1" + crlf + "Connection: close" + crlf + crlf);

                NetworkStream stream = client.GetStream();
                stream.Write(request, 0, request.Length);

                var answer = new MemoryStream();
                var buffer = new byte[1024];
                int read;
                while (answer.Length < 8192 && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    answer.Write(buffer, 0, read);

                string text = Encoding.UTF8.GetString(answer.ToArray());
                int split = text.IndexOf(crlf + crlf, StringComparison.Ordinal);
                if (split < 0 || !text.StartsWith("HTTP/1.1 200", StringComparison.Ordinal)) return false;

                string body = text.Substring(split + 4).Trim();
                return body.Length > 0 && string.Equals(
                    Norm(body), Norm(NowWebPreviewPaths.ProjectRoot), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // Anything on that port that is not a NowUI preview for THIS project: not ours, move on. This is
                // the check that stops project A's Editor handing the user project B's application.
                return false;
            }
            finally
            {
                try { ((IDisposable)client).Dispose(); } catch (Exception) { }
            }
        }

        private static string Norm(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
        }

        // ----------------------------------------------------------------------------------------- accept loop

        private static void AcceptLoop()
        {
            HttpListener listener = s_Listener;
            while (listener != null && listener.IsListening && !s_Stopping)
            {
                HttpListenerContext context;
                try
                {
                    context = listener.GetContext();
                }
                catch (ObjectDisposedException) { return; }   // Stop() closed it under us. Expected, not an error.
                catch (HttpListenerException) { return; }      // Same, on the other implementation.
                catch (InvalidOperationException) { return; }
                catch (Exception) { return; }

                try
                {
                    Handle(context);
                }
                catch (Exception e)
                {
                    // One bad request must never take the whole preview down.
                    try { WriteText(context, 500, "text/plain", "NowUI Web Preview: " + e.Message); }
                    catch (Exception) { }
                }
            }
        }

        private static void Handle(HttpListenerContext context)
        {
            string method = context.Request.HttpMethod;
            if (method != "GET" && method != "HEAD")
            {
                WriteText(context, 405, "text/plain", "The NowUI Web Preview serves GET only.");
                return;
            }

            string rawPath = context.Request.RawUrl ?? "/";
            int query = rawPath.IndexOf('?');
            if (query >= 0) rawPath = rawPath.Substring(0, query);
            rawPath = Uri.UnescapeDataString(rawPath);

            if (rawPath == "/__nowui/id")
            {
                WriteText(context, 200, "text/plain", NowWebPreviewPaths.ProjectRoot);
                return;
            }

            if (rawPath == "/__nowui/mtime")
            {
                HandleMtime(context);
                return;
            }

            if (!IsSafePath(rawPath))
            {
                WriteText(context, 400, "text/plain",
                    "The NowUI Web Preview refused the path '" + rawPath + "'. Paths may hold letters, digits, " +
                    "'.', '_', '-' and '/', and may not contain '..'.");
                return;
            }

            string relative = rawPath.TrimStart('/');
            if (relative.Length == 0) relative = "index.html";

            string resolved = Resolve(relative, out bool fromUserFolder);
            if (resolved == null)
            {
                WriteText(context, 404, "text/plain", NotFoundBody(relative));
                return;
            }

            ServeFile(context, resolved, fromUserFolder);
        }

        /// <summary>
        /// The one route that is not a plain static file, and the whole of <c>?watch=1</c>: the last-write time
        /// of the file <c>?app=NAME</c> would serve, so the page can reload itself when the author saves.
        /// </summary>
        private static void HandleMtime(HttpListenerContext context)
        {
            string app = context.Request.QueryString["app"];
            if (string.IsNullOrEmpty(app) || !IsSafeAppName(app))
            {
                WriteText(context, 400, "text/plain", "0");
                return;
            }

            string resolved = Resolve(app + ".js", out bool _);
            long ticks = resolved == null ? 0 : File.GetLastWriteTimeUtc(resolved).Ticks;
            WriteText(context, 200, "text/plain", ticks.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Where a request actually comes from, and the only interesting decision in this file.
        /// </summary>
        /// <remarks>
        /// The user's folder is checked FIRST for an application, the bundle SECOND. That ordering is what makes
        /// <c>?app=app</c> open the user's own file once seeded while the shipped sample stays as the fallback
        /// if they delete theirs, and it is what makes a package upgrade unable to overwrite their work: the
        /// package never writes to the folder that wins.
        ///
        /// An application is a bare <c>NAME.js</c> at the site root, because that is where the runtime looks for
        /// it (BridgeHost resolves <c>../NAME.js</c> from <c>_framework/</c>). <c>/apps/NAME.js</c> is accepted
        /// as well so the same server keeps working if a later bundle moves its samples into a subfolder.
        /// </remarks>
        private static string Resolve(string relative, out bool fromUserFolder)
        {
            fromUserFolder = false;

            string appCandidate = null;
            if (relative.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                if (relative.IndexOf('/') < 0 && !ReservedRootModules.Contains(relative))
                    appCandidate = relative;
                else if (relative.StartsWith("apps/", StringComparison.OrdinalIgnoreCase) &&
                         relative.IndexOf('/', 5) < 0)
                    appCandidate = relative.Substring(5);
            }

            if (appCandidate != null)
            {
                string user = Path.Combine(NowWebPreviewPaths.AppsRoot, appCandidate);
                if (IsInside(NowWebPreviewPaths.AppsRoot, user) && File.Exists(user))
                {
                    fromUserFolder = true;
                    return user;
                }
            }

            if (s_BundleRoot == null) return null;

            string bundled = Path.Combine(s_BundleRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (IsInside(s_BundleRoot, bundled))
            {
                // The LOGICAL path either way. The shipped bundle stores most files only as NAME.br to keep the
                // committed tree a third of its raw size; ServeFile is what knows about that, so that everything
                // upstream - the app shadowing, the mtime watch, the 404 text - keeps working in real names.
                if (File.Exists(bundled) || File.Exists(bundled + ".br")) return bundled;
            }

            // A request for /apps/NAME.js against a bundle that still keeps its samples at the root.
            if (appCandidate != null && relative.StartsWith("apps/", StringComparison.OrdinalIgnoreCase))
            {
                string flat = Path.Combine(s_BundleRoot, appCandidate);
                if (IsInside(s_BundleRoot, flat) && File.Exists(flat)) return flat;
            }

            return null;
        }

        private static string NotFoundBody(string relative)
        {
            var text = new StringBuilder();
            text.Append("NowUI Web Preview: '").Append(relative).Append("' was not found.\n\nLooked in:\n");
            if (relative.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                text.Append("  ").Append(Path.Combine(NowWebPreviewPaths.AppsRoot, Path.GetFileName(relative))).Append('\n');
            text.Append("  ").Append(Path.Combine(s_BundleRoot ?? "<no bundle>", relative.Replace('/', Path.DirectorySeparatorChar)));
            return text.ToString();
        }

        // -------------------------------------------------------------------------------------------- security

        /// <summary>
        /// Rejected before the filesystem is touched, and then checked again after resolution. This is loopback
        /// only, but one of the two served roots is a folder of the user's own source, and the check is short.
        /// </summary>
        private static bool IsSafePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/') return false;
            if (path.IndexOf("..", StringComparison.Ordinal) >= 0) return false;

            foreach (char c in path)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '.' || c == '_' || c == '-' || c == '/';
                if (!ok) return false;
            }

            return true;
        }

        private static bool IsSafeAppName(string app)
        {
            foreach (char c in app)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '_' || c == '-';
                if (!ok) return false;
            }

            return app.Length > 0;
        }

        private static bool IsInside(string root, string candidate)
        {
            try
            {
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string full = Path.GetFullPath(candidate);
                return full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(full, fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------------------------------------ i/o

        /// <summary>
        /// Serves one file, transparently un-brotli-ing a bundle that ships compressed.
        /// </summary>
        /// <remarks>
        /// <para>The shipped bundle stores its compressible files as <c>NAME.br</c> and does NOT keep the raw
        /// original, because that is what took the committed tree from 8,570,227 B to 2,845,658 B - a third of
        /// the size, losslessly, in a folder that lives in git forever. The saving is real on disk and in every
        /// clone; it is not a transfer optimisation, and it is worth doing even though this server runs on
        /// loopback where transfer is free.</para>
        /// <para>Two ways out, and the second is why this is safe. When the client sends
        /// <c>Accept-Encoding: br</c> - every browser released this decade does - the compressed bytes go out
        /// under <c>Content-Encoding: br</c> and the browser inflates them. When it does not, this inflates them
        /// here, so a client with no brotli still gets the file rather than a broken page.</para>
        /// <para>Integrity survives either way, which is the part worth stating plainly:
        /// <c>_framework/blazor.boot.json</c> carries SHA-256 SRI hashes of the RAW assemblies, and a browser
        /// checks integrity AFTER decoding a content encoding. So the hashes still match, and nothing under
        /// <c>_framework/</c> has to be excluded, rewritten or re-hashed.</para>
        /// </remarks>
        private static void ServeFile(HttpListenerContext context, string path, bool fromUserFolder)
        {
            // `path` is the logical name. The bundle may hold only the compressed sibling.
            string onDisk = path;
            bool brotli = false;

            if (!File.Exists(onDisk) && File.Exists(path + ".br"))
            {
                onDisk = path + ".br";
                brotli = true;
            }

            var info = new FileInfo(onDisk);

            if (fromUserFolder)
            {
                // The author's own file. F5 must always re-read it from disk; a cached copy here would mean
                // "I saved and nothing changed", which is the worst possible authoring bug.
                context.Response.Headers["Cache-Control"] = "no-store";
            }
            else
            {
                // The bundle's filenames carry no content hash any more (that is what keeps the committed bundle
                // cheap in git), so freshness is this server's job. no-cache + a strong ETag means the browser
                // revalidates on every load - free on loopback - and can never serve a stale body after the
                // bundle is rebuilt. This project has been bitten by the .NET loader's per-URL cache surviving
                // both a cache clear and a query-string bust; this puts that under our control.
                string etag = "\"" + info.Length.ToString("x", CultureInfo.InvariantCulture) + "-" +
                              info.LastWriteTimeUtc.Ticks.ToString("x", CultureInfo.InvariantCulture) + "\"";
                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.Headers["ETag"] = etag;

                if (string.Equals(context.Request.Headers["If-None-Match"], etag, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 304;
                    context.Response.Close();
                    return;
                }
            }

            context.Response.StatusCode = 200;

            // From the LOGICAL name: a .wasm stored as .wasm.br is still application/wasm, and calling it
            // application/brotli would stop the streaming instantiation the loader depends on.
            context.Response.ContentType = MimeFor(path);

            bool passThrough = brotli && AcceptsBrotli(context.Request.Headers["Accept-Encoding"]);
            if (passThrough)
                context.Response.Headers["Content-Encoding"] = "br";

            if (context.Request.HttpMethod == "HEAD")
            {
                // Only meaningful when the length on the wire is knowable, which for the inflate path it is not.
                if (!brotli || passThrough) context.Response.ContentLength64 = info.Length;
                context.Response.Close();
                return;
            }

            using (var source = new FileStream(onDisk, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (brotli && !passThrough)
                {
                    // No Content-Length: the inflated size is not known without inflating twice, and a chunked
                    // response is correct and costs nothing here.
                    using (var inflate = new BrotliStream(source, CompressionMode.Decompress))
                        Pump(inflate, context.Response.OutputStream);
                }
                else
                {
                    context.Response.ContentLength64 = info.Length;
                    Pump(source, context.Response.OutputStream);
                }
            }

            context.Response.Close();
        }

        private static void Pump(Stream from, Stream to)
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
                to.Write(buffer, 0, read);
        }

        /// <summary>
        /// Whether the client named brotli in Accept-Encoding, without matching a q=0 refusal.
        /// </summary>
        private static bool AcceptsBrotli(string header)
        {
            if (string.IsNullOrEmpty(header)) return false;

            foreach (string part in header.Split(','))
            {
                string token = part.Trim();
                if (token.Length == 0) continue;

                int semi = token.IndexOf(';');
                string name = (semi < 0 ? token : token.Substring(0, semi)).Trim();

                if (!string.Equals(name, "br", StringComparison.OrdinalIgnoreCase)) continue;

                // "br;q=0" is a refusal, and it is the one case where the name being present means the opposite.
                if (semi >= 0 && token.Substring(semi + 1).Replace(" ", string.Empty)
                        .StartsWith("q=0", StringComparison.OrdinalIgnoreCase) &&
                    !token.Substring(semi + 1).Replace(" ", string.Empty)
                        .StartsWith("q=0.", StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
            }

            return false;
        }

        private static void WriteText(HttpListenerContext context, int status, string contentType, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = contentType + "; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        /// <summary>
        /// Load-bearing, not decorative. Two of these are hard requirements rather than hints: the .NET loader
        /// instantiates the runtime with <c>WebAssembly.instantiateStreaming</c>, which REFUSES a response whose
        /// type is not <c>application/wasm</c>, and a browser refuses a module script whose type is not a
        /// JavaScript type. Both failures are silent apart from one console line.
        /// </summary>
        private static string MimeFor(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".wasm": return "application/wasm";
                case ".js":
                case ".mjs":  return "text/javascript";
                case ".json": return "application/json";
                case ".html":
                case ".htm":  return "text/html; charset=utf-8";
                case ".css":  return "text/css";
                case ".ttf":  return "font/ttf";
                case ".otf":  return "font/otf";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif":  return "image/gif";
                case ".svg":  return "image/svg+xml";
                case ".webp": return "image/webp";
                case ".ico":  return "image/x-icon";
                case ".txt":
                case ".frag":
                case ".vert":
                case ".glsl": return "text/plain; charset=utf-8";
                case ".map":  return "application/json";
                case ".dat":
                case ".bin":  return "application/octet-stream";
                default:      return "application/octet-stream";
            }
        }
    }
}
