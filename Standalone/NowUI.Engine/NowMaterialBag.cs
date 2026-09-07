// Not a Unity type: the shared property store behind UnityEngine.Material and UnityEngine.MaterialPropertyBlock.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.10 (the type table), §3.5 (`Material.cs`,
// `MaterialPropertyBlock.cs`), §1.2 (the allocation rule).
//
// WHY typed dictionaries rather than one Dictionary<int, object>: the allocation rule (design §1.2). A boxing store
// would allocate on every SetFloat in a steady-state frame, and NowUI sets material properties per draw batch
// (NowSdf.cs:4835-4907 alone does SetFloat x5, SetTexture x3, SetVector x14 and SetVectorArray x9 per upload).
//
// WHY the array setters copy into a bag-owned buffer: Unity copies at call time, and NowUI's callers reuse *static*
// scratch arrays across frames (NowMaskShader: 8 and 2 entries; NowSdf: 64 and 16). Storing the caller's reference
// would make every material in the process share the scratch buffer and see the next frame's values. The stored buffer
// is reused when the incoming length matches — the steady-state case — so the copy allocates nothing.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NowUI.Engine
{
    /// <summary>
    /// A material property bag: one typed dictionary per shader property kind, plus the keyword set. Shared verbatim by
    /// <see cref="UnityEngine.Material"/> and <see cref="UnityEngine.MaterialPropertyBlock"/>, which is why a backend
    /// can read either through the same code path.
    /// </summary>
    public sealed class NowMaterialBag
    {
        public readonly Dictionary<int, float> floats = new Dictionary<int, float>();
        public readonly Dictionary<int, int> ints = new Dictionary<int, int>();
        public readonly Dictionary<int, Vector4> vectors = new Dictionary<int, Vector4>();
        public readonly Dictionary<int, Vector4[]> vectorArrays = new Dictionary<int, Vector4[]>();
        public readonly Dictionary<int, float[]> floatArrays = new Dictionary<int, float[]>();
        public readonly Dictionary<int, Matrix4x4> matrices = new Dictionary<int, Matrix4x4>();
        public readonly Dictionary<int, Texture> textures = new Dictionary<int, Texture>();
        public readonly HashSet<string> keywords = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Bumped on every write. A backend keys its uniform cache on (instance id, version) and re-uploads only when
        /// this moved (design §4.1 guarantee 1).
        /// </summary>
        public uint version;

        // Reused by CopyFrom so reconciling the array dictionaries does not allocate a key list per call.
        private readonly List<int> m_StaleKeys = new List<int>();

        // Array buffers retired by Clear(), kept for the next Set*Array of the same property. Design §7.5 gates a
        // steady-state frame — Clear, then Set* N times, then read back — at *zero* bytes, and NowMaskShader's shared
        // property block runs exactly that cycle every frame (NowMaskShader.cs:314-445). Without this cache each
        // Clear would throw away the 8- and 2-entry buffers and the refill would allocate them again. The cache is
        // bounded by the number of distinct property ids the bag has ever seen, which is a handful per material.
        private readonly Dictionary<int, Vector4[]> m_RetiredVectorArrays = new Dictionary<int, Vector4[]>();
        private readonly Dictionary<int, float[]> m_RetiredFloatArrays = new Dictionary<int, float[]>();

        /// <summary>True when nothing has been set. Backs <c>MaterialPropertyBlock.isEmpty</c>.</summary>
        public bool isEmpty
        {
            get
            {
                return floats.Count == 0
                    && ints.Count == 0
                    && vectors.Count == 0
                    && vectorArrays.Count == 0
                    && floatArrays.Count == 0
                    && matrices.Count == 0
                    && textures.Count == 0
                    && keywords.Count == 0;
            }
        }

        /// <summary>Whether any dictionary holds <paramref name="nameID"/>. The bag half of <c>Material.HasProperty</c>.</summary>
        public bool Contains(int nameID)
        {
            return floats.ContainsKey(nameID)
                || ints.ContainsKey(nameID)
                || vectors.ContainsKey(nameID)
                || vectorArrays.ContainsKey(nameID)
                || floatArrays.ContainsKey(nameID)
                || matrices.ContainsKey(nameID)
                || textures.ContainsKey(nameID);
        }

        public void SetFloat(int nameID, float value)
        {
            floats[nameID] = value;
            version++;
        }

        public void SetInt(int nameID, int value)
        {
            ints[nameID] = value;
            version++;
        }

        public void SetVector(int nameID, Vector4 value)
        {
            vectors[nameID] = value;
            version++;
        }

        public void SetMatrix(int nameID, Matrix4x4 value)
        {
            matrices[nameID] = value;
            version++;
        }

        public void SetTexture(int nameID, Texture value)
        {
            textures[nameID] = value;
            version++;
        }

        /// <summary>
        /// Copies <paramref name="values"/> into a bag-owned <see cref="Vector4"/> array of exactly
        /// <c>values.Length</c> entries, reusing the stored array when the length already matches.
        /// </summary>
        public void SetVectorArray(int nameID, Vector4[] values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            // Unity rejects a zero-length array here rather than storing an empty property; reproducing the throw keeps
            // a caller that accidentally passes an emptied scratch list from silently disabling a shader feature.
            if (values.Length == 0)
                throw new ArgumentException("Zero-sized array is not allowed.", nameof(values));

            Vector4[] stored = RentVectorArray(nameID, values.Length);
            Array.Copy(values, stored, values.Length);
            version++;
        }

        /// <summary>List overload of <see cref="SetVectorArray(int, Vector4[])"/>, with the same copy semantics.</summary>
        public void SetVectorArray(int nameID, List<Vector4> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            int count = values.Count;
            if (count == 0)
                throw new ArgumentException("Zero-sized array is not allowed.", nameof(values));

            Vector4[] stored = RentVectorArray(nameID, count);
            // A manual loop rather than CopyTo: List<T>.CopyTo is fine, but the indexer form is what the float overload
            // needs anyway and keeping the two identical makes the allocation argument obvious.
            for (int i = 0; i < count; i++)
                stored[i] = values[i];
            version++;
        }

        public void SetFloatArray(int nameID, float[] values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            if (values.Length == 0)
                throw new ArgumentException("Zero-sized array is not allowed.", nameof(values));

            float[] stored = RentFloatArray(nameID, values.Length);
            Array.Copy(values, stored, values.Length);
            version++;
        }

        public void SetFloatArray(int nameID, List<float> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            int count = values.Count;
            if (count == 0)
                throw new ArgumentException("Zero-sized array is not allowed.", nameof(values));

            float[] stored = RentFloatArray(nameID, count);
            for (int i = 0; i < count; i++)
                stored[i] = values[i];
            version++;
        }

        /// <summary>
        /// The stored array for <paramref name="nameID"/>, resized (i.e. replaced) only when the requested length
        /// differs. Returning the existing instance is what makes a repeated <c>SetVectorArray</c> allocation-free.
        /// </summary>
        private Vector4[] RentVectorArray(int nameID, int length)
        {
            Vector4[] stored;
            if (vectorArrays.TryGetValue(nameID, out stored) && stored.Length == length)
                return stored;

            Vector4[] retired;
            if (m_RetiredVectorArrays.TryGetValue(nameID, out retired) && retired.Length == length)
            {
                m_RetiredVectorArrays.Remove(nameID);
                vectorArrays[nameID] = retired;
                return retired;
            }

            stored = new Vector4[length];
            vectorArrays[nameID] = stored;
            return stored;
        }

        private float[] RentFloatArray(int nameID, int length)
        {
            float[] stored;
            if (floatArrays.TryGetValue(nameID, out stored) && stored.Length == length)
                return stored;

            float[] retired;
            if (m_RetiredFloatArrays.TryGetValue(nameID, out retired) && retired.Length == length)
            {
                m_RetiredFloatArrays.Remove(nameID);
                floatArrays[nameID] = retired;
                return retired;
            }

            stored = new float[length];
            floatArrays[nameID] = stored;
            return stored;
        }

        /// <summary>
        /// Replaces this bag's contents with <paramref name="other"/>'s. Arrays are deep-copied — sharing them would
        /// make <c>Material.CopyPropertiesFromMaterial</c> alias two materials onto one buffer — but the destination
        /// buffer is reused when its length already matches.
        /// </summary>
        public void CopyFrom(NowMaterialBag other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            if (ReferenceEquals(other, this))
                return;

            // Deliberately NOT Clear(): that would drop the stored array buffers and force CopyFrom to allocate a fresh
            // one for every array property on every call. The scalar dictionaries are cleared (Dictionary.Clear keeps
            // its buckets, so refilling them allocates nothing) and the array dictionaries are reconciled key by key.
            floats.Clear();
            ints.Clear();
            vectors.Clear();
            matrices.Clear();
            textures.Clear();
            keywords.Clear();

            DropArrayKeysMissingFrom(other);

            foreach (KeyValuePair<int, float> entry in other.floats)
                floats[entry.Key] = entry.Value;
            foreach (KeyValuePair<int, int> entry in other.ints)
                ints[entry.Key] = entry.Value;
            foreach (KeyValuePair<int, Vector4> entry in other.vectors)
                vectors[entry.Key] = entry.Value;
            foreach (KeyValuePair<int, Matrix4x4> entry in other.matrices)
                matrices[entry.Key] = entry.Value;
            foreach (KeyValuePair<int, Texture> entry in other.textures)
                textures[entry.Key] = entry.Value;

            foreach (KeyValuePair<int, Vector4[]> entry in other.vectorArrays)
            {
                Vector4[] source = entry.Value;
                Vector4[] destination = RentVectorArray(entry.Key, source.Length);
                Array.Copy(source, destination, source.Length);
            }

            foreach (KeyValuePair<int, float[]> entry in other.floatArrays)
            {
                float[] source = entry.Value;
                float[] destination = RentFloatArray(entry.Key, source.Length);
                Array.Copy(source, destination, source.Length);
            }

            foreach (string keyword in other.keywords)
                keywords.Add(keyword);

            version++;
        }

        /// <summary>
        /// Removes the array properties <paramref name="other"/> does not have, so <see cref="CopyFrom"/> leaves this
        /// bag holding exactly the source's keys while keeping the buffers of the keys they share.
        /// </summary>
        private void DropArrayKeysMissingFrom(NowMaterialBag other)
        {
            if (vectorArrays.Count != 0)
            {
                m_StaleKeys.Clear();
                foreach (KeyValuePair<int, Vector4[]> entry in vectorArrays)
                {
                    if (!other.vectorArrays.ContainsKey(entry.Key))
                        m_StaleKeys.Add(entry.Key);
                }

                for (int i = 0; i < m_StaleKeys.Count; i++)
                {
                    int key = m_StaleKeys[i];
                    m_RetiredVectorArrays[key] = vectorArrays[key];
                    vectorArrays.Remove(key);
                }
            }

            if (floatArrays.Count == 0)
                return;

            m_StaleKeys.Clear();
            foreach (KeyValuePair<int, float[]> entry in floatArrays)
            {
                if (!other.floatArrays.ContainsKey(entry.Key))
                    m_StaleKeys.Add(entry.Key);
            }

            for (int i = 0; i < m_StaleKeys.Count; i++)
            {
                int key = m_StaleKeys[i];
                m_RetiredFloatArrays[key] = floatArrays[key];
                floatArrays.Remove(key);
            }
        }

        /// <summary>
        /// Empties every dictionary. Dictionary.Clear keeps its buckets, so a bag that is cleared and refilled every
        /// frame — which is exactly what <c>NowMaskShader</c>'s shared property block does — allocates nothing.
        /// </summary>
        public void Clear()
        {
            floats.Clear();
            ints.Clear();
            vectors.Clear();
            matrices.Clear();
            textures.Clear();
            keywords.Clear();
            RetireArrays();
            version++;
        }

        /// <summary>
        /// Moves the array buffers out of the live dictionaries and into the retired cache, where the next
        /// <c>Set*Array</c> of the same property picks them up again. The property itself is gone — <see cref="Contains"/>
        /// and <see cref="isEmpty"/> answer as if the buffer had been dropped — only the memory is kept.
        /// </summary>
        private void RetireArrays()
        {
            if (vectorArrays.Count != 0)
            {
                foreach (KeyValuePair<int, Vector4[]> entry in vectorArrays)
                    m_RetiredVectorArrays[entry.Key] = entry.Value;
                vectorArrays.Clear();
            }

            if (floatArrays.Count == 0)
                return;

            foreach (KeyValuePair<int, float[]> entry in floatArrays)
                m_RetiredFloatArrays[entry.Key] = entry.Value;
            floatArrays.Clear();
        }
    }
}
