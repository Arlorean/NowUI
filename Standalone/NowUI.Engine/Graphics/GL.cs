// Mirrors UnityEngine.GL for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 ("GL.cs" - the exact member list, and the note that
// Begin/End/Vertex/Color/TexCoord are omitted because NowUI never calls them), §4.2 (NowImmediate owns the state
// these members read and write).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.GL` - three core files call
// PushMatrix, PopMatrix, LoadIdentity, LoadProjectionMatrix and Clear, and nothing else.
//
// Behaviours that look odd and are deliberate:
//   * GetGPUProjectionMatrix returns its argument unchanged. Under Unity it folds in the platform's clip-space
//     convention (z range, y direction, and whether the target is a texture). Here the backend owns clip space
//     (design §3.5, §4.7), so folding a convention in on the CPU would apply it twice.
//   * The immediate-mode vertex API (Begin/Vertex/Color/TexCoord/End) is absent. It is not a gap: NowUI submits
//     meshes, and a shim member nothing calls is a member nobody has verified.
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// Low-level graphics state: the matrix stack, the target clear, and the two render-state toggles NowUI's hosts
    /// touch. Every member is a view onto <c>NowImmediate</c>, which is where the shim keeps its CPU-side state.
    /// </summary>
    public static class GL
    {
        /// <summary>Saves the modelview and projection matrices together, as Unity's does.</summary>
        public static void PushMatrix()
        {
            NowImmediate.PushMatrix();
        }

        /// <summary>
        /// Restores the matrices saved by the matching <see cref="PushMatrix"/>. An unmatched pop reports through the
        /// host logger and leaves the matrices alone - it must not take a frame down mid-paint.
        /// </summary>
        public static void PopMatrix()
        {
            NowImmediate.PopMatrix();
        }

        /// <summary>Loads the identity modelview matrix. The projection is untouched.</summary>
        public static void LoadIdentity()
        {
            NowImmediate.LoadIdentity();
        }

        /// <summary>Loads a projection matrix verbatim. See <see cref="GetGPUProjectionMatrix"/> for why nothing is folded in.</summary>
        public static void LoadProjectionMatrix(Matrix4x4 mat)
        {
            NowImmediate.LoadProjection(mat);
        }

        /// <summary>
        /// Sets up a projection whose x and y run 0..1 over the target, with an identity modelview.
        /// </summary>
        /// <remarks>
        /// NowUI does not call this (the inventory records only Push/Pop/LoadIdentity/LoadProjectionMatrix/Clear), so
        /// there is no captured oracle for the near and far planes; the -1..100 range is the one Unity documents for
        /// its ortho helpers and the one <see cref="LoadPixelMatrix()"/> uses here, so the two agree.
        /// </remarks>
        public static void LoadOrtho()
        {
            NowImmediate.LoadProjection(Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 100f));
            NowImmediate.LoadIdentity();
        }

        /// <summary>Sets up a projection in which one unit is one pixel of the bound target, origin bottom-left.</summary>
        public static void LoadPixelMatrix()
        {
            NowRenderTarget target = NowImmediate.activeTarget;

            // A zero-sized target - nothing bound yet, on a host that reports no screen - would produce a degenerate
            // projection; 1x1 keeps the matrix invertible and the failure visible as "everything at one pixel".
            float width = target.width > 0 ? target.width : 1f;
            float height = target.height > 0 ? target.height : 1f;

            LoadPixelMatrix(0f, width, 0f, height);
        }

        /// <summary>Sets up a projection mapping the given rectangle to the whole target, with an identity modelview.</summary>
        public static void LoadPixelMatrix(float left, float right, float bottom, float top)
        {
            NowImmediate.LoadProjection(Matrix4x4.Ortho(left, right, bottom, top, -1f, 100f));
            NowImmediate.LoadIdentity();
        }

        /// <summary>Right-multiplies the modelview matrix, so <paramref name="m"/> applies before what is already there.</summary>
        public static void MultMatrix(Matrix4x4 m)
        {
            NowImmediate.MultMatrix(m);
        }

        /// <summary>The current modelview matrix.</summary>
        public static Matrix4x4 modelview
        {
            get { return NowImmediate.modelView; }
            set { NowImmediate.modelView = value; }
        }

        /// <summary>
        /// The projection matrix a GPU should be given for <paramref name="proj"/>. An identity transform in the
        /// standalone build: clip-space conventions - the z range, the y flip for texture targets - belong to the
        /// backend, which is the only component that knows what API it is talking to (design §3.5, §4.7).
        /// </summary>
        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 proj, bool renderIntoTexture)
        {
            return proj;
        }

        /// <summary>Clears the bound target, with depth 1 (the far plane), which is Unity's default.</summary>
        public static void Clear(bool clearDepth, bool clearColor, Color backgroundColor)
        {
            NowImmediate.Clear(clearDepth, clearColor, in backgroundColor, 1f);
        }

        /// <summary>Clears the bound target to an explicit depth.</summary>
        public static void Clear(bool clearDepth, bool clearColor, Color backgroundColor, float depth)
        {
            NowImmediate.Clear(clearDepth, clearColor, in backgroundColor, depth);
        }

        /// <summary>Sets the viewport inside the bound target, without rebinding it.</summary>
        public static void Viewport(Rect pixelRect)
        {
            NowImmediate.SetViewport(in pixelRect);
        }

        /// <summary>
        /// Whether the backend flips its triangle winding. State the shim records and a backend reads; the null
        /// backend has no culling to invert.
        /// </summary>
        public static bool invertCulling
        {
            get { return NowImmediate.invertCulling; }
            set { NowImmediate.invertCulling = value; }
        }

        /// <summary>Whether writes to an sRGB target are converted. Recorded state, read by a backend that has a choice.</summary>
        public static bool sRGBWrite
        {
            get { return NowImmediate.sRGBWrite; }
            set { NowImmediate.sRGBWrite = value; }
        }
    }
}
