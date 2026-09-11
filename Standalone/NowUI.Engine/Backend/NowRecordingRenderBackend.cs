// A backend that records the ordered op list. Two jobs, both named by the design: it is what the CommandBuffer
// replay tests assert against, and it is the M2 acceptance harness - the WebGL2 backend's op log must match this
// backend's op log for the same NowUI frame, which is the cheapest possible correctness check for a new backend.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§4.6 defines it; §4.1 is the contract; §7.5 "Immediate path" is
// the suite that reads the log).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine
{
    /// <summary>
    /// Wraps a <see cref="NullRenderBackend"/> and appends one human-readable line per operation, in call order.
    /// </summary>
    /// <remarks>
    /// <para>Recording allocates - a string per op - and that is fine: this backend is a test and bring-up
    /// instrument, never the steady-state path. The allocation gate (§7.5) runs against
    /// <see cref="NullRenderBackend"/> with its ring off.</para>
    /// <para><b>Objects appear as stable aliases</b> (<c>mesh0</c>, <c>mat1</c>, <c>rt0</c>) rather than as instance
    /// ids. Instance ids are process-global and monotonic, so a raw id depends on how many objects earlier tests
    /// created - which would make two logs of the same frame differ and defeat the M2 diff the log exists for. The
    /// alias is assigned in first-seen order per recorder, so the same sequence of operations always produces the
    /// same text.</para>
    /// <para>Two members are deliberately <i>not</i> logged: <see cref="caps"/>, which is a query rather than an op,
    /// and <see cref="IsRenderTextureLost"/>, which core code calls an arbitrary number of times through
    /// <c>RenderTexture.IsCreated()</c> - noise that differs between backends and says nothing about what was
    /// drawn.</para>
    /// </remarks>
    public sealed class NowRecordingRenderBackend : INowRenderBackend
    {
        private readonly NullRenderBackend m_Inner;
        private readonly List<string> m_Ops = new List<string>();
        private readonly Dictionary<int, string> m_Aliases = new Dictionary<int, string>();
        private readonly Dictionary<string, int> m_NextAlias = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly StringBuilder m_Builder = new StringBuilder(160);

        /// <summary>Records onto a fresh <see cref="NullRenderBackend"/>.</summary>
        public NowRecordingRenderBackend()
            : this(null)
        {
        }

        /// <summary>
        /// Records onto <paramref name="inner"/>, so a test can read the counters of a backend it configured (its
        /// draw ring, for instance) while also reading the op log. Null means a fresh one.
        /// </summary>
        public NowRecordingRenderBackend(NullRenderBackend inner)
        {
            m_Inner = inner ?? new NullRenderBackend();
        }

        /// <summary>The wrapped backend: its counters, its created-target set, and its optional draw ring.</summary>
        public NullRenderBackend inner
        {
            get { return m_Inner; }
        }

        /// <summary>Every operation so far, in call order. An op that failed validation does not appear.</summary>
        public IReadOnlyList<string> ops
        {
            get { return m_Ops; }
        }

        /// <summary>Drops the op log and the alias table, so the next op is line 0 and the next mesh is <c>mesh0</c>.</summary>
        /// <remarks>Does not reset the wrapped backend; call <c>inner.Reset()</c> for that.</remarks>
        public void Clear()
        {
            m_Ops.Clear();
            m_Aliases.Clear();
            m_NextAlias.Clear();
        }

        /// <summary>The op log as one newline-separated string - the form the M2 harness diffs.</summary>
        public string ToLog()
        {
            return string.Join("\n", m_Ops);
        }

        // ---------------------------------------------------------------------------------------------------- caps

        /// <inheritdoc/>
        public NowRenderCaps caps
        {
            get { return m_Inner.caps; }
        }

        // ---------------------------------------------------------------------------------------- frame boundaries

        /// <inheritdoc/>
        public void BeginFrame(int frameCount)
        {
            m_Inner.BeginFrame(frameCount);

            Begin("BeginFrame");
            Int(frameCount);
            End();
        }

        /// <inheritdoc/>
        public void EndFrame()
        {
            m_Inner.EndFrame();

            Begin("EndFrame");
            End();
        }

        // --------------------------------------------------------------------------------------- resource lifetime

        /// <inheritdoc/>
        public void UploadTexture2D(Texture2D texture, ReadOnlySpan<byte> pixels, RectInt dirtyRect, bool generateMips)
        {
            m_Inner.UploadTexture2D(texture, pixels, dirtyRect, generateMips);

            Begin("UploadTexture2D");
            Obj(texture, "tex");
            Named("pixels", pixels.Length);
            Sep();
            m_Builder.Append("dirty=");
            AppendRectInt(dirtyRect);
            Named("mips", generateMips);
            End();
        }

        /// <inheritdoc/>
        public void UpdateSampler(Texture texture)
        {
            m_Inner.UpdateSampler(texture);

            Begin("UpdateSampler");
            Obj(texture, "tex");
            End();
        }

        /// <inheritdoc/>
        public void ReleaseTexture(Texture texture)
        {
            m_Inner.ReleaseTexture(texture);

            Begin("ReleaseTexture");
            Obj(texture, "tex");
            End();
        }

        /// <inheritdoc/>
        public bool CreateRenderTexture(RenderTexture texture, in NowRenderTextureRequest request)
        {
            bool created = m_Inner.CreateRenderTexture(texture, in request);

            Begin("CreateRenderTexture");
            Obj(texture, "rt");
            Sep();
            m_Builder.Append(request.width.ToString(CultureInfo.InvariantCulture));
            m_Builder.Append('x');
            m_Builder.Append(request.height.ToString(CultureInfo.InvariantCulture));
            Named("depth", request.depthBits);
            Named("format", request.format.ToString());
            Named("readWrite", request.readWrite.ToString());
            Named("dimension", request.dimension.ToString());
            Named("volumeDepth", request.volumeDepth);
            Named("mipCount", request.mipCount);
            Named("msaa", request.msaaSamples);
            Named("vrUsage", request.vrUsage.ToString());
            Named("bindMS", request.bindMS);
            Named("useMipMap", request.useMipMap);
            Named("autoGenerateMips", request.autoGenerateMips);
            Named("randomWrite", request.enableRandomWrite);
            EndWithResult(created);
            return created;
        }

        /// <inheritdoc/>
        public bool IsRenderTextureLost(RenderTexture texture)
        {
            // Not logged: see the type remarks.
            return m_Inner.IsRenderTextureLost(texture);
        }

        /// <inheritdoc/>
        public void ReleaseRenderTexture(RenderTexture texture)
        {
            m_Inner.ReleaseRenderTexture(texture);

            Begin("ReleaseRenderTexture");
            Obj(texture, "rt");
            End();
        }

        /// <inheritdoc/>
        public void ReleaseMesh(Mesh mesh)
        {
            m_Inner.ReleaseMesh(mesh);

            Begin("ReleaseMesh");
            Obj(mesh, "mesh");
            End();
        }

        /// <inheritdoc/>
        public void ReleaseMaterial(Material material)
        {
            m_Inner.ReleaseMaterial(material);

            Begin("ReleaseMaterial");
            Obj(material, "mat");
            End();
        }

        /// <inheritdoc/>
        public bool ResolveShader(Shader shader)
        {
            bool resolved = m_Inner.ResolveShader(shader);

            Begin("ResolveShader");
            Obj(shader, "shader");
            Sep();
            // The shader's name is the program key a backend looks up, so it belongs in the log even though the alias
            // already identifies the instance.
            m_Builder.Append('"');
            m_Builder.Append(shader.name);
            m_Builder.Append('"');
            EndWithResult(resolved);
            return resolved;
        }

        // --------------------------------------------------------------------------------------------------- state

        /// <inheritdoc/>
        public void SetRenderTarget(in NowRenderTarget target)
        {
            m_Inner.SetRenderTarget(in target);

            Begin("SetRenderTarget");
            AppendTarget(in target);
            End();
        }

        /// <inheritdoc/>
        public void SetViewport(in Rect pixelRect)
        {
            m_Inner.SetViewport(in pixelRect);

            Begin("SetViewport");
            AppendRect(in pixelRect);
            End();
        }

        /// <inheritdoc/>
        public void SetViewProjection(in Matrix4x4 view, in Matrix4x4 projection)
        {
            m_Inner.SetViewProjection(in view, in projection);

            Begin("SetViewProjection");
            m_Builder.Append("view=");
            AppendMatrix(in view);
            Sep();
            m_Builder.Append("projection=");
            AppendMatrix(in projection);
            End();
        }

        /// <inheritdoc/>
        public void ClearRenderTarget(bool clearDepth, bool clearColor, in Color color, float depth)
        {
            m_Inner.ClearRenderTarget(clearDepth, clearColor, in color, depth);

            Begin("ClearRenderTarget");
            m_Builder.Append("depth=");
            AppendBool(clearDepth);
            Named("color", clearColor);
            Sep();
            AppendColor(in color);
            Named("z", depth);
            End();
        }

        // --------------------------------------------------------------------------------------------------- draws

        /// <inheritdoc/>
        public void DrawMesh(Mesh mesh, int subMesh, in Matrix4x4 model, Material material, int pass, MaterialPropertyBlock properties)
        {
            m_Inner.DrawMesh(mesh, subMesh, in model, material, pass, properties);

            Begin("DrawMesh");
            Obj(mesh, "mesh");
            Named("subMesh", subMesh);
            Sep();
            m_Builder.Append("material=");
            AppendAlias(material, "mat");
            Named("pass", pass);
            Named("properties", properties != null ? "block" : "none");
            Sep();
            m_Builder.Append("model=");
            AppendMatrix(in model);
            End();
        }

        /// <inheritdoc/>
        public void DrawProcedural(in Matrix4x4 model, Material material, int pass, MeshTopology topology, int vertexCount,
                                   int instanceCount, MaterialPropertyBlock properties)
        {
            m_Inner.DrawProcedural(in model, material, pass, topology, vertexCount, instanceCount, properties);

            Begin("DrawProcedural");
            m_Builder.Append("material=");
            AppendAlias(material, "mat");
            Named("pass", pass);
            Named("topology", topology.ToString());
            Named("vertices", vertexCount);
            Named("instances", instanceCount);
            Named("properties", properties != null ? "block" : "none");
            Sep();
            m_Builder.Append("model=");
            AppendMatrix(in model);
            End();
        }

        /// <inheritdoc/>
        public void Blit(Texture source, in NowRenderTarget destination, Material material, int pass,
                         in Vector2 scale, in Vector2 offset, int sourceDepthSlice, int destinationDepthSlice)
        {
            m_Inner.Blit(source, in destination, material, pass, in scale, in offset, sourceDepthSlice, destinationDepthSlice);

            Begin("Blit");
            m_Builder.Append("source=");
            if (ReferenceEquals(source, null))
                m_Builder.Append("none");
            else
                AppendAlias(source, "tex");
            Sep();
            m_Builder.Append("destination=");
            AppendTarget(in destination);
            Sep();
            m_Builder.Append("material=");
            if (ReferenceEquals(material, null))
                m_Builder.Append("none");
            else
                AppendAlias(material, "mat");
            Named("pass", pass);
            Sep();
            m_Builder.Append("scale=");
            AppendVector2(in scale);
            Sep();
            m_Builder.Append("offset=");
            AppendVector2(in offset);
            Named("sourceSlice", sourceDepthSlice);
            Named("destinationSlice", destinationDepthSlice);
            End();
        }

        /// <inheritdoc/>
        public void CopyTexture(Texture source, Texture destination)
        {
            m_Inner.CopyTexture(source, destination);

            Begin("CopyTexture");
            Obj(source, "tex");
            Sep();
            AppendAlias(destination, "tex");
            End();
        }

        // ------------------------------------------------------------------------------------------ log formatting
        //
        // Everything below writes into one reused StringBuilder and formats with the invariant culture, so the log
        // is byte-identical on a machine whose decimal separator is a comma. Floats use the shortest round-trippable
        // form, which is what makes a diff of two backends' logs meaningful rather than merely approximate.

        private void Begin(string op)
        {
            m_Builder.Clear();
            m_Builder.Append(op);
            m_Builder.Append('(');
        }

        private void End()
        {
            m_Builder.Append(')');
            m_Ops.Add(m_Builder.ToString());
        }

        private void EndWithResult(bool result)
        {
            m_Builder.Append(") -> ");
            AppendBool(result);
            m_Ops.Add(m_Builder.ToString());
        }

        private void Sep()
        {
            if (m_Builder.Length > 0 && m_Builder[m_Builder.Length - 1] != '(')
                m_Builder.Append(", ");
        }

        private void Obj(UnityEngine.Object value, string prefix)
        {
            AppendAlias(value, prefix);
        }

        private void Int(int value)
        {
            m_Builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private void Named(string name, int value)
        {
            Sep();
            m_Builder.Append(name);
            m_Builder.Append('=');
            m_Builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private void Named(string name, float value)
        {
            Sep();
            m_Builder.Append(name);
            m_Builder.Append('=');
            AppendFloat(value);
        }

        private void Named(string name, bool value)
        {
            Sep();
            m_Builder.Append(name);
            m_Builder.Append('=');
            AppendBool(value);
        }

        private void Named(string name, string value)
        {
            Sep();
            m_Builder.Append(name);
            m_Builder.Append('=');
            m_Builder.Append(value);
        }

        private void AppendBool(bool value)
        {
            m_Builder.Append(value ? "true" : "false");
        }

        private void AppendFloat(float value)
        {
            m_Builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private void AppendVector2(in Vector2 value)
        {
            m_Builder.Append('(');
            AppendFloat(value.x);
            m_Builder.Append(", ");
            AppendFloat(value.y);
            m_Builder.Append(')');
        }

        private void AppendRect(in Rect value)
        {
            m_Builder.Append('(');
            AppendFloat(value.x);
            m_Builder.Append(", ");
            AppendFloat(value.y);
            m_Builder.Append(", ");
            AppendFloat(value.width);
            m_Builder.Append(", ");
            AppendFloat(value.height);
            m_Builder.Append(')');
        }

        private void AppendRectInt(RectInt value)
        {
            m_Builder.Append('(');
            m_Builder.Append(value.x.ToString(CultureInfo.InvariantCulture));
            m_Builder.Append(", ");
            m_Builder.Append(value.y.ToString(CultureInfo.InvariantCulture));
            m_Builder.Append(", ");
            m_Builder.Append(value.width.ToString(CultureInfo.InvariantCulture));
            m_Builder.Append(", ");
            m_Builder.Append(value.height.ToString(CultureInfo.InvariantCulture));
            m_Builder.Append(')');
        }

        private void AppendColor(in Color value)
        {
            m_Builder.Append("rgba(");
            AppendFloat(value.r);
            m_Builder.Append(", ");
            AppendFloat(value.g);
            m_Builder.Append(", ");
            AppendFloat(value.b);
            m_Builder.Append(", ");
            AppendFloat(value.a);
            m_Builder.Append(')');
        }

        private void AppendMatrix(in Matrix4x4 value)
        {
            m_Builder.Append('[');
            for (int i = 0; i < 16; i++)
            {
                if (i != 0)
                    m_Builder.Append(' ');

                // Row-major reading order, which is how the matrix is written in the design and in NowUI's own
                // projection code; Matrix4x4's linear indexer is column-major, so the row/column indexer is used.
                AppendFloat(value[i >> 2, i & 3]);
            }

            m_Builder.Append(']');
        }

        private void AppendTarget(in NowRenderTarget target)
        {
            if (target.isBackBuffer)
                m_Builder.Append("backbuffer");
            else
                AppendAlias(target.texture, "rt");

            Named("mip", target.mipLevel);
            Named("face", target.face.ToString());
            Named("slice", target.depthSlice);
            Sep();
            m_Builder.Append(target.width.ToString(CultureInfo.InvariantCulture));
            m_Builder.Append('x');
            m_Builder.Append(target.height.ToString(CultureInfo.InvariantCulture));
        }

        private void AppendAlias(UnityEngine.Object value, string prefix)
        {
            if (ReferenceEquals(value, null))
            {
                m_Builder.Append("null");
                return;
            }

            int id = value.GetInstanceID();
            string alias;
            if (!m_Aliases.TryGetValue(id, out alias))
            {
                int next;
                m_NextAlias.TryGetValue(prefix, out next);
                m_NextAlias[prefix] = next + 1;
                alias = prefix + next.ToString(CultureInfo.InvariantCulture);
                m_Aliases[id] = alias;
            }

            m_Builder.Append(alias);
        }
    }
}
