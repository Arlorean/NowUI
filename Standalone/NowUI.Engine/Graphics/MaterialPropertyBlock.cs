// Mirrors UnityEngine.MaterialPropertyBlock for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 (`MaterialPropertyBlock.cs`), §3.10 (NowMaterialBag),
// §4.1 (the backend reads the block through the same bag as a Material).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.MaterialPropertyBlock`.
//
// Not a UnityEngine.Object — Unity's isn't either, and NowUI null-checks it with plain `== null` rather than the
// fake-null operator. It is constructed in a static field initialiser (NowMaskShader.cs:309), so its constructor must
// touch no backend and no host (design §6.4): allocating dictionaries is all it does.
//
// The snapshot members exist because NowMaskShader hands the *same* block to consecutive CommandBuffer.DrawMesh calls
// and mutates it between them. Unity copies the block into the command at record time; §3.5 requires the shim to do
// the same, and Snapshot/SnapshotInto are how CommandBuffer does it.
using System;
using System.Collections.Generic;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// A bag of per-draw property overrides, applied on top of a material's own values. Same storage shape as
    /// <see cref="Material"/>, so a backend resolves both through one code path.
    /// </summary>
    public sealed class MaterialPropertyBlock
    {
        private readonly NowMaterialBag m_Bag = new NowMaterialBag();

        public MaterialPropertyBlock()
        {
        }

        /// <summary>The property bag, for backends and for the <c>CommandBuffer</c> snapshot.</summary>
        internal NowMaterialBag bag
        {
            get { return m_Bag; }
        }

        /// <summary>True when nothing has been set. A backend may skip binding the block entirely.</summary>
        public bool isEmpty
        {
            get { return m_Bag.isEmpty; }
        }

        /// <summary>
        /// Drops every override. Keeps the dictionaries' capacity, so the clear-then-refill cycle NowMaskShader runs
        /// every frame allocates nothing (design §1.2).
        /// </summary>
        public void Clear()
        {
            m_Bag.Clear();
        }

        public bool HasProperty(int nameID)
        {
            return m_Bag.Contains(nameID);
        }

        public bool HasProperty(string name)
        {
            return m_Bag.Contains(Shader.PropertyToID(name));
        }

        public void SetFloat(int nameID, float value)
        {
            m_Bag.SetFloat(nameID, value);
        }

        public void SetFloat(string name, float value)
        {
            m_Bag.SetFloat(Shader.PropertyToID(name), value);
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

        /// <summary>Unity's legacy integer setter, which writes the float table. See <see cref="Material.SetInt(int, int)"/>.</summary>
        public void SetInt(int nameID, int value)
        {
            m_Bag.SetFloat(nameID, value);
        }

        public void SetInt(string name, int value)
        {
            SetInt(Shader.PropertyToID(name), value);
        }

        public int GetInt(int nameID)
        {
            return (int)GetFloat(nameID);
        }

        public int GetInt(string name)
        {
            return GetInt(Shader.PropertyToID(name));
        }

        public void SetInteger(int nameID, int value)
        {
            m_Bag.SetInt(nameID, value);
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

        public void SetVector(int nameID, Vector4 value)
        {
            m_Bag.SetVector(nameID, value);
        }

        public void SetVector(string name, Vector4 value)
        {
            m_Bag.SetVector(Shader.PropertyToID(name), value);
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

        /// <summary>Colors and vectors share one slot, exactly as on <see cref="Material"/>.</summary>
        public void SetColor(int nameID, Color value)
        {
            m_Bag.SetVector(nameID, new Vector4(value.r, value.g, value.b, value.a));
        }

        public void SetColor(string name, Color value)
        {
            SetColor(Shader.PropertyToID(name), value);
        }

        public Color GetColor(int nameID)
        {
            Vector4 v = GetVector(nameID);
            return new Color(v.x, v.y, v.z, v.w);
        }

        public Color GetColor(string name)
        {
            return GetColor(Shader.PropertyToID(name));
        }

        public void SetMatrix(int nameID, Matrix4x4 value)
        {
            m_Bag.SetMatrix(nameID, value);
        }

        public void SetMatrix(string name, Matrix4x4 value)
        {
            m_Bag.SetMatrix(Shader.PropertyToID(name), value);
        }

        public Matrix4x4 GetMatrix(int nameID)
        {
            Matrix4x4 value;
            return m_Bag.matrices.TryGetValue(nameID, out value) ? value : Matrix4x4.zero;
        }

        public Matrix4x4 GetMatrix(string name)
        {
            return GetMatrix(Shader.PropertyToID(name));
        }

        public void SetTexture(int nameID, Texture value)
        {
            m_Bag.SetTexture(nameID, value);
        }

        public void SetTexture(string name, Texture value)
        {
            m_Bag.SetTexture(Shader.PropertyToID(name), value);
        }

        /// <summary>Returns the stored instance, like <c>Material.mainTexture</c> does, so reference checks work.</summary>
        public Texture GetTexture(int nameID)
        {
            Texture value;
            return m_Bag.textures.TryGetValue(nameID, out value) ? value : null;
        }

        public Texture GetTexture(string name)
        {
            return GetTexture(Shader.PropertyToID(name));
        }

        /// <summary>
        /// Copies <paramref name="values"/> into a block-owned array of exactly <c>values.Length</c> entries, reusing
        /// the stored buffer when the length matches. NowMaskShader passes static 8- and 2-entry scratch arrays here.
        /// </summary>
        public void SetVectorArray(int nameID, Vector4[] values)
        {
            m_Bag.SetVectorArray(nameID, values);
        }

        public void SetVectorArray(string name, Vector4[] values)
        {
            m_Bag.SetVectorArray(Shader.PropertyToID(name), values);
        }

        public void SetVectorArray(int nameID, List<Vector4> values)
        {
            m_Bag.SetVectorArray(nameID, values);
        }

        public void SetVectorArray(string name, List<Vector4> values)
        {
            m_Bag.SetVectorArray(Shader.PropertyToID(name), values);
        }

        /// <summary>A fresh array of the stored length, or null when the property was never set.</summary>
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

        /// <summary>Clears <paramref name="values"/> first, so it ends up holding exactly the stored count.</summary>
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
        }

        public void SetFloatArray(string name, float[] values)
        {
            m_Bag.SetFloatArray(Shader.PropertyToID(name), values);
        }

        public void SetFloatArray(int nameID, List<float> values)
        {
            m_Bag.SetFloatArray(nameID, values);
        }

        public void SetFloatArray(string name, List<float> values)
        {
            m_Bag.SetFloatArray(Shader.PropertyToID(name), values);
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
        /// A detached copy of this block. <c>CommandBuffer.DrawMesh</c> records one of these rather than the caller's
        /// block, because NowMaskShader reuses a single shared block across consecutive batches and mutates it between
        /// them (design §3.5).
        /// </summary>
        internal MaterialPropertyBlock Snapshot()
        {
            MaterialPropertyBlock copy = new MaterialPropertyBlock();
            copy.m_Bag.CopyFrom(m_Bag);
            return copy;
        }

        /// <summary>
        /// Snapshots into an existing block, so a command buffer that is cleared and refilled every frame can pool its
        /// snapshots instead of allocating one per recorded draw.
        /// </summary>
        internal void SnapshotInto(MaterialPropertyBlock destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.m_Bag.CopyFrom(m_Bag);
        }
    }
}
