// Not a Unity type: the process-wide global shader property store behind UnityEngine.Shader.SetGlobal*/GetGlobal*.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.10 (the type table), §3.5 (`Shader.cs`), §4.1 guarantee 3
// ("NowRuntime.globals holds the current global uniforms at draw time"), §6.2 (ResetAll).
//
// WHY a separate type from NowMaterialBag even though the dictionaries match: the two have different lifetimes and
// different reset rules. A material bag dies with its material; the globals survive everything except
// NowRuntime.ResetAll and Shutdown, and their version counter is what a backend watches to know it must re-push the
// global uniform block. Keeping them apart also keeps `Reset()` off the material bag, where it would be a footgun.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NowUI.Engine
{
    /// <summary>
    /// Global shader properties: Unity's process-wide uniform state, which under Unity lives in the graphics device and
    /// here lives in one instance hanging off <see cref="NowRuntime.globals"/>. Written by
    /// <c>Shader.SetGlobal*</c> and by <c>CommandBuffer</c> replay; read by the backend at bind time.
    /// </summary>
    public sealed class NowShaderGlobals
    {
        public readonly Dictionary<int, float> floats = new Dictionary<int, float>();
        public readonly Dictionary<int, int> ints = new Dictionary<int, int>();
        public readonly Dictionary<int, Vector4> vectors = new Dictionary<int, Vector4>();
        public readonly Dictionary<int, Vector4[]> vectorArrays = new Dictionary<int, Vector4[]>();
        public readonly Dictionary<int, float[]> floatArrays = new Dictionary<int, float[]>();
        public readonly Dictionary<int, Matrix4x4> matrices = new Dictionary<int, Matrix4x4>();
        public readonly Dictionary<int, Texture> textures = new Dictionary<int, Texture>();

        /// <summary>
        /// Incremented on every write, including <see cref="Reset"/>. A backend that cached the global uniform block
        /// re-reads it when this moved and skips the work when it did not (design §4.1 guarantee 1).
        /// </summary>
        public int version;

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

        /// <summary>Copies <paramref name="values"/>, reusing the stored buffer when the length already matches.</summary>
        public void SetVectorArray(int nameID, Vector4[] values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            if (values.Length == 0)
                throw new ArgumentException("Zero-sized array is not allowed.", nameof(values));

            Vector4[] stored;
            if (!vectorArrays.TryGetValue(nameID, out stored) || stored.Length != values.Length)
            {
                stored = new Vector4[values.Length];
                vectorArrays[nameID] = stored;
            }

            Array.Copy(values, stored, values.Length);
            version++;
        }

        /// <summary>Copies <paramref name="values"/>, reusing the stored buffer when the length already matches.</summary>
        public void SetFloatArray(int nameID, float[] values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            if (values.Length == 0)
                throw new ArgumentException("Zero-sized array is not allowed.", nameof(values));

            float[] stored;
            if (!floatArrays.TryGetValue(nameID, out stored) || stored.Length != values.Length)
            {
                stored = new float[values.Length];
                floatArrays[nameID] = stored;
            }

            Array.Copy(values, stored, values.Length);
            version++;
        }

        /// <summary>Zero when unset, which is what Unity reports for an unwritten global float.</summary>
        public float GetFloat(int nameID)
        {
            float value;
            return floats.TryGetValue(nameID, out value) ? value : 0f;
        }

        public int GetInt(int nameID)
        {
            int value;
            return ints.TryGetValue(nameID, out value) ? value : 0;
        }

        public Vector4 GetVector(int nameID)
        {
            Vector4 value;
            return vectors.TryGetValue(nameID, out value) ? value : Vector4.zero;
        }

        public Matrix4x4 GetMatrix(int nameID)
        {
            Matrix4x4 value;
            return matrices.TryGetValue(nameID, out value) ? value : Matrix4x4.zero;
        }

        public Texture GetTexture(int nameID)
        {
            Texture value;
            return textures.TryGetValue(nameID, out value) ? value : null;
        }

        /// <summary>
        /// Drops every global. Called by <c>NowRuntime.ResetAll</c> — note that the <c>Shader.PropertyToID</c> intern
        /// table is explicitly NOT reset with it (design §6.2 step 3): core types cache property ids in
        /// <c>static readonly</c> fields, so an id issued before the reset must still name the same property after it.
        /// </summary>
        public void Reset()
        {
            floats.Clear();
            ints.Clear();
            vectors.Clear();
            vectorArrays.Clear();
            floatArrays.Clear();
            matrices.Clear();
            textures.Clear();
            version++;
        }
    }
}
