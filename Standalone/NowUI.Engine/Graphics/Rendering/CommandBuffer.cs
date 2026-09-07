// Mirrors UnityEngine.Rendering.CommandBuffer for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §4.3 (the member list and the replay rules), §3.5
// ("Engine/Graphics/Rendering/" - CommandBuffer is an op recorder), §4.2 (NowImmediate.Resolve and the shared bind /
// blit helpers), §4.1 (the backend contract and its invariants), §1.2 (the allocation rule).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.Rendering.CommandBuffer` - ten core
// files record into one, and `NowRenderer.Draw(CommandBuffer, ...)` / `NowRenderer.PopulateCommandBuffer` are public
// API, so the recorded surface is part of NowUI's contract with its hosts.
//
// The whole point of this type is that there is ONE render path. A recorded op replays into exactly the calls the
// immediate path makes - `NowImmediate.Bind`, `NowImmediate.Blit`, `backend.DrawMesh` - so a backend author has one
// ordering to implement and the recording backend's log of a recorded frame is comparable to its log of an immediate
// one (design §4, opening rule; §4.6).
//
// Behaviours that look odd and are deliberate:
//   * `DrawMesh` SNAPSHOTS the property block but stores the mesh and material by reference. That is Unity's split,
//     and NowUI depends on both halves: NowMaskShader hands the same block to consecutive batches and mutates it
//     between them, while materials are expected to be read at execution time.
//   * `SetGlobal*` writes at REPLAY time, not at record time. Unity's globals are device state, and a single-threaded
//     executor sees the ops in order, so a global set inside a buffer must not leak forward to draws recorded before
//     it and must be visible to draws recorded after it.
//   * Temporaries still allocated when the last op has run are released. Unity frees them at the end of execution,
//     and the shim's pool would otherwise hold an entry nobody can name any more.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NowUI.Engine;

namespace UnityEngine.Rendering
{
    /// <summary>
    /// A list of rendering commands recorded now and replayed later. Not a <see cref="Object"/> - Unity's is a plain
    /// class too, which is why the core null-checks it with <c>== null</c> rather than through the fake-null operator.
    /// </summary>
    public class CommandBuffer : IDisposable
    {
        // Ops are a struct list that is Clear()ed and refilled: NowUI re-records its buffers every frame, so the
        // backing array has to survive the frame boundary (design §1.2).
        private readonly List<NowCommandOp> m_Ops = new List<NowCommandOp>(32);

        // Property-block snapshots, reused across Clear() cycles. m_BlockCount is how many of them the current
        // recording has handed out; Clear() rewinds it without dropping the blocks themselves, so a buffer that
        // records the same eight masked batches every frame allocates eight blocks once and never again.
        private readonly List<MaterialPropertyBlock> m_BlockPool = new List<MaterialPropertyBlock>(4);
        private int m_BlockCount;

        private string m_Name = DefaultName;
        private bool m_Released;

        /// <summary>The name a buffer carries until something names it - the one Unity's profiler shows.</summary>
        private const string DefaultName = "Unnamed command buffer";

        /// <summary>Creates an empty buffer.</summary>
        public CommandBuffer()
        {
        }

        /// <summary>
        /// The buffer's name, as it appears in a profiler. Set by nine of the ten core files that record one, so it is
        /// how a NowUI frame is read in a capture.
        /// </summary>
        public string name
        {
            get { return m_Name; }
            set { m_Name = value; }
        }

        /// <summary>
        /// Roughly how much memory the recorded ops occupy. Unity reports the size of its native command list; the
        /// shim reports the size of the managed one, which is the same quantity measured on the shim's own storage.
        /// </summary>
        public int sizeInBytes
        {
            get { return m_Ops.Count * Unsafe.SizeOf<NowCommandOp>(); }
        }

        /// <summary>Ops recorded so far. Internal because Unity has no such member; the semantics suite reads it.</summary>
        internal int opCount
        {
            get { return m_Ops.Count; }
        }

        /// <summary>Whether <see cref="Release"/> has been called. Internal, for the semantics suite.</summary>
        internal bool released
        {
            get { return m_Released; }
        }

        /// <summary>
        /// Drops the recorded commands and keeps the capacity, so re-recording the same frame shape allocates
        /// nothing (design §4.3).
        /// </summary>
        public void Clear()
        {
            ThrowIfReleased();

            m_Ops.Clear();

            // The pooled snapshots stay; only the hand-out cursor rewinds. Their contents are overwritten on the next
            // recording, so nothing stale can be replayed.
            m_BlockCount = 0;
        }

        /// <summary>
        /// Frees the buffer. Any later use throws <see cref="ObjectDisposedException"/>, which is the shape
        /// <c>NowRenderer.ThrowIfDisposed</c> already has - a released buffer being recorded into is a lifetime bug,
        /// and a silent no-op would hide it until the frame came out empty.
        /// </summary>
        public void Release()
        {
            if (m_Released)
                return;

            m_Ops.Clear();
            m_BlockPool.Clear();
            m_BlockCount = 0;
            m_Released = true;
        }

        /// <summary>Same as <see cref="Release"/>; Unity's <c>CommandBuffer</c> is <see cref="IDisposable"/> for <c>using</c> blocks.</summary>
        public void Dispose()
        {
            Release();
        }

        // ------------------------------------------------------------------------------------------ render targets

        /// <summary>Binds a target. The viewport follows it at replay time (backend invariant 5).</summary>
        public void SetRenderTarget(RenderTargetIdentifier rt)
        {
            NowCommandOp op = Op(NowCommandOpType.SetRenderTarget);
            op.target = rt;
            Record(in op);
        }

        /// <summary>Binds one mip / face / slice of a target.</summary>
        public void SetRenderTarget(RenderTargetIdentifier rt, int mipLevel, CubemapFace cubemapFace, int depthSlice)
        {
            NowCommandOp op = Op(NowCommandOpType.SetRenderTarget);
            op.target = new RenderTargetIdentifier(rt, mipLevel, cubemapFace, depthSlice);
            Record(in op);
        }

        /// <summary>
        /// Binds a colour target with a separate depth target.
        /// </summary>
        /// <remarks>
        /// The shim's <c>NowRenderTarget</c> carries one surface, because every NowUI pass renders colour into a
        /// target that owns its own depth. The depth identifier is recorded so the op is faithful and a backend that
        /// later grows separate depth attachments has it, but replay binds the colour target - which is what the
        /// single-surface contract can honour.
        /// </remarks>
        public void SetRenderTarget(RenderTargetIdentifier color, RenderTargetIdentifier depth)
        {
            NowCommandOp op = Op(NowCommandOpType.SetRenderTarget);
            op.target = color;
            op.source = depth;
            op.hasDepthTarget = true;
            Record(in op);
        }

        /// <summary>Sets the viewport inside the bound target.</summary>
        public void SetViewport(Rect pixelRect)
        {
            NowCommandOp op = Op(NowCommandOpType.SetViewport);
            op.rect = pixelRect;
            Record(in op);
        }

        /// <summary>Sets the camera matrices for the draws that follow.</summary>
        public void SetViewProjectionMatrices(Matrix4x4 view, Matrix4x4 proj)
        {
            NowCommandOp op = Op(NowCommandOpType.SetViewProjectionMatrices);
            op.matrixA = view;
            op.matrixB = proj;
            Record(in op);
        }

        /// <summary>Clears the bound target, with depth 1 (the far plane), which is Unity's default.</summary>
        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor)
        {
            ClearRenderTarget(clearDepth, clearColor, backgroundColor, 1f);
        }

        /// <summary>Clears the bound target to an explicit depth.</summary>
        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor, float depth)
        {
            NowCommandOp op = Op(NowCommandOpType.ClearRenderTarget);
            op.clearDepth = clearDepth;
            op.clearColor = clearColor;
            op.color = backgroundColor;
            op.floatA = depth;
            Record(in op);
        }

        // ---------------------------------------------------------------------------------------------- temporaries

        /// <summary>Allocates a temporary target that later ops name by <paramref name="nameID"/>.</summary>
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, RenderTextureFormat format)
        {
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, RenderTextureReadWrite.Default, 1);
        }

        /// <inheritdoc cref="GetTemporaryRT(int,int,int,int,FilterMode,RenderTextureFormat)"/>
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, RenderTextureFormat format,
                                   RenderTextureReadWrite readWrite)
        {
            GetTemporaryRT(nameID, width, height, depthBuffer, filter, format, readWrite, 1);
        }

        /// <inheritdoc cref="GetTemporaryRT(int,int,int,int,FilterMode,RenderTextureFormat)"/>
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer, FilterMode filter, RenderTextureFormat format,
                                   RenderTextureReadWrite readWrite, int antiAliasing)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height, format, depthBuffer)
            {
                // The descriptor stores RESOLVED colour handling, exactly as RenderTexture.GetTemporary does, so two
                // acquires that name the read/write mode differently but resolve the same share a pool slot.
                sRGB = RenderTexture.ResolveSrgb(readWrite),
                msaaSamples = antiAliasing < 1 ? 1 : antiAliasing,
            };

            GetTemporaryRT(nameID, descriptor, filter);
        }

        /// <summary>Allocates a temporary target described by <paramref name="desc"/>.</summary>
        public void GetTemporaryRT(int nameID, RenderTextureDescriptor desc, FilterMode filter)
        {
            NowCommandOp op = Op(NowCommandOpType.GetTemporaryRT);
            op.nameID = nameID;
            op.descriptor = desc;
            op.intA = (int)filter;
            Record(in op);
        }

        /// <summary>Returns the temporary allocated under <paramref name="nameID"/>.</summary>
        public void ReleaseTemporaryRT(int nameID)
        {
            NowCommandOp op = Op(NowCommandOpType.ReleaseTemporaryRT);
            op.nameID = nameID;
            Record(in op);
        }

        // --------------------------------------------------------------------------------------------------- draws

        /// <summary>Draws sub-mesh 0 with pass -1 ("every pass"), which is Unity's default.</summary>
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material)
        {
            DrawMesh(mesh, matrix, material, 0, -1, null);
        }

        /// <inheritdoc cref="DrawMesh(Mesh,Matrix4x4,Material)"/>
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex)
        {
            DrawMesh(mesh, matrix, material, submeshIndex, -1, null);
        }

        /// <inheritdoc cref="DrawMesh(Mesh,Matrix4x4,Material)"/>
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass)
        {
            DrawMesh(mesh, matrix, material, submeshIndex, shaderPass, null);
        }

        /// <summary>
        /// Draws one sub-mesh. <paramref name="properties"/> is <b>snapshotted</b>: <c>NowMaskShader</c> hands one
        /// shared block to consecutive batches and mutates it between them, so recording the caller's reference would
        /// make every batch in the buffer draw with the last batch's values (design §3.5, §4.3).
        /// </summary>
        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex, int shaderPass,
                             MaterialPropertyBlock properties)
        {
            NowCommandOp op = Op(NowCommandOpType.DrawMesh);
            op.mesh = mesh;
            op.matrixA = matrix;
            op.material = material;
            op.intA = submeshIndex;
            op.intB = shaderPass;
            op.properties = SnapshotBlock(properties);
            Record(in op);
        }

        /// <summary>Draws <paramref name="vertexCount"/> generated vertices, one instance.</summary>
        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount)
        {
            DrawProcedural(matrix, material, shaderPass, topology, vertexCount, 1, null);
        }

        /// <inheritdoc cref="DrawProcedural(Matrix4x4,Material,int,MeshTopology,int)"/>
        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount,
                                   int instanceCount)
        {
            DrawProcedural(matrix, material, shaderPass, topology, vertexCount, instanceCount, null);
        }

        /// <summary>Draws generated vertices. <paramref name="properties"/> is snapshotted, as <see cref="DrawMesh(Mesh,Matrix4x4,Material,int,int,MaterialPropertyBlock)"/>'s is.</summary>
        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount,
                                   int instanceCount, MaterialPropertyBlock properties)
        {
            NowCommandOp op = Op(NowCommandOpType.DrawProcedural);
            op.matrixA = matrix;
            op.material = material;
            op.intB = shaderPass;
            op.topology = topology;
            op.intA = vertexCount;
            op.intC = instanceCount;
            op.properties = SnapshotBlock(properties);
            Record(in op);
        }

        // --------------------------------------------------------------------------------------------------- blits

        /// <summary>Copies one target into another with no shader involved.</summary>
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest)
        {
            RecordBlit(source, false, null, dest, null, 0, Vector2.one, Vector2.zero, 0, 0);
        }

        /// <summary>Copies through <paramref name="mat"/>'s pass -1, i.e. every pass, which is Unity's default.</summary>
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Material mat)
        {
            RecordBlit(source, false, null, dest, mat, -1, Vector2.one, Vector2.zero, 0, 0);
        }

        /// <summary>Copies through one pass of <paramref name="mat"/>.</summary>
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Material mat, int pass)
        {
            RecordBlit(source, false, null, dest, mat, pass, Vector2.one, Vector2.zero, 0, 0);
        }

        /// <summary>Copies a scaled and offset sub-rect of the source.</summary>
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Vector2 scale, Vector2 offset)
        {
            RecordBlit(source, false, null, dest, null, 0, scale, offset, 0, 0);
        }

        /// <summary>
        /// Copies a scaled and offset sub-rect between explicit array slices.
        /// </summary>
        /// <remarks>
        /// Not in the design's §4.3 member list, but the inventory records <c>NowGlassRenderer</c> calling exactly this
        /// overload, and a shim that omitted it would fail the core compile. Unity has it.
        /// </remarks>
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Vector2 scale, Vector2 offset,
                         int sourceDepthSlice, int destDepthSlice)
        {
            RecordBlit(source, false, null, dest, null, 0, scale, offset, sourceDepthSlice, destDepthSlice);
        }

        /// <summary>Copies a texture handle into a target.</summary>
        public void Blit(Texture source, RenderTargetIdentifier dest)
        {
            RecordBlit(default, true, source, dest, null, 0, Vector2.one, Vector2.zero, 0, 0);
        }

        /// <summary>Copies a texture handle through <paramref name="mat"/>'s pass -1.</summary>
        public void Blit(Texture source, RenderTargetIdentifier dest, Material mat)
        {
            RecordBlit(default, true, source, dest, mat, -1, Vector2.one, Vector2.zero, 0, 0);
        }

        /// <summary>Copies a texture handle through one pass of <paramref name="mat"/>.</summary>
        public void Blit(Texture source, RenderTargetIdentifier dest, Material mat, int pass)
        {
            RecordBlit(default, true, source, dest, mat, pass, Vector2.one, Vector2.zero, 0, 0);
        }

        // ------------------------------------------------------------------------------------------------ globals

        /// <summary>Writes a global float at replay time.</summary>
        public void SetGlobalFloat(int nameID, float value)
        {
            NowCommandOp op = Op(NowCommandOpType.SetGlobalFloat);
            op.nameID = nameID;
            op.floatA = value;
            Record(in op);
        }

        /// <inheritdoc cref="SetGlobalFloat(int,float)"/>
        public void SetGlobalFloat(string name, float value)
        {
            SetGlobalFloat(Shader.PropertyToID(name), value);
        }

        /// <summary>Writes a global int at replay time.</summary>
        public void SetGlobalInt(int nameID, int value)
        {
            NowCommandOp op = Op(NowCommandOpType.SetGlobalInt);
            op.nameID = nameID;
            op.intA = value;
            Record(in op);
        }

        /// <inheritdoc cref="SetGlobalInt(int,int)"/>
        public void SetGlobalInt(string name, int value)
        {
            SetGlobalInt(Shader.PropertyToID(name), value);
        }

        /// <summary>Writes a global vector at replay time.</summary>
        public void SetGlobalVector(int nameID, Vector4 value)
        {
            NowCommandOp op = Op(NowCommandOpType.SetGlobalVector);
            op.nameID = nameID;
            op.vector = value;
            Record(in op);
        }

        /// <inheritdoc cref="SetGlobalVector(int,Vector4)"/>
        public void SetGlobalVector(string name, Vector4 value)
        {
            SetGlobalVector(Shader.PropertyToID(name), value);
        }

        /// <summary>Writes a global colour, which is a vector: Unity has no separate colour uniform slot.</summary>
        public void SetGlobalColor(int nameID, Color value)
        {
            SetGlobalVector(nameID, value);
        }

        /// <inheritdoc cref="SetGlobalColor(int,Color)"/>
        public void SetGlobalColor(string name, Color value)
        {
            SetGlobalVector(Shader.PropertyToID(name), value);
        }

        /// <summary>Writes a global matrix at replay time.</summary>
        public void SetGlobalMatrix(int nameID, Matrix4x4 value)
        {
            NowCommandOp op = Op(NowCommandOpType.SetGlobalMatrix);
            op.nameID = nameID;
            op.matrixA = value;
            Record(in op);
        }

        /// <inheritdoc cref="SetGlobalMatrix(int,Matrix4x4)"/>
        public void SetGlobalMatrix(string name, Matrix4x4 value)
        {
            SetGlobalMatrix(Shader.PropertyToID(name), value);
        }

        /// <summary>
        /// Writes a global texture named by an identifier - a temporary this buffer allocated, most often. The
        /// identifier is resolved at <i>replay</i> time, because that is when the temporary exists.
        /// </summary>
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value)
        {
            NowCommandOp op = Op(NowCommandOpType.SetGlobalTexture);
            op.nameID = nameID;
            op.target = value;
            op.resolveTexture = true;
            Record(in op);
        }

        /// <inheritdoc cref="SetGlobalTexture(int,RenderTargetIdentifier)"/>
        public void SetGlobalTexture(string name, RenderTargetIdentifier value)
        {
            SetGlobalTexture(Shader.PropertyToID(name), value);
        }

        /// <summary>Writes a global texture by handle. The handle is stored by reference, as Unity stores it.</summary>
        public void SetGlobalTexture(int nameID, Texture value)
        {
            NowCommandOp op = Op(NowCommandOpType.SetGlobalTexture);
            op.nameID = nameID;
            op.texture = value;
            Record(in op);
        }

        /// <inheritdoc cref="SetGlobalTexture(int,Texture)"/>
        public void SetGlobalTexture(string name, Texture value)
        {
            SetGlobalTexture(Shader.PropertyToID(name), value);
        }

        // ------------------------------------------------------------------------------------------------ samples

        /// <summary>Opens a profiler scope at replay time.</summary>
        public void BeginSample(string name)
        {
            NowCommandOp op = Op(NowCommandOpType.BeginSample);
            op.name = name;
            Record(in op);
        }

        /// <summary>Closes a profiler scope at replay time.</summary>
        public void EndSample(string name)
        {
            NowCommandOp op = Op(NowCommandOpType.EndSample);
            op.name = name;
            Record(in op);
        }

        // -------------------------------------------------------------------------------------------------- replay

        /// <summary>
        /// Replays every recorded op, in order, into <paramref name="backend"/> - through the same helpers the
        /// immediate path uses, so a backend sees one ordering and not two (design §4.3).
        /// </summary>
        /// <remarks>
        /// <para><b>Invariant 6 without double work.</b> The first draw in an execution that has not yet issued a
        /// view-projection gets one, taken from the current GL matrices. A buffer that records
        /// <c>SetViewProjectionMatrices</c> before its draws - which every NowUI buffer does - pays nothing for this.</para>
        /// <para><b>Temporaries are freed even when an op throws.</b> The release walk is in a <c>finally</c>: a
        /// malformed buffer must not leak pool entries for the life of the process.</para>
        /// </remarks>
        internal void Execute(INowRenderBackend backend)
        {
            ThrowIfReleased();

            if (backend == null)
                throw new ArgumentNullException(nameof(backend));

            NowCommandContext context = NowImmediate.RentContext();
            bool viewProjectionSet = false;

            try
            {
                for (int i = 0; i < m_Ops.Count; i++)
                {
                    NowCommandOp op = m_Ops[i];

                    switch (op.type)
                    {
                        case NowCommandOpType.SetRenderTarget:
                        {
                            NowRenderTarget target = NowImmediate.Resolve(in op.target, context);
                            NowImmediate.Bind(in target, context, backend);
                            break;
                        }

                        case NowCommandOpType.SetViewport:
                            context.viewport = op.rect;
                            backend.SetViewport(in op.rect);
                            break;

                        case NowCommandOpType.SetViewProjectionMatrices:
                            backend.SetViewProjection(in op.matrixA, in op.matrixB);
                            viewProjectionSet = true;
                            break;

                        case NowCommandOpType.ClearRenderTarget:
                            backend.ClearRenderTarget(op.clearDepth, op.clearColor, in op.color, op.floatA);
                            break;

                        case NowCommandOpType.GetTemporaryRT:
                            AcquireTemporary(context, in op);
                            break;

                        case NowCommandOpType.ReleaseTemporaryRT:
                            ReleaseTemporary(context, op.nameID);
                            break;

                        case NowCommandOpType.DrawMesh:
                            EnsureViewProjection(backend, ref viewProjectionSet);
                            backend.DrawMesh(op.mesh, op.intA, in op.matrixA, op.material, op.intB, op.properties);
                            break;

                        case NowCommandOpType.DrawProcedural:
                            EnsureViewProjection(backend, ref viewProjectionSet);
                            backend.DrawProcedural(in op.matrixA, op.material, op.intB, op.topology, op.intA, op.intC,
                                                   op.properties);
                            break;

                        case NowCommandOpType.Blit:
                        {
                            Texture source = op.hasSourceTexture
                                ? op.texture
                                : NowImmediate.ResolveTexture(in op.source, context);
                            NowRenderTarget destination = NowImmediate.Resolve(in op.target, context);

                            NowImmediate.Blit(source, in destination, op.material, op.intB, in op.scale, in op.offset,
                                              op.intC, op.intD, context, backend);
                            break;
                        }

                        case NowCommandOpType.SetGlobalFloat:
                            NowRuntime.globals.SetFloat(op.nameID, op.floatA);
                            break;

                        case NowCommandOpType.SetGlobalInt:
                            NowRuntime.globals.SetInt(op.nameID, op.intA);
                            break;

                        case NowCommandOpType.SetGlobalVector:
                            NowRuntime.globals.SetVector(op.nameID, op.vector);
                            break;

                        case NowCommandOpType.SetGlobalMatrix:
                            NowRuntime.globals.SetMatrix(op.nameID, op.matrixA);
                            break;

                        case NowCommandOpType.SetGlobalTexture:
                            NowRuntime.globals.SetTexture(
                                op.nameID,
                                op.resolveTexture ? NowImmediate.ResolveTexture(in op.target, context) : op.texture);
                            break;

                        case NowCommandOpType.BeginSample:
                        {
                            INowProfilerSink sink = NowRuntime.profilerSink;

                            if (sink != null)
                                sink.Begin(op.name);

                            break;
                        }

                        case NowCommandOpType.EndSample:
                        {
                            INowProfilerSink sink = NowRuntime.profilerSink;

                            if (sink != null)
                                sink.End(op.name);

                            break;
                        }

                        default:
                            throw new InvalidOperationException(
                                "CommandBuffer.Execute: unhandled op " + op.type + ".");
                    }
                }
            }
            finally
            {
                ReleaseRemainingTemporaries(context);
                NowImmediate.ReturnContext(context);
            }
        }

        // ------------------------------------------------------------------------------------------------ internals

        private static void EnsureViewProjection(INowRenderBackend backend, ref bool viewProjectionSet)
        {
            if (viewProjectionSet)
                return;

            // Backend invariant 6: every draw is preceded by at least one SetViewProjection. A buffer that never
            // records SetViewProjectionMatrices inherits the immediate path's matrices, which is what Unity does too -
            // its command buffers execute against whatever camera state is current.
            backend.SetViewProjection(in NowImmediate.modelView, in NowImmediate.projection);
            viewProjectionSet = true;
        }

        private static void AcquireTemporary(NowCommandContext context, in NowCommandOp op)
        {
            RenderTexture existing;

            // Re-getting the same id replaces the previous target. Unity leaks the old one; the shim returns it,
            // because the shim's pool tracks outstanding entries by instance id and an entry nobody can name any more
            // would sit in that table for the life of the process.
            if (context.temporaries.TryGetValue(op.nameID, out existing))
                RenderTexture.ReleaseTemporary(existing);

            RenderTexture temporary = RenderTexture.GetTemporary(op.descriptor);

            // Filter mode is applied after the acquire, exactly as NowSdfImageField does it by hand - which is why
            // filterMode is deliberately not part of the pool key (see NowTemporaryRenderTexturePool's header).
            temporary.filterMode = (FilterMode)op.intA;

            context.temporaries[op.nameID] = temporary;
        }

        private static void ReleaseTemporary(NowCommandContext context, int nameID)
        {
            RenderTexture temporary;

            // Releasing an id that was never allocated is ignored rather than thrown: Unity ignores it too, and a
            // paired Get/Release inside a conditional branch is a normal recording shape.
            if (!context.temporaries.TryGetValue(nameID, out temporary))
                return;

            context.temporaries.Remove(nameID);
            RenderTexture.ReleaseTemporary(temporary);
        }

        private static void ReleaseRemainingTemporaries(NowCommandContext context)
        {
            if (context.temporaries.Count == 0)
                return;

            foreach (KeyValuePair<int, RenderTexture> entry in context.temporaries)
                RenderTexture.ReleaseTemporary(entry.Value);

            context.temporaries.Clear();
        }

        private void RecordBlit(RenderTargetIdentifier source, bool hasSourceTexture, Texture sourceTexture,
                                RenderTargetIdentifier dest, Material material, int pass,
                                Vector2 scale, Vector2 offset, int sourceDepthSlice, int destDepthSlice)
        {
            NowCommandOp op = Op(NowCommandOpType.Blit);
            op.source = source;
            op.hasSourceTexture = hasSourceTexture;
            op.texture = sourceTexture;
            op.target = dest;
            op.material = material;
            op.intB = pass;
            op.scale = scale;
            op.offset = offset;
            op.intC = sourceDepthSlice;
            op.intD = destDepthSlice;
            Record(in op);
        }

        private MaterialPropertyBlock SnapshotBlock(MaterialPropertyBlock properties)
        {
            if (properties == null)
                return null;

            MaterialPropertyBlock destination;

            if (m_BlockCount < m_BlockPool.Count)
            {
                destination = m_BlockPool[m_BlockCount];
            }
            else
            {
                destination = new MaterialPropertyBlock();
                m_BlockPool.Add(destination);
            }

            m_BlockCount++;

            // SnapshotInto rather than Snapshot: the pooled block is reused across frames, and CopyFrom overwrites it
            // wholesale, so a recording that shrinks cannot leave a stale property behind.
            properties.SnapshotInto(destination);
            return destination;
        }

        private static NowCommandOp Op(NowCommandOpType type)
        {
            // A fresh local, so every field an op does not use is its default rather than the previous op's value.
            // This is a stack struct: it costs no allocation.
            NowCommandOp op = default;
            op.type = type;
            return op;
        }

        private void Record(in NowCommandOp op)
        {
            ThrowIfReleased();
            m_Ops.Add(op);
        }

        private void ThrowIfReleased()
        {
            if (m_Released)
                throw new ObjectDisposedException(nameof(CommandBuffer),
                    "This CommandBuffer has been released and cannot be used again.");
        }
    }
}
