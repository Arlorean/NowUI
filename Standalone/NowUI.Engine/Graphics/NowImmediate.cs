// The shim-side immediate-mode state machine. Not a UnityEngine type: it is the single place UnityEngine.GL,
// UnityEngine.Graphics, UnityEngine.RenderTexture.active, UnityEngine.Material.SetPass and the replay of
// UnityEngine.Rendering.CommandBuffer all funnel through, which is what makes "one immediate-mode render contract"
// (design §4, opening rule) true rather than aspirational: the recorded path and the immediate path reach the backend
// through the same three helpers below, so a backend cannot see two different orderings.
// Design: Docs/Standalone/StandaloneCoreDesign.md §4.2 (this type and NowCommandContext, member for member),
// §4.1 (the backend contract; invariants 5 "SetRenderTarget is always followed by SetViewport", 6 "every draw is
// preceded by at least one SetViewProjection" and 7 "Blit leaves the destination bound" are enforced here),
// §4.3 (CommandBuffer.Execute calls Resolve and Bind), §3.5 (RenderTexture.active; binding a target bumps its
// updateCount), §6.2 (NowRuntime.ResetAll).
//
// Behaviours that look odd and are deliberate:
//   * The CPU-side state (target, viewport, matrices, selected pass) lives here rather than in the backend, because
//     §4 makes the backend a thin GPU adapter with no policy. A backend never has to answer "what is bound?".
//   * `Resolve` turns every RenderTargetIdentifier flavour into a concrete RenderTexture-or-back-buffer, so a backend
//     never sees a nameID and never needs a temporary pool of its own (§4.1 guarantee 4).
//   * Binding a target increments its updateCount. NowSdf uses updateCount as its staleness signal
//     (hazard D.1 #11), and a target that was drawn into is exactly what "stale" has to mean.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine
{
    /// <summary>
    /// The CPU-side render state the shim owns: what is bound, what the viewport is, the GL matrix stack and the
    /// material pass <c>Material.SetPass</c> selected for the next immediate draw.
    /// </summary>
    /// <remarks>
    /// Single-threaded, like the rest of the shim's render path (design §4.1 guarantee 8). Everything here is static
    /// because Unity's own immediate mode is: <c>GL.PushMatrix</c> and <c>RenderTexture.active</c> are process state,
    /// not instance state, and NowUI calls them as such.
    /// </remarks>
    internal static class NowImmediate
    {
        /// <summary>
        /// The target currently bound. <c>RenderTexture.active</c> is a view onto its <c>texture</c>, so the default
        /// value - a back buffer with a null texture - is what "nothing bound" reads as, exactly as under Unity.
        /// </summary>
        internal static NowRenderTarget activeTarget;

        /// <summary>The GL modelview matrix. Identity until something loads one.</summary>
        internal static Matrix4x4 modelView = Matrix4x4.identity;

        /// <summary>The GL projection matrix. Identity until something loads one.</summary>
        internal static Matrix4x4 projection = Matrix4x4.identity;

        /// <summary>
        /// The material and pass the next <c>Graphics.DrawMeshNow</c> draws with, recorded by
        /// <c>Material.SetPass</c>. Unity has no getter for this; the shim keeps it because the immediate path has to
        /// hand the backend a material and Unity's device state is where that material lives.
        /// </summary>
        internal static (Material material, int pass) activePass;

        /// <summary>Backing field for <c>GL.invertCulling</c>. State, so <see cref="Reset"/> has to clear it.</summary>
        internal static bool invertCulling;

        /// <summary>Backing field for <c>GL.sRGBWrite</c>. State, so <see cref="Reset"/> has to clear it.</summary>
        internal static bool sRGBWrite;

        // Both matrices move together, because GL.PushMatrix saves the whole transform state and GL.PopMatrix
        // restores it. Storing them as one struct is what makes the pair atomic.
        private static readonly Stack<MatrixState> s_MatrixStack = new Stack<MatrixState>(8);

        // The command contexts CommandBuffer.Execute borrows. A stack rather than a single instance so that a nested
        // execution - which the shim does not do today, but a future host half might - cannot hand two executions the
        // same temporary table. Pooled because §4.3 requires the executor to allocate nothing per op.
        private static readonly Stack<NowCommandContext> s_ContextPool = new Stack<NowCommandContext>(2);

        private static bool s_Hooked;

        /// <summary>The state <c>GL.PushMatrix</c> saves: both matrices, restored together by <c>GL.PopMatrix</c>.</summary>
        private struct MatrixState
        {
            internal Matrix4x4 modelView;
            internal Matrix4x4 projection;
        }

        // ------------------------------------------------------------------------------------------ render targets

        /// <summary>
        /// Binds <paramref name="rt"/> (null means the host's back buffer), sets the full viewport with it, and marks
        /// the target updated. This is the setter behind <c>RenderTexture.active</c>.
        /// </summary>
        internal static void SetActive(RenderTexture rt)
        {
            Bind(ResolveTarget(rt), null, NowRuntime.backend);
        }

        /// <summary>
        /// Binds a resolved target and sets the viewport to the whole of it. Every path that changes the bound target
        /// goes through here, which is what makes invariant 5 structural rather than a convention.
        /// </summary>
        /// <param name="context">The command context to keep in sync during a replay, or null on the immediate path.</param>
        /// <param name="backend">
        /// The backend to bind on. Passed rather than read from <c>NowRuntime</c> so that a
        /// <c>CommandBuffer.Execute(backend)</c> replays into the backend it was handed - the recording backend, in the
        /// semantics suite - and not into whatever happens to be registered.
        /// </param>
        internal static void Bind(in NowRenderTarget target, NowCommandContext context, INowRenderBackend backend)
        {
            EnsureHooked();

            Rect viewport = new Rect(0f, 0f, target.width, target.height);

            activeTarget = target;

            if (context != null)
            {
                context.current = target;
                context.viewport = viewport;
            }

            backend.SetRenderTarget(in target);
            backend.SetViewport(in viewport);

            MarkTargetUpdated(in target);
        }

        /// <summary>
        /// Records that a target was rendered into without the shim having bound it - which is what a
        /// <see cref="Blit"/> does, since <c>INowRenderBackend.Blit</c> leaves its destination bound (invariant 7).
        /// No <c>SetRenderTarget</c> is issued, so invariant 5 is not implicated.
        /// </summary>
        internal static void AdoptBlitDestination(in NowRenderTarget target, NowCommandContext context)
        {
            activeTarget = target;

            if (context != null)
                context.current = target;

            MarkTargetUpdated(in target);
        }

        // ------------------------------------------------------------------------------------------------ matrices

        /// <summary>Saves both matrices.</summary>
        internal static void PushMatrix()
        {
            EnsureHooked();
            s_MatrixStack.Push(new MatrixState { modelView = modelView, projection = projection });
        }

        /// <summary>
        /// Restores both matrices. An underflow leaves the matrices alone and reports through the host logger, which
        /// is Unity's shape too: <c>GL.PopMatrix</c> without a matching push is an error message, not an exception -
        /// a mismatched pair inside a paint routine must not take the frame down.
        /// </summary>
        internal static void PopMatrix()
        {
            EnsureHooked();

            if (s_MatrixStack.Count == 0)
            {
                INowLogger logger = NowRuntime.host.logger;

                if (logger != null)
                    logger.Log(LogType.Error, "GL.PopMatrix: matrix stack underflow (more pops than pushes).", null, null);

                return;
            }

            MatrixState state = s_MatrixStack.Pop();
            modelView = state.modelView;
            projection = state.projection;
        }

        /// <summary>Loads the identity modelview matrix. The projection is untouched, as Unity's is.</summary>
        internal static void LoadIdentity()
        {
            modelView = Matrix4x4.identity;
        }

        /// <summary>Loads a projection matrix verbatim - see <c>GL.GetGPUProjectionMatrix</c> for why no clip-space fix-up happens here.</summary>
        internal static void LoadProjection(Matrix4x4 matrix)
        {
            projection = matrix;
        }

        /// <summary>Right-multiplies the modelview matrix, so the new transform applies before the existing one.</summary>
        internal static void MultMatrix(Matrix4x4 matrix)
        {
            modelView = modelView * matrix;
        }

        // --------------------------------------------------------------------------------------------------- draws

        /// <summary>
        /// Draws one sub-mesh with the material <c>Material.SetPass</c> last selected, under the current GL matrices.
        /// </summary>
        /// <remarks>
        /// The view-projection is pushed on every call rather than only when it changed. That is what makes invariant
        /// 6 hold for the immediate path unconditionally, and it costs two matrix copies against a draw call - the
        /// backend is free to skip the upload when the values did not move.
        /// </remarks>
        internal static void DrawMeshNow(Mesh mesh, in Matrix4x4 model, int subMesh)
        {
            EnsureHooked();

            INowRenderBackend backend = NowRuntime.backend;

            backend.SetViewProjection(in modelView, in projection);
            backend.DrawMesh(mesh, subMesh, in model, activePass.material, activePass.pass, null);
        }

        /// <summary>Clears the bound target.</summary>
        internal static void Clear(bool clearDepth, bool clearColor, in Color color, float depth)
        {
            EnsureHooked();
            NowRuntime.backend.ClearRenderTarget(clearDepth, clearColor, in color, depth);
        }

        /// <summary>Sets the viewport inside the bound target, without rebinding it.</summary>
        internal static void SetViewport(in Rect pixelRect)
        {
            EnsureHooked();
            NowRuntime.backend.SetViewport(in pixelRect);
        }

        /// <summary>
        /// Copies <paramref name="source"/> into <paramref name="destination"/>, leaving the destination bound
        /// (invariant 7, and Unity's convention: <c>NowSdfImageField</c> saves and restores
        /// <c>RenderTexture.active</c> around every blit precisely because of it).
        /// </summary>
        internal static void Blit(Texture source, RenderTexture destination, Material material, int pass,
                                  Vector2 scale, Vector2 offset)
        {
            NowRenderTarget target = ResolveTarget(destination);
            Blit(source, in target, material, pass, in scale, in offset, 0, 0, null, NowRuntime.backend);
        }

        /// <summary>The resolved form, shared by the immediate path and by <c>CommandBuffer</c> replay.</summary>
        internal static void Blit(Texture source, in NowRenderTarget destination, Material material, int pass,
                                  in Vector2 scale, in Vector2 offset, int sourceDepthSlice, int destinationDepthSlice,
                                  NowCommandContext context, INowRenderBackend backend)
        {
            EnsureHooked();

            backend.Blit(source, in destination, material, pass, in scale, in offset,
                         sourceDepthSlice, destinationDepthSlice);

            AdoptBlitDestination(in destination, context);
        }

        // ---------------------------------------------------------------------------------------------- resolution

        /// <summary>
        /// Turns a <see cref="RenderTargetIdentifier"/> into a concrete target (design §4.2): <c>None</c> is the
        /// current target, <c>CameraTarget</c>/<c>CurrentActive</c> are the back buffer, a name id is looked up in
        /// <paramref name="context"/>'s temporary table, and a texture is that texture.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// A <c>Texture2D</c> named as a draw target (Unity throws too - it is not a render target), a name id with no
        /// live temporary behind it, or a builtin target the standalone build has no equivalent for.
        /// </exception>
        internal static NowRenderTarget Resolve(in RenderTargetIdentifier id, NowCommandContext context)
        {
            switch (id.kind)
            {
                case RenderTargetIdentifier.Kind.None:
                    return Current(context);

                case RenderTargetIdentifier.Kind.BuiltinType:
                    switch (id.builtin)
                    {
                        // "The current active target" and "the camera's target" both mean the host's framebuffer in a
                        // build with no camera stack - which is what NowRenderer names when it wants the screen.
                        case BuiltinRenderTextureType.CameraTarget:
                        case BuiltinRenderTextureType.CurrentActive:
                            return BackBuffer();

                        // Kind.BuiltinType carrying None is how `new RenderTargetIdentifier(BuiltinRenderTextureType.None)`
                        // arrives; Unity treats it as "leave the target alone", the same as a default identifier.
                        case BuiltinRenderTextureType.None:
                            return Current(context);

                        default:
                            // Depth / DepthNormals / ResolvedDepth are engine-owned buffers no standalone backend
                            // produces. Failing loudly beats silently drawing to the screen instead.
                            throw new ArgumentException(
                                "RenderTargetIdentifier: the builtin target " + id.builtin +
                                " has no standalone equivalent.", nameof(id));
                    }

                case RenderTargetIdentifier.Kind.NameID:
                {
                    RenderTexture temporary = ResolveTemporary(id.nameID, context);
                    return MakeTarget(temporary, id.mipLevel, id.face, id.depthSlice);
                }

                default:
                {
                    Texture texture = id.texture;

                    // A null texture handle is not a Texture2D and is not a live target either; Unity binds the
                    // framebuffer, which is also the only thing the shim could do.
                    if (ReferenceEquals(texture, null))
                        return BackBuffer();

                    RenderTexture rt = texture as RenderTexture;

                    if (ReferenceEquals(rt, null))
                        throw new ArgumentException(
                            "RenderTargetIdentifier: '" + texture.name +
                            "' is not a RenderTexture and cannot be used as a render target.", nameof(id));

                    return MakeTarget(rt, id.mipLevel, id.face, id.depthSlice);
                }
            }
        }

        /// <summary>
        /// The texture an identifier names, for the cases that read rather than write: a blit source and
        /// <c>CommandBuffer.SetGlobalTexture(int, RenderTargetIdentifier)</c>. Null means the back buffer.
        /// </summary>
        internal static Texture ResolveTexture(in RenderTargetIdentifier id, NowCommandContext context)
        {
            switch (id.kind)
            {
                case RenderTargetIdentifier.Kind.None:
                    return Current(context).texture;

                case RenderTargetIdentifier.Kind.BuiltinType:
                    switch (id.builtin)
                    {
                        case BuiltinRenderTextureType.CameraTarget:
                        case BuiltinRenderTextureType.CurrentActive:
                            return null;

                        case BuiltinRenderTextureType.None:
                            return Current(context).texture;

                        default:
                            throw new ArgumentException(
                                "RenderTargetIdentifier: the builtin target " + id.builtin +
                                " has no standalone equivalent.", nameof(id));
                    }

                case RenderTargetIdentifier.Kind.NameID:
                    return ResolveTemporary(id.nameID, context);

                default:
                    return id.texture;
            }
        }

        /// <summary>Builds the target a <see cref="RenderTexture"/> handle stands for; null is the back buffer.</summary>
        internal static NowRenderTarget ResolveTarget(RenderTexture rt)
        {
            // The fake-null operator on purpose: assigning a *destroyed* render texture to RenderTexture.active means
            // "the back buffer" under Unity, because a destroyed handle reads as null everywhere the core looks.
            if (rt == null)
                return BackBuffer();

            return MakeTarget(rt, 0, CubemapFace.Unknown, RenderTargetIdentifier.AllDepthSlices);
        }

        /// <summary>The host's framebuffer at the size <c>INowHostServices.screen</c> reports right now.</summary>
        internal static NowRenderTarget BackBuffer()
        {
            NowScreenInfo screen = NowRuntime.host.screen;
            return NowRenderTarget.BackBuffer(screen.width, screen.height);
        }

        // ------------------------------------------------------------------------------------ command-buffer scope

        /// <summary>Borrows a context for one <c>CommandBuffer.Execute</c>, seeded with what is bound right now.</summary>
        internal static NowCommandContext RentContext()
        {
            NowCommandContext context = s_ContextPool.Count > 0 ? s_ContextPool.Pop() : new NowCommandContext();

            context.temporaries.Clear();
            context.current = activeTarget;
            context.viewport = new Rect(0f, 0f, activeTarget.width, activeTarget.height);
            return context;
        }

        /// <summary>Returns a context to the pool. The dictionary keeps its capacity, so the next replay allocates nothing.</summary>
        internal static void ReturnContext(NowCommandContext context)
        {
            if (context == null)
                return;

            context.temporaries.Clear();
            context.current = default;
            context.viewport = default;
            s_ContextPool.Push(context);
        }

        // --------------------------------------------------------------------------------------------------- reset

        /// <summary>
        /// Drops every scrap of immediate-mode state (design §6.2). Nothing is unbound on the backend: a reset is the
        /// standalone domain reload, after which the backend's own state is gone too.
        /// </summary>
        internal static void Reset()
        {
            activeTarget = default;
            modelView = Matrix4x4.identity;
            projection = Matrix4x4.identity;
            activePass = default;
            invertCulling = false;
            sRGBWrite = false;
            s_MatrixStack.Clear();
            s_ContextPool.Clear();
        }

        // ------------------------------------------------------------------------------------------------ internals

        private static NowRenderTarget Current(NowCommandContext context)
        {
            return context != null ? context.current : activeTarget;
        }

        private static RenderTexture ResolveTemporary(int nameID, NowCommandContext context)
        {
            RenderTexture temporary;

            if (context == null || !context.temporaries.TryGetValue(nameID, out temporary))
                throw new ArgumentException(
                    "RenderTargetIdentifier: no temporary render texture is allocated for property id " +
                    nameID.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " ('" + Shader.IDToName(nameID) + "'). GetTemporaryRT must run before anything names it.",
                    nameof(nameID));

            return temporary;
        }

        private static NowRenderTarget MakeTarget(RenderTexture rt, int mipLevel, CubemapFace face, int depthSlice)
        {
            // Creation is lazy everywhere in the shim (design §3.5), so binding is one of the moments the GPU
            // resource has to exist. IsCreated() also clears the flag when a backend reports the target lost, so a
            // target that died to a context reset is rebuilt here rather than bound as a dead handle.
            if (!rt.IsCreated())
                rt.Create();

            int mip = mipLevel < 0 ? 0 : mipLevel;

            return new NowRenderTarget(rt, mip, face, depthSlice, MipSize(rt.width, mip), MipSize(rt.height, mip));
        }

        /// <summary>The pixel size of one mip level, floored at 1 the way every graphics API defines it.</summary>
        private static int MipSize(int size, int mip)
        {
            int result = size >> mip;
            return result < 1 ? 1 : result;
        }

        private static void MarkTargetUpdated(in NowRenderTarget target)
        {
            RenderTexture texture = target.texture;

            // ReferenceEquals, not ==: a destroyed target is a bug worth surfacing through the backend, and bumping
            // a counter on it is harmless, whereas skipping the bump for a live one would break NowSdf's staleness
            // check (hazard D.1 #11).
            if (!ReferenceEquals(texture, null))
                texture.IncrementUpdateCount();
        }

        private static void EnsureHooked()
        {
            if (s_Hooked)
                return;

            s_Hooked = true;

            // Subscribed lazily, exactly as the temporary pool does, to keep the static-initialiser graph shallow
            // (hazard D.1 #4): a core type that only ever touches Texture2D never brings the render state - or its
            // reset hook - into existence.
            NowRuntime.onEngineReset += Reset;
        }
    }

    /// <summary>
    /// The per-execution state of a <c>CommandBuffer</c> replay (design §4.2): the temporaries the buffer allocated
    /// under their property ids, plus what the buffer has bound so far.
    /// </summary>
    /// <remarks>
    /// Pooled by <see cref="NowImmediate.RentContext"/>, because §4.3 requires the executor to allocate nothing per
    /// op and a fresh dictionary per execution would be an allocation per frame.
    /// </remarks>
    internal sealed class NowCommandContext
    {
        /// <summary>Live temporaries by the property id <c>GetTemporaryRT</c> allocated them under.</summary>
        internal readonly Dictionary<int, RenderTexture> temporaries = new Dictionary<int, RenderTexture>();

        /// <summary>What this replay has bound. Seeded with whatever was bound when the replay started.</summary>
        internal NowRenderTarget current;

        /// <summary>The viewport this replay last set.</summary>
        internal Rect viewport;
    }
}
