// The standalone build's runtime root: where a host registers itself, where the frame boundaries are, and what a
// "domain reload" means without a domain.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§4.5 members, §6.1 frame contract, §6.2 ResetAll, §6.3 destroy
// drain, §6.4 static-initialiser safety, §6.6 shutdown).
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NowUI.Engine
{
    /// <summary>
    /// Process-wide state for the engine-free NowUI build. A browser frame is
    /// <c>BeginFrame(); using (Now.StartUI(scale)) app.Draw(); EndFrame();</c> and nothing else, because the standalone
    /// halves of the core subscribe themselves to <see cref="onFrame"/>.
    /// </summary>
    public static class NowRuntime
    {
        private static readonly List<Assembly> s_RegisteredAssemblies = new List<Assembly>();

        private static INowHostServices s_Host;
        private static INowRenderBackend s_Backend;
        private static NowShaderGlobals s_Globals;

        private static MethodInfo[] s_InitializeMethods;
        private static bool s_FrameStarted;
        private static double s_StartSeconds;
        private static double s_LastFrameSeconds;

        static NowRuntime()
        {
            // Installing the defaults here - and touching no core type while doing it - is what makes every shim static
            // reachable from a core static initialiser safe (design §6.4). There is no initialisation cycle: a
            // DefaultHostServices needs nothing but System, and a NullRenderBackend needs nothing at all.
            s_Host = new DefaultHostServices();
            s_Backend = new NullRenderBackend();
            s_Globals = new NowShaderGlobals();
        }

        /// <summary>The registered host. Never null; a <see cref="DefaultHostServices"/> stands in until one registers.</summary>
        public static INowHostServices host => s_Host;

        /// <summary>The registered backend. Never null; a <see cref="NullRenderBackend"/> stands in.</summary>
        public static INowRenderBackend backend => s_Backend;

        /// <summary>Global shader properties, the shim's stand-in for Unity's global material state.</summary>
        public static NowShaderGlobals globals => s_Globals;

        /// <summary>
        /// What the framebuffer expects. Gamma by default (design H.14) - NowUI's colour maths is written for the gamma
        /// pipeline, and a linear host sets this before the first frame.
        /// </summary>
        public static UnityEngine.ColorSpace colorSpace { get; set; } = UnityEngine.ColorSpace.Gamma;

        /// <summary>
        /// Whether the runtime behaves like a player. True by default, which is what a browser host wants; the tests
        /// host sets false so every core site that branches on <c>Application.isPlaying</c> takes its edit-mode path.
        /// </summary>
        public static bool isPlaying { get; set; } = true;

        /// <summary>
        /// Whether sealing a mesh or texture may drop its CPU copy. False keeps the CPU stores, so a backend can
        /// re-upload everything after a WebGL context loss without the core rebuilding it.
        /// </summary>
        public static bool releaseCpuCopiesOnSeal { get; set; }

        /// <summary>
        /// Whether <c>Object.Destroy</c> defers to <see cref="EndFrame"/> while playing, as Unity does (design §6.3).
        /// Kept as a switch purely for bisecting a suspected timing difference; leave it true.
        /// </summary>
        public static bool deferDestroyToEndOfFrame { get; set; } = true;

        /// <summary>Where <c>ProfilerMarker</c> scopes go. Null means the markers cost a null check and nothing else.</summary>
        public static INowProfilerSink profilerSink { get; set; }

        /// <summary>
        /// Raised once per <see cref="BeginFrame"/>, after the backend's own frame start. The standalone halves of the
        /// core (<c>NowLottieCache.Tick</c>, <c>NowMarkdownImages.Tick</c>) subscribe themselves here, so no host has
        /// to know their names.
        /// </summary>
        public static event Action onFrame;

        /// <summary>Frames begun so far. 0 before the first <see cref="BeginFrame"/> (design §6.1).</summary>
        internal static int frameCount { get; private set; }

        /// <summary>Seconds since the first <see cref="BeginFrame"/>, latched per frame. Backs <c>Time.time</c>.</summary>
        internal static double timeSeconds { get; private set; }

        /// <summary>Seconds between the last two frame starts, clamped to zero on the first frame. Backs <c>Time.deltaTime</c>.</summary>
        internal static float deltaTime { get; private set; }

        /// <summary>
        /// The clock reading the first frame started at. <c>Time.realtimeSinceStartup</c> subtracts it at <i>call</i>
        /// time rather than reading a latched value, because the suite sleeps inside a test and compares (design §6.1).
        /// </summary>
        internal static double startSeconds => s_StartSeconds;

        /// <summary>
        /// Engine-internal frame start, raised before the backend's. The facades that own per-frame counters in other
        /// files (the <c>GUIUtility.GetControlID</c> counter) subscribe here rather than being named by this file.
        /// </summary>
        internal static event Action onEngineBeginFrame;

        /// <summary>Engine-internal frame end, raised after the destroy drain (the temporary-RT pool trims here).</summary>
        internal static event Action onEngineEndFrame;

        /// <summary>Engine-internal reset, raised by <see cref="ResetAll"/> after the core's own reset methods ran.</summary>
        internal static event Action onEngineReset;

        /// <summary>Engine-internal quit, raised first by <see cref="Shutdown"/>; <c>Application.quitting</c> forwards it.</summary>
        internal static event Action onEngineQuitting;

        /// <summary>
        /// Registers the host and the backend. Passing null for either restores that half's default, so
        /// <see cref="host"/> and <see cref="backend"/> are never null.
        /// </summary>
        public static void Initialize(INowHostServices host, INowRenderBackend backend)
        {
            s_Host = host ?? new DefaultHostServices();
            s_Backend = backend ?? new NullRenderBackend();

            // A new host means a new clock, so the frame timing restarts rather than reporting a delta measured
            // against the previous host's timeline.
            s_FrameStarted = false;
            s_StartSeconds = 0d;
            s_LastFrameSeconds = 0d;
            timeSeconds = 0d;
            deltaTime = 0f;
        }

        /// <summary>
        /// Declares an assembly that <see cref="ResetAll"/> should scan for <c>[RuntimeInitializeOnLoadMethod]</c>.
        /// Registering even one assembly replaces the domain-wide scan, which is the point: a browser host knows its
        /// own assemblies and should not pay for a reflection walk of everything loaded.
        /// </summary>
        public static void RegisterAssembly(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            if (s_RegisteredAssemblies.Contains(assembly))
                return;

            s_RegisteredAssemblies.Add(assembly);
            s_InitializeMethods = null;
        }

        /// <summary>
        /// Starts a frame: advances the frame counter, samples the clock into the time fields, resets the per-frame
        /// engine counters, starts the backend's frame and raises <see cref="onFrame"/> (design §6.1).
        /// </summary>
        public static void BeginFrame()
        {
            frameCount++;

            double now = s_Host.clock.realtimeSeconds;
            if (!s_FrameStarted)
            {
                s_FrameStarted = true;
                s_StartSeconds = now;
                deltaTime = 0f;
            }
            else
            {
                double delta = now - s_LastFrameSeconds;
                // A host clock that goes backwards (a wall clock rather than a monotonic one) must not produce a
                // negative deltaTime: core animation code multiplies by it and would run in reverse.
                deltaTime = delta > 0d ? (float)delta : 0f;
            }

            s_LastFrameSeconds = now;
            timeSeconds = now - s_StartSeconds;

            // The SmoothDamp overloads that omit deltaTime read EngineClock, not Time, so it has to be pushed here.
            EngineClock.deltaTime = deltaTime;

            Action engineBegin = onEngineBeginFrame;
            if (engineBegin != null)
                engineBegin();

            s_Backend.BeginFrame(frameCount);

            Action frame = onFrame;
            if (frame != null)
                frame();
        }

        /// <summary>
        /// Ends a frame: drains the deferred-destroy queue, trims the engine's per-frame pools, and ends the backend's
        /// frame (design §6.1, §6.3).
        /// </summary>
        public static void EndFrame()
        {
            UnityEngine.Object.DrainDestroyQueue(timeSeconds);

            Action engineEnd = onEngineEndFrame;
            if (engineEnd != null)
                engineEnd();

            s_Backend.EndFrame();
        }

        /// <summary>
        /// The standalone equivalent of a Unity domain reload (design §6.2): run every
        /// <c>[RuntimeInitializeOnLoadMethod]</c> in the registered assemblies in Unity's load-type order, then reset
        /// the engine's own state. No better and no worse than a domain reload - statics the core never resets stay
        /// un-reset here too.
        /// </summary>
        public static void ResetAll()
        {
            MethodInfo[] methods = s_InitializeMethods;
            if (methods == null)
            {
                methods = CollectInitializeMethods();
                s_InitializeMethods = methods;
            }

            for (int i = 0; i < methods.Length; i++)
                InvokeInitializeMethod(methods[i]);

            // Engine-side resets run after the core's, so anything a core reset method destroys still releases through
            // a live backend (design §6.2 step 4).
            s_Globals.Reset();
            UnityEngine.Object.ClearDestroyQueue();
            UnityEngine.ScriptableObject.ClearMessageCache();

            Action reset = onEngineReset;
            if (reset != null)
                reset();

            // Deliberately NOT reset: Shader.PropertyToID's intern table. Core types cache ids in static readonly
            // fields, and Unity does not reset the table across a domain reload either (design §6.2 step 3).
        }

        /// <summary>
        /// Tears the runtime down (design §6.6): raise the quit event, reset, destroy everything still tracked so the
        /// backend releases it, then restore the default host and the null backend.
        /// </summary>
        public static void Shutdown()
        {
            Action quitting = onEngineQuitting;
            if (quitting != null)
                quitting();

            ResetAll();

            UnityEngine.Object.DestroyAllLive();

            s_Host = new DefaultHostServices();
            s_Backend = new NullRenderBackend();
            s_Globals = new NowShaderGlobals();

            onFrame = null;
            s_FrameStarted = false;
            s_StartSeconds = 0d;
            s_LastFrameSeconds = 0d;
            timeSeconds = 0d;
            deltaTime = 0f;
            frameCount = 0;
        }

        /// <summary>
        /// Logs an exception thrown by a dispatched lifecycle message. Central so the ScriptableObject dispatcher does
        /// not have to know how a host reports errors.
        /// </summary>
        internal static void LogMessageException(MethodInfo method, UnityEngine.Object context, Exception exception)
        {
            INowLogger logger = s_Host.logger;
            if (logger == null)
                return;

            string where = method == null
                ? "a lifecycle message"
                : (method.DeclaringType == null ? method.Name : method.DeclaringType.FullName + "." + method.Name);
            logger.Log(UnityEngine.LogType.Exception, "Exception thrown by " + where + ".", exception, context);
        }

        private static void InvokeInitializeMethod(MethodInfo method)
        {
            // Checked at invoke time rather than filtered out at collection time, so the warning is repeated on every
            // ResetAll: a mis-declared initializer is a standing problem, not a one-off startup notice (design §6.2).
            if (!method.IsStatic || method.GetParameters().Length != 0)
            {
                INowLogger logger = s_Host.logger;
                if (logger != null)
                {
                    logger.Log(
                        UnityEngine.LogType.Warning,
                        "[NowUI] Skipping [RuntimeInitializeOnLoadMethod] " + Describe(method) +
                        ": it must be static and take no parameters.",
                        null,
                        null);
                }

                return;
            }

            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException e)
            {
                LogMessageException(method, null, e.InnerException ?? e);
            }
        }

        private static MethodInfo[] CollectInitializeMethods()
        {
            List<Assembly> assemblies = s_RegisteredAssemblies.Count > 0
                ? s_RegisteredAssemblies
                : ScanDomainForEngineReferences();

            List<Entry> entries = new List<Entry>();
            for (int a = 0; a < assemblies.Count; a++)
                CollectFromAssembly(assemblies[a], entries);

            // Unity's order, then declaring type, then method name: the reset has to be reproducible run to run, and
            // reflection does not promise a stable member order.
            entries.Sort(CompareEntries);

            MethodInfo[] result = new MethodInfo[entries.Count];
            for (int i = 0; i < entries.Count; i++)
                result[i] = entries[i].method;
            return result;
        }

        private static void CollectFromAssembly(Assembly assembly, List<Entry> entries)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                // A partially loadable assembly still contributes the types that did load; failing the whole reset
                // because one unrelated type could not be resolved would be worse than skipping it.
                types = e.Types;
            }
            catch (Exception)
            {
                return;
            }

            if (types == null)
                return;

            for (int t = 0; t < types.Length; t++)
            {
                Type type = types[t];
                if (type == null)
                    continue;

                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(
                        BindingFlags.Static | BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch (Exception)
                {
                    continue;
                }

                for (int m = 0; m < methods.Length; m++)
                {
                    MethodInfo method = methods[m];
                    UnityEngine.RuntimeInitializeOnLoadMethodAttribute attribute;
                    try
                    {
                        attribute = (UnityEngine.RuntimeInitializeOnLoadMethodAttribute)Attribute.GetCustomAttribute(
                            method, typeof(UnityEngine.RuntimeInitializeOnLoadMethodAttribute), inherit: false);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (attribute == null)
                        continue;

                    entries.Add(new Entry(method, LoadTypeRank(attribute.loadType)));
                }
            }
        }

        private static List<Assembly> ScanDomainForEngineReferences()
        {
            List<Assembly> found = new List<Assembly>();
            Assembly self = typeof(NowRuntime).Assembly;
            string engineName = self.GetName().Name;

            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loaded.Length; i++)
            {
                Assembly assembly = loaded[i];
                if (assembly.IsDynamic)
                    continue;

                if (assembly == self)
                    continue;

                AssemblyName[] references;
                try
                {
                    references = assembly.GetReferencedAssemblies();
                }
                catch (Exception)
                {
                    continue;
                }

                for (int r = 0; r < references.Length; r++)
                {
                    if (!string.Equals(references[r].Name, engineName, StringComparison.Ordinal))
                        continue;

                    found.Add(assembly);
                    break;
                }
            }

            return found;
        }

        /// <summary>Unity runs the load types in this order; the numeric enum values are not that order.</summary>
        private static int LoadTypeRank(UnityEngine.RuntimeInitializeLoadType loadType)
        {
            switch (loadType)
            {
                case UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration: return 0;
                case UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded: return 1;
                case UnityEngine.RuntimeInitializeLoadType.BeforeSplashScreen: return 2;
                case UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad: return 3;
                default: return 4;
            }
        }

        private static int CompareEntries(Entry a, Entry b)
        {
            if (a.rank != b.rank)
                return a.rank < b.rank ? -1 : 1;

            int byType = string.CompareOrdinal(TypeName(a.method), TypeName(b.method));
            if (byType != 0)
                return byType;

            return string.CompareOrdinal(a.method.Name, b.method.Name);
        }

        private static string TypeName(MethodInfo method)
        {
            return method.DeclaringType == null ? "" : (method.DeclaringType.FullName ?? "");
        }

        private static string Describe(MethodInfo method)
        {
            return TypeName(method) + "." + method.Name;
        }

        private readonly struct Entry
        {
            public readonly MethodInfo method;
            public readonly int rank;

            public Entry(MethodInfo method, int rank)
            {
                this.method = method;
                this.rank = rank;
            }
        }
    }
}
