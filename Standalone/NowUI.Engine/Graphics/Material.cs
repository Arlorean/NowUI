// Mirrors UnityEngine.Material for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 (`Material.cs` member list and the SetVectorArray rule),
// §3.10 (NowMaterialBag, NowShaderInfo), §4.1 (backend contract), §1.2 (allocation rule).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.Material`; hazard D.1 #10.
//
// Three behaviours here look odd and are deliberate:
//   * `mainTexture`'s getter returns the *stored instance*, not a copy or a re-resolution, because NowGradient
//     (NowGradient.cs:675/678) does `ReferenceEquals(_material.mainTexture, atlas)` to decide whether to re-upload.
//   * `SetVectorArray` copies into a bag-owned array of the caller's length. NowMaskShader and NowSdf hand it *static
//     scratch arrays* (8/2 and 64/16 entries) that they overwrite next frame, so keeping the caller's reference would
//     alias every material in the process onto one buffer.
//   * `HasProperty` is true for a property the shader declares but nobody has set, because that is what Unity answers
//     and NowUI gates real work on it (Now.cs:1020, NowFont.cs:1478, NowSdf.cs:3508).
using System;
using System.Collections.Generic;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// A shader plus a bag of property values. Under Unity this is a native property sheet; here it is a
    /// <see cref="NowMaterialBag"/>, which a backend reads directly at bind time.
    /// </summary>
    public class Material : Object
    {
        // Interned once. Safe in a static initialiser: PropertyToID touches nothing but its own table (design §6.4).
        private static readonly int k_MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int k_MainTexST = Shader.PropertyToID("_MainTex_ST");

        private readonly NowMaterialBag m_Bag = new NowMaterialBag();
        private Shader m_Shader;
        private int m_RenderQueue = -1;

        /// <summary>
        /// Bumped on every setter. A backend keys its resolved-uniform cache on <c>(GetInstanceID(), version)</c> and
        /// skips the re-push when it has not moved (design §4.1 guarantee 1).
        /// </summary>
        internal uint version;

        /// <summary>
        /// Creates a material for <paramref name="shader"/>, seeded with the shader's declared defaults and named after
        /// it — which is what Unity's own <c>new Material(shader)</c> produces.
        /// </summary>
        public Material(Shader shader)
        {
            m_Shader = shader;

            if (!ReferenceEquals(shader, null))
            {
                name = shader.name;
                SeedFromShaderDefaults(shader.info);
            }
        }

        /// <summary>
        /// Copy constructor: a deep copy of the property bag (arrays included), the keywords, the shader and the name.
        /// Unity keeps the source's name here; only <c>Object.Instantiate</c> appends "(Clone)".
        /// </summary>
        public Material(Material source)
        {
            if (ReferenceEquals(source, null))
                throw new ArgumentNullException(nameof(source));

            m_Shader = source.m_Shader;
            m_RenderQueue = source.m_RenderQueue;
            m_Bag.CopyFrom(source.m_Bag);
            name = source.name;
        }

        /// <summary>
        /// The shader this material draws with. NowUI identity-compares it (<c>Now.cs:953-1015</c>) to decide whether a
        /// cached material can be reused, so the setter stores the reference as given and does not re-seed defaults —
        /// re-seeding would clobber values the caller has already set, which Unity does not do either.
        /// </summary>
        public Shader shader
        {
            get { return m_Shader; }
            set
            {
                m_Shader = value;
                version++;
                m_Bag.version++;
            }
        }

        /// <summary>
        /// The <c>_MainTex</c> slot. The getter returns the identical instance the setter stored (hazard D.1 #10):
        /// <c>NowGradient</c> compares it with <c>ReferenceEquals</c> against its atlas.
        /// </summary>
        public Texture mainTexture
        {
            get { return GetTexture(k_MainTex); }
            set { SetTexture(k_MainTex, value); }
        }

        /// <summary>
        /// The offset half of <c>_MainTex_ST</c>, which Unity packs as <c>(scale.x, scale.y, offset.x, offset.y)</c>.
        /// Unset reads as zero offset with unit scale, which is the identity transform a shader assumes.
        /// </summary>
        public Vector2 mainTextureOffset
        {
            get
            {
                Vector4 st = GetTextureST();
                return new Vector2(st.z, st.w);
            }
            set
            {
                Vector4 st = GetTextureST();
                SetVector(k_MainTexST, new Vector4(st.x, st.y, value.x, value.y));
            }
        }

        /// <summary>The scale half of <c>_MainTex_ST</c>. Unset reads as <c>(1, 1)</c>.</summary>
        public Vector2 mainTextureScale
        {
            get
            {
                Vector4 st = GetTextureST();
                return new Vector2(st.x, st.y);
            }
            set
            {
                Vector4 st = GetTextureST();
                SetVector(k_MainTexST, new Vector4(value.x, value.y, st.z, st.w));
            }
        }

        /// <summary>Passes the shader declares. Zero when the material has no shader at all.</summary>
        public int passCount
        {
            get { return ReferenceEquals(m_Shader, null) ? 0 : m_Shader.info.passCount; }
        }

        /// <summary>
        /// The render queue override. -1 means "use the shader's queue"; the shim has no shader queue metadata, so it
        /// reports the override verbatim and a backend that cares reads its own program table.
        /// </summary>
        public int renderQueue
        {
            get { return m_RenderQueue; }
            set
            {
                m_RenderQueue = value;
                version++;
            }
        }

        /// <summary>The property bag, for backends and for <c>CopyPropertiesFromMaterial</c>.</summary>
        internal NowMaterialBag bag
        {
            get { return m_Bag; }
        }

        /// <summary>
        /// True when the bag holds <paramref name="nameID"/> <b>or</b> the shader declares it. The second half is the
        /// one that matters: NowUI asks before setting optional uniforms, and a bag-only answer would make every
        /// optional feature look absent until something had already set it.
        /// </summary>
        public bool HasProperty(int nameID)
        {
            if (m_Bag.Contains(nameID))
                return true;

            return !ReferenceEquals(m_Shader, null) && m_Shader.info.DeclaresProperty(nameID);
        }

        public bool HasProperty(string name)
        {
            return HasProperty(Shader.PropertyToID(name));
        }

        public float GetFloat(int nameID)
        {
            float value;
            return m_Bag.floats.TryGetValue(nameID, out value) ? value : 0f;
        }

        public float GetFloat(string name)
        {
            return GetFloat(Shader.PropertyToID(name));
        }

        public void SetFloat(int nameID, float value)
        {
            m_Bag.SetFloat(nameID, value);
            version++;
        }

        public void SetFloat(string name, float value)
        {
            SetFloat(Shader.PropertyToID(name), value);
        }

        /// <summary>
        /// Unity's legacy integer setter, which writes the <i>float</i> table — in a shader an int property and a float
        /// property are the same register. <see cref="SetInteger(int, int)"/> is the one that writes a real integer
        /// uniform. Keeping the split is what makes <c>GetFloat</c> see a value written by <c>SetInt</c>, as it does
        /// under Unity.
        /// </summary>
        public void SetInt(int nameID, int value)
        {
            m_Bag.SetFloat(nameID, value);
            version++;
        }

        public void SetInt(string name, int value)
        {
            SetInt(Shader.PropertyToID(name), value);
        }

        /// <summary>Reads the float table and truncates, which is what Unity's <c>GetInt</c> does.</summary>
        public int GetInt(int nameID)
        {
            return (int)GetFloat(nameID);
        }

        public int GetInt(string name)
        {
            return GetInt(Shader.PropertyToID(name));
        }

        /// <summary>Writes a real integer uniform (Unity 2022+ <c>SetInteger</c>), stored apart from the floats.</summary>
        public void SetInteger(int nameID, int value)
        {
            m_Bag.SetInt(nameID, value);
            version++;
        }

        public void SetInteger(string name, int value)
        {
            SetInteger(Shader.PropertyToID(name), value);
        }

        public int GetInteger(int nameID)
        {
            int value;
            return m_Bag.ints.TryGetValue(nameID, out value) ? value : 0;
        }

        public int GetInteger(string name)
        {
            return GetInteger(Shader.PropertyToID(name));
        }

        public Vector4 GetVector(int nameID)
        {
            Vector4 value;
            return m_Bag.vectors.TryGetValue(nameID, out value) ? value : Vector4.zero;
        }

        public Vector4 GetVector(string name)
        {
            return GetVector(Shader.PropertyToID(name));
        }

        public void SetVector(int nameID, Vector4 value)
        {
            m_Bag.SetVector(nameID, value);
            version++;
        }

        public void SetVector(string name, Vector4 value)
        {
            SetVector(Shader.PropertyToID(name), value);
        }

        /// <summary>
        /// Colors share the vector table, as they do in Unity: the shader's <c>float4</c> does not know it was named a
        /// color, so <c>GetVector</c> reads back what <c>SetColor</c> wrote and vice versa.
        /// </summary>
        public Color GetColor(int nameID)
        {
            Vector4 v = GetVector(nameID);
            return new Color(v.x, v.y, v.z, v.w);
        }

        public Color GetColor(string name)
        {
            return GetColor(Shader.PropertyToID(name));
        }

        public void SetColor(int nameID, Color value)
        {
            SetVector(nameID, new Vector4(value.r, value.g, value.b, value.a));
        }

        public void SetColor(string name, Color value)
        {
            SetColor(Shader.PropertyToID(name), value);
        }

        public Texture GetTexture(int nameID)
        {
            Texture value;
            return m_Bag.textures.TryGetValue(nameID, out value) ? value : null;
        }

        public Texture GetTexture(string name)
        {
            return GetTexture(Shader.PropertyToID(name));
        }

        public void SetTexture(int nameID, Texture value)
        {
            m_Bag.SetTexture(nameID, value);
            version++;
        }

        public void SetTexture(string name, Texture value)
        {
            SetTexture(Shader.PropertyToID(name), value);
        }

        /// <summary>Unset reads as the zero matrix, which is Unity's answer for a property it has no value for.</summary>
        public Matrix4x4 GetMatrix(int nameID)
        {
            Matrix4x4 value;
            return m_Bag.matrices.TryGetValue(nameID, out value) ? value : Matrix4x4.zero;
        }

        public Matrix4x4 GetMatrix(string name)
        {
            return GetMatrix(Shader.PropertyToID(name));
        }

        public void SetMatrix(int nameID, Matrix4x4 value)
        {
            m_Bag.SetMatrix(nameID, value);
            version++;
        }

        public void SetMatrix(string name, Matrix4x4 value)
        {
            SetMatrix(Shader.PropertyToID(name), value);
        }

        /// <summary>
        /// Copies <paramref name="values"/> into a bag-owned array of exactly <c>values.Length</c> entries. The caller
        /// keeps ownership of its array — NowMaskShader and NowSdf pass static scratch buffers they reuse every frame.
        /// The stored buffer is reused when the length matches, so the steady-state call allocates nothing.
        /// </summary>
        public void SetVectorArray(int nameID, Vector4[] values)
        {
            m_Bag.SetVectorArray(nameID, values);
            version++;
        }

        public void SetVectorArray(string name, Vector4[] values)
        {
            SetVectorArray(Shader.PropertyToID(name), values);
        }

        public void SetVectorArray(int nameID, List<Vector4> values)
        {
            m_Bag.SetVectorArray(nameID, values);
            version++;
        }

        public void SetVectorArray(string name, List<Vector4> values)
        {
            SetVectorArray(Shader.PropertyToID(name), values);
        }

        /// <summary>
        /// A fresh array of exactly the stored length, or null when the property was never set. A copy, because Unity
        /// returns one and because handing the bag's buffer out would let a caller mutate the material behind its back.
        /// </summary>
        public Vector4[] GetVectorArray(int nameID)
        {
            Vector4[] stored;
            if (!m_Bag.vectorArrays.TryGetValue(nameID, out stored))
                return null;

            Vector4[] copy = new Vector4[stored.Length];
            Array.Copy(stored, copy, stored.Length);
            return copy;
        }

        public Vector4[] GetVectorArray(string name)
        {
            return GetVectorArray(Shader.PropertyToID(name));
        }

        /// <summary>
        /// Fills <paramref name="values"/> with the stored array, clearing it first — Unity's list getters always leave
        /// the list holding exactly the stored count, never appending to what was there.
        /// </summary>
        public void GetVectorArray(int nameID, List<Vector4> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            values.Clear();

            Vector4[] stored;
            if (!m_Bag.vectorArrays.TryGetValue(nameID, out stored))
                return;

            for (int i = 0; i < stored.Length; i++)
                values.Add(stored[i]);
        }

        public void SetFloatArray(int nameID, float[] values)
        {
            m_Bag.SetFloatArray(nameID, values);
            version++;
        }

        public void SetFloatArray(string name, float[] values)
        {
            SetFloatArray(Shader.PropertyToID(name), values);
        }

        public void SetFloatArray(int nameID, List<float> values)
        {
            m_Bag.SetFloatArray(nameID, values);
            version++;
        }

        public void SetFloatArray(string name, List<float> values)
        {
            SetFloatArray(Shader.PropertyToID(name), values);
        }

        public float[] GetFloatArray(int nameID)
        {
            float[] stored;
            if (!m_Bag.floatArrays.TryGetValue(nameID, out stored))
                return null;

            float[] copy = new float[stored.Length];
            Array.Copy(stored, copy, stored.Length);
            return copy;
        }

        public float[] GetFloatArray(string name)
        {
            return GetFloatArray(Shader.PropertyToID(name));
        }

        public void GetFloatArray(int nameID, List<float> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            values.Clear();

            float[] stored;
            if (!m_Bag.floatArrays.TryGetValue(nameID, out stored))
                return;

            for (int i = 0; i < stored.Length; i++)
                values.Add(stored[i]);
        }

        /// <summary>
        /// Replaces this material's property values and keywords with <paramref name="mat"/>'s. The shader is
        /// <b>not</b> copied: <c>Now.GetTexturedMaterial</c> (Now.cs:953-1015) checks shader identity separately and
        /// then calls this, so copying the shader here would make that check meaningless.
        /// </summary>
        public void CopyPropertiesFromMaterial(Material mat)
        {
            if (ReferenceEquals(mat, null))
                throw new ArgumentNullException(nameof(mat));

            if (ReferenceEquals(mat, this))
                return;

            m_Bag.CopyFrom(mat.m_Bag);
            version++;
        }

        public void EnableKeyword(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
                return;

            if (m_Bag.keywords.Add(keyword))
            {
                m_Bag.version++;
                version++;
            }
        }

        public void DisableKeyword(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
                return;

            if (m_Bag.keywords.Remove(keyword))
            {
                m_Bag.version++;
                version++;
            }
        }

        public bool IsKeywordEnabled(string keyword)
        {
            return !string.IsNullOrEmpty(keyword) && m_Bag.keywords.Contains(keyword);
        }

        /// <summary>
        /// The enabled keywords. The getter allocates a fresh array (Unity does too); the setter replaces the whole
        /// set. Null or empty clears it rather than throwing, matching Unity's tolerance here.
        /// </summary>
        public string[] shaderKeywords
        {
            get
            {
                HashSet<string> keywords = m_Bag.keywords;
                string[] result = new string[keywords.Count];
                int i = 0;
                foreach (string keyword in keywords)
                    result[i++] = keyword;
                return result;
            }
            set
            {
                m_Bag.keywords.Clear();
                if (value != null)
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        string keyword = value[i];
                        if (!string.IsNullOrEmpty(keyword))
                            m_Bag.keywords.Add(keyword);
                    }
                }

                m_Bag.version++;
                version++;
            }
        }

        /// <summary>
        /// Selects the pass the next immediate-mode draw uses. Records the pair for <c>Graphics.DrawMeshNow</c> and
        /// always returns true: under Unity a false return means the pass was culled by the current shader LOD or
        /// replacement tag, neither of which exists here.
        /// </summary>
        public bool SetPass(int pass)
        {
            NowImmediate.activePass = (this, pass);
            return true;
        }

        /// <summary>The copy <c>Object.Instantiate</c> hands back; the caller then appends "(Clone)" to its name.</summary>
        internal override Object CloneForInstantiate()
        {
            return new Material(this);
        }

        /// <summary>Lets the backend drop whatever it cached for this material (design §4.1).</summary>
        internal override void OnDestroyResources()
        {
            NowRuntime.backend.ReleaseMaterial(this);
        }

        /// <summary>
        /// Copies the shader's declared defaults into the bag. Unity does the same when a material is created for a
        /// shader, which is why a freshly created material already answers <c>GetFloat</c> with the shader's default
        /// rather than 0.
        /// </summary>
        private void SeedFromShaderDefaults(NowShaderInfo info)
        {
            if (info == null)
                return;

            foreach (KeyValuePair<int, float> entry in info.floatDefaults)
                m_Bag.floats[entry.Key] = entry.Value;

            foreach (KeyValuePair<int, Vector4> entry in info.vectorDefaults)
                m_Bag.vectors[entry.Key] = entry.Value;

            m_Bag.version++;
            version++;
        }

        /// <summary>The stored <c>_MainTex_ST</c>, or the identity transform <c>(1, 1, 0, 0)</c> when it is unset.</summary>
        private Vector4 GetTextureST()
        {
            Vector4 st;
            if (m_Bag.vectors.TryGetValue(k_MainTexST, out st))
                return st;

            return new Vector4(1f, 1f, 0f, 0f);
        }
    }
}
