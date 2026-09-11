// The recorded form of one UnityEngine.Rendering.CommandBuffer call. Not a UnityEngine type: Unity records into a
// native command list, and this is the managed stand-in for one entry of it.
// Design: Docs/Standalone/StandaloneCoreDesign.md §4.3 ("Ops live in a List<NowCommandOp> - a struct with an Op enum
// and payload fields"), §3.10 (the type table places NowCommandOp and NowCommandContext in the NowUI.Engine
// namespace), §1.2 (the allocation rule).
//
// Why one wide struct rather than a class hierarchy or a discriminated union of small structs: §1.2 forbids
// allocation on steady-state paths, and NowUI re-records its command buffers every frame (NowRenderer.Draw,
// NowGlassRenderer). A List<NowCommandOp> that is Clear()ed and refilled reuses its backing array forever and
// allocates nothing; one op object per call would allocate on every frame of every mask, blur and glass pass. The
// cost is a struct wider than any single op needs, paid once into a list that never grows again.
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine
{
    /// <summary>Which command a <see cref="NowCommandOp"/> is (design §4.3).</summary>
    internal enum NowCommandOpType
    {
        /// <summary>Bind a target and set the full viewport with it.</summary>
        SetRenderTarget = 0,

        /// <summary>Set the viewport inside the bound target.</summary>
        SetViewport = 1,

        /// <summary>Set the view and projection matrices.</summary>
        SetViewProjectionMatrices = 2,

        /// <summary>Clear the bound target.</summary>
        ClearRenderTarget = 3,

        /// <summary>Allocate a temporary target under a shader property id.</summary>
        GetTemporaryRT = 4,

        /// <summary>Return the temporary allocated under a shader property id.</summary>
        ReleaseTemporaryRT = 5,

        /// <summary>Draw one sub-mesh.</summary>
        DrawMesh = 6,

        /// <summary>Draw vertices the vertex shader generates.</summary>
        DrawProcedural = 7,

        /// <summary>Copy a source into a destination, optionally through a material.</summary>
        Blit = 8,

        /// <summary>Write a global float.</summary>
        SetGlobalFloat = 9,

        /// <summary>Write a global int.</summary>
        SetGlobalInt = 10,

        /// <summary>Write a global vector. <c>SetGlobalColor</c> records one of these, as Unity's does.</summary>
        SetGlobalVector = 11,

        /// <summary>Write a global matrix.</summary>
        SetGlobalMatrix = 12,

        /// <summary>Write a global texture, either by handle or by resolving an identifier at replay time.</summary>
        SetGlobalTexture = 13,

        /// <summary>Open a profiler scope.</summary>
        BeginSample = 14,

        /// <summary>Close a profiler scope.</summary>
        EndSample = 15,
    }

    /// <summary>
    /// One recorded command. Fields are shared between op types - <see cref="type"/> says which of them mean
    /// anything - and every field's meaning per op is spelled out on the field itself.
    /// </summary>
    /// <remarks>
    /// <para><b>Reference payloads are stored by reference, with one exception.</b> A <see cref="Mesh"/>,
    /// <see cref="Material"/>, <see cref="Texture"/> or <c>Vector4[]</c> is recorded as the handle the caller passed,
    /// because that is what Unity records and what NowUI expects: a material mutated between recording and execution
    /// draws with its new values. The exception is <see cref="properties"/>, which <c>CommandBuffer.DrawMesh</c>
    /// <i>snapshots</i> - Unity copies the block too, and <c>NowMaskShader</c> hands one shared block to consecutive
    /// batches and mutates it between them (design §3.5, §4.3).</para>
    /// </remarks>
    [Serializable]
    internal struct NowCommandOp
    {
        /// <summary>Which command this is.</summary>
        internal NowCommandOpType type;

        /// <summary>
        /// <c>SetRenderTarget</c>: the colour target. <c>Blit</c>: the destination. <c>SetGlobalTexture</c>: the
        /// identifier to resolve when <see cref="resolveTexture"/> is set.
        /// </summary>
        internal RenderTargetIdentifier target;

        /// <summary><c>SetRenderTarget</c>: the depth target when <see cref="hasDepthTarget"/> is set. <c>Blit</c>: the source.</summary>
        internal RenderTargetIdentifier source;

        /// <summary><c>SetViewProjectionMatrices</c>: the view matrix. <c>DrawMesh</c>/<c>DrawProcedural</c>: the model matrix. <c>SetGlobalMatrix</c>: the value.</summary>
        internal Matrix4x4 matrixA;

        /// <summary><c>SetViewProjectionMatrices</c>: the projection matrix.</summary>
        internal Matrix4x4 matrixB;

        /// <summary><c>GetTemporaryRT</c>: what to allocate.</summary>
        internal RenderTextureDescriptor descriptor;

        /// <summary><c>SetViewport</c>: the viewport in pixels of the bound target.</summary>
        internal Rect rect;

        /// <summary><c>ClearRenderTarget</c>: the clear colour.</summary>
        internal Color color;

        /// <summary><c>SetGlobalVector</c>: the value. <c>SetGlobalColor</c> records the colour here as a vector.</summary>
        internal Vector4 vector;

        /// <summary><c>Blit</c>: the source-rect scale.</summary>
        internal Vector2 scale;

        /// <summary><c>Blit</c>: the source-rect offset.</summary>
        internal Vector2 offset;

        /// <summary><c>DrawMesh</c>: the mesh.</summary>
        internal Mesh mesh;

        /// <summary><c>DrawMesh</c>/<c>DrawProcedural</c>/<c>Blit</c>: the material, or null for a plain copy.</summary>
        internal Material material;

        /// <summary><c>DrawMesh</c>/<c>DrawProcedural</c>: a snapshot of the caller's block, or null.</summary>
        internal MaterialPropertyBlock properties;

        /// <summary><c>Blit</c>: the source texture when the caller gave a handle. <c>SetGlobalTexture</c>: the value.</summary>
        internal Texture texture;

        /// <summary><c>BeginSample</c>/<c>EndSample</c>: the scope name.</summary>
        internal string name;

        /// <summary><c>GetTemporaryRT</c>/<c>ReleaseTemporaryRT</c>/<c>SetGlobal*</c>: the shader property id.</summary>
        internal int nameID;

        /// <summary><c>DrawMesh</c>: sub-mesh index. <c>DrawProcedural</c>: vertex count. <c>GetTemporaryRT</c>: filter mode. <c>SetGlobalInt</c>: the value.</summary>
        internal int intA;

        /// <summary><c>DrawMesh</c>/<c>DrawProcedural</c>/<c>Blit</c>: the shader pass. <c>DrawProcedural</c> also uses <see cref="topology"/> and <see cref="intC"/>.</summary>
        internal int intB;

        /// <summary><c>DrawProcedural</c>: instance count. <c>Blit</c>: the source depth slice.</summary>
        internal int intC;

        /// <summary><c>Blit</c>: the destination depth slice.</summary>
        internal int intD;

        /// <summary><c>ClearRenderTarget</c>: the depth value. <c>SetGlobalFloat</c>: the value.</summary>
        internal float floatA;

        /// <summary><c>DrawProcedural</c>: the primitive topology.</summary>
        internal MeshTopology topology;

        /// <summary><c>ClearRenderTarget</c>: whether depth is cleared.</summary>
        internal bool clearDepth;

        /// <summary><c>ClearRenderTarget</c>: whether colour is cleared.</summary>
        internal bool clearColor;

        /// <summary><c>SetRenderTarget</c>: whether <see cref="source"/> carries a separate depth target.</summary>
        internal bool hasDepthTarget;

        /// <summary><c>Blit</c>: whether the source is <see cref="texture"/> (true) or the <see cref="source"/> identifier (false).</summary>
        internal bool hasSourceTexture;

        /// <summary><c>SetGlobalTexture</c>: whether the value is <see cref="target"/> resolved at replay time rather than <see cref="texture"/>.</summary>
        internal bool resolveTexture;
    }
}
