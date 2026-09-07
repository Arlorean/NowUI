// Mirrors UnityEngine.Shader for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 (`Shader.cs` member list), §3.10 (NowShaderInfo,
// NowShaderGlobals), §4.1 (backend contract), §6.2 step 3 (the intern table is never reset), §6.4 (static-initialiser
// safety). Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.Shader`.
//
// The load-bearing member here is PropertyToID. NowUI calls it from *static field initialisers* in thirteen files
// (Now.cs:852, NowMaskShader.cs:290-300, NowRenderer.cs:18-20, NowGlassRenderer.cs:170-187, NowFontCompiler.cs:44,
// NowFont.cs:1219-1220, NowSdf.cs:118 and 3290-3322, NowSdfImageField.cs:100-105, NowGradient.cs, NowWorldGraphic.cs,
// NowWorldGlassBackdrop.cs:22, NowGraphic.cs:93-107), so it has to answer correctly before any host, backend or render
// context exists — hazard D.1 #4. That is why the intern table is a plain static dictionary that touches nothing else,
// and why it is never reset: those cached ids outlive every ResetAll (design §6.2 step 3).
using System;
using System.Collections.Generic;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// A shader program handle. Instances come from the host's <c>INowResourceProvider</c> — there is no shader
    /// compiler on this side of the build, so a <see cref="Shader"/> is a name plus the declared-uniform table the
    /// provider parsed out of its asset bundle.
    /// </summary>
    public sealed class Shader : Object
    {
        // The intern table. A lock rather than a ConcurrentDictionary because it is contended only during start-up
        // (static initialisers) and read through a plain dictionary afterwards; the lock costs nothing uncontended and
        // allocates nothing. Ids start at 1 so that 0 stays available as "no property" for backends.
        private static readonly object s_InternLock = new object();
        private static readonly Dictionary<string, int> s_NameToID = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly List<string> s_IDToName = new List<string>();

        private readonly NowShaderInfo m_Info;

        /// <summary>
        /// Builds a shader handle over a provider-supplied description. Internal because only the shim and a resource
        /// provider inside the shim's friend assemblies may mint one; under Unity, shaders come from the asset
        /// database and there is no public constructor either.
        /// </summary>
        internal Shader(string name, NowShaderInfo info)
        {
            m_Info = info ?? new NowShaderInfo(name);

            // Keep the two names in step: the provider may have built the info without a name, and Object.name is what
            // the backend resolves the GPU program by (design §4.1, ResolveShader).
            if (string.IsNullOrEmpty(m_Info.name))
                m_Info.name = name ?? "";

            this.name = name ?? m_Info.name;
        }

        /// <summary>The declared surface of this program: pass count, uniform declarations, keywords.</summary>
        internal NowShaderInfo info
        {
            get { return m_Info; }
        }

        /// <summary>
        /// Interns <paramref name="name"/> into a process-wide table and returns its id. Ids start at 1, are stable for
        /// the life of the process and are <b>never</b> recycled or reset — <c>NowRuntime.ResetAll</c> deliberately
        /// leaves this table alone, because core types cache ids in <c>static readonly</c> fields that a reset cannot
        /// re-run (design §6.2 step 3).
        /// </summary>
        public static int PropertyToID(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            lock (s_InternLock)
            {
                int id;
                if (s_NameToID.TryGetValue(name, out id))
                    return id;

                id = s_IDToName.Count + 1;
                s_IDToName.Add(name);
                s_NameToID.Add(name, id);
                return id;
            }
        }

        /// <summary>
        /// The name an id was interned from, or null for an id this process never issued. For backends (which need the
        /// uniform name to bind by) and for tests.
        /// </summary>
        internal static string IDToName(int id)
        {
            lock (s_InternLock)
            {
                if (id < 1 || id > s_IDToName.Count)
                    return null;

                return s_IDToName[id - 1];
            }
        }

        /// <summary>Ids issued so far. Test-only: it is the one way to observe that the table is never reset.</summary>
        internal static int propertyCount
        {
            get
            {
                lock (s_InternLock)
                    return s_IDToName.Count;
            }
        }

        /// <summary>
        /// Resolves a shader by its Unity name (<c>"NowUI/UI Bezier"</c>, <c>"Hidden/NowUI/SDF Image Field"</c>, ...)
        /// through the host's resource provider. Null when the host does not know the name, which is exactly the
        /// branch NowUI's own resolvers already handle.
        /// </summary>
        public static Shader Find(string name)
        {
            if (name == null)
                return null;

            INowResourceProvider resources = NowRuntime.host.resources;
            if (resources == null)
                return null;

            return resources.FindShader(name);
        }

        public static void SetGlobalFloat(int nameID, float value)
        {
            NowRuntime.globals.SetFloat(nameID, value);
        }

        public static void SetGlobalFloat(string name, float value)
        {
            NowRuntime.globals.SetFloat(PropertyToID(name), value);
        }

        public static void SetGlobalInt(int nameID, int value)
        {
            NowRuntime.globals.SetInt(nameID, value);
        }

        public static void SetGlobalInt(string name, int value)
        {
            NowRuntime.globals.SetInt(PropertyToID(name), value);
        }

        public static void SetGlobalInteger(int nameID, int value)
        {
            NowRuntime.globals.SetInt(nameID, value);
        }

        public static void SetGlobalInteger(string name, int value)
        {
            NowRuntime.globals.SetInt(PropertyToID(name), value);
        }

        public static void SetGlobalVector(int nameID, Vector4 value)
        {
            NowRuntime.globals.SetVector(nameID, value);
        }

        public static void SetGlobalVector(string name, Vector4 value)
        {
            NowRuntime.globals.SetVector(PropertyToID(name), value);
        }

        /// <summary>
        /// Colors and vectors share one slot, as they do in Unity: a shader's <c>float4</c> uniform does not know
        /// whether the author called it a color, so <c>GetGlobalVector</c> reads back what <c>SetGlobalColor</c> wrote.
        /// </summary>
        public static void SetGlobalColor(int nameID, Color value)
        {
            NowRuntime.globals.SetVector(nameID, new Vector4(value.r, value.g, value.b, value.a));
        }

        public static void SetGlobalColor(string name, Color value)
        {
            SetGlobalColor(PropertyToID(name), value);
        }

        public static void SetGlobalMatrix(int nameID, Matrix4x4 value)
        {
            NowRuntime.globals.SetMatrix(nameID, value);
        }

        public static void SetGlobalMatrix(string name, Matrix4x4 value)
        {
            NowRuntime.globals.SetMatrix(PropertyToID(name), value);
        }

        public static void SetGlobalTexture(int nameID, Texture value)
        {
            NowRuntime.globals.SetTexture(nameID, value);
        }

        public static void SetGlobalTexture(string name, Texture value)
        {
            NowRuntime.globals.SetTexture(PropertyToID(name), value);
        }

        public static void SetGlobalVectorArray(int nameID, Vector4[] values)
        {
            NowRuntime.globals.SetVectorArray(nameID, values);
        }

        public static void SetGlobalVectorArray(string name, Vector4[] values)
        {
            NowRuntime.globals.SetVectorArray(PropertyToID(name), values);
        }

        public static void SetGlobalFloatArray(int nameID, float[] values)
        {
            NowRuntime.globals.SetFloatArray(nameID, values);
        }

        public static void SetGlobalFloatArray(string name, float[] values)
        {
            NowRuntime.globals.SetFloatArray(PropertyToID(name), values);
        }

        public static float GetGlobalFloat(int nameID)
        {
            return NowRuntime.globals.GetFloat(nameID);
        }

        public static float GetGlobalFloat(string name)
        {
            return NowRuntime.globals.GetFloat(PropertyToID(name));
        }

        public static int GetGlobalInt(int nameID)
        {
            return NowRuntime.globals.GetInt(nameID);
        }

        public static int GetGlobalInt(string name)
        {
            return NowRuntime.globals.GetInt(PropertyToID(name));
        }

        public static Vector4 GetGlobalVector(int nameID)
        {
            return NowRuntime.globals.GetVector(nameID);
        }

        public static Vector4 GetGlobalVector(string name)
        {
            return NowRuntime.globals.GetVector(PropertyToID(name));
        }

        public static Color GetGlobalColor(int nameID)
        {
            Vector4 v = NowRuntime.globals.GetVector(nameID);
            return new Color(v.x, v.y, v.z, v.w);
        }

        public static Matrix4x4 GetGlobalMatrix(int nameID)
        {
            return NowRuntime.globals.GetMatrix(nameID);
        }

        public static Texture GetGlobalTexture(int nameID)
        {
            return NowRuntime.globals.GetTexture(nameID);
        }
    }
}
