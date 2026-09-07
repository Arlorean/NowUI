// Mirrors UnityEngine.Graphics for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 ("Graphics.cs" - the exact member list, and the note that
// Blit leaves the destination bound), §4.2 (NowImmediate, which every member here delegates to), §4.3
// (ExecuteCommandBuffer restores the previously active target).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.Graphics` - seven core files call
// DrawMeshNow, ExecuteCommandBuffer and Blit.
//
// Behaviours that look odd and are deliberate:
//   * Blit leaves the destination bound afterwards, so `RenderTexture.active == dest`. That is Unity's convention,
//     and NowUI's own callers (NowSdfImageField.ClearTarget, NowSdfImageField.Bake) save and restore
//     RenderTexture.active around every blit precisely because of it - remove the behaviour and their restore puts
//     the wrong target back.
//   * ExecuteCommandBuffer restores what was bound before it ran. Now.cs:2158/2170 reads RenderTexture.active
//     immediately afterwards and expects its own target, not whatever the buffer left behind.
//   * DrawMeshNow draws with the material Material.SetPass last selected. Unity's immediate mode works the same way:
//     the pass is device state, not a parameter.
using NowUI.Engine;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// The immediate-mode entry points: submit a mesh now, replay a recorded buffer, copy a texture, choose a target.
    /// </summary>
    public static class Graphics
    {
        /// <summary>
        /// Replays <paramref name="buffer"/> into the registered backend, then restores the target that was bound
        /// before the call.
        /// </summary>
        /// <remarks>
        /// The restore is conditional: a buffer that never rebound anything costs no extra
        /// <c>SetRenderTarget</c>/<c>SetViewport</c> pair, which keeps the recording backend's log of a NowUI frame
        /// free of binds NowUI did not ask for.
        /// </remarks>
        public static void ExecuteCommandBuffer(CommandBuffer buffer)
        {
            if (buffer == null)
                throw new System.ArgumentNullException(nameof(buffer));

            NowRenderTarget previous = NowImmediate.activeTarget;

            buffer.Execute(NowRuntime.backend);

            NowRenderTarget current = NowImmediate.activeTarget;

            // ReferenceEquals on the texture, not ==: two *different* destroyed targets both read as null through the
            // fake-null operator, and restoring the wrong one would be worse than restoring nothing.
            if (!ReferenceEquals(current.texture, previous.texture) ||
                current.mipLevel != previous.mipLevel ||
                current.depthSlice != previous.depthSlice ||
                current.face != previous.face)
            {
                NowImmediate.Bind(in previous, null, NowRuntime.backend);
            }
        }

        /// <summary>Draws sub-mesh 0 with the material <c>Material.SetPass</c> last selected.</summary>
        public static void DrawMeshNow(Mesh mesh, Matrix4x4 matrix)
        {
            NowImmediate.DrawMeshNow(mesh, in matrix, 0);
        }

        /// <summary>Draws one sub-mesh with the material <c>Material.SetPass</c> last selected.</summary>
        public static void DrawMeshNow(Mesh mesh, Matrix4x4 matrix, int materialIndex)
        {
            NowImmediate.DrawMeshNow(mesh, in matrix, materialIndex);
        }

        /// <summary>Draws sub-mesh 0 at a position and rotation, with unit scale.</summary>
        public static void DrawMeshNow(Mesh mesh, Vector3 position, Quaternion rotation)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
            NowImmediate.DrawMeshNow(mesh, in matrix, 0);
        }

        /// <summary>Draws one sub-mesh at a position and rotation, with unit scale.</summary>
        public static void DrawMeshNow(Mesh mesh, Vector3 position, Quaternion rotation, int materialIndex)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
            NowImmediate.DrawMeshNow(mesh, in matrix, materialIndex);
        }

        /// <summary>Copies <paramref name="source"/> into <paramref name="dest"/>, which stays bound afterwards.</summary>
        public static void Blit(Texture source, RenderTexture dest)
        {
            NowImmediate.Blit(source, dest, null, 0, Vector2.one, Vector2.zero);
        }

        /// <summary>Copies through <paramref name="mat"/>; <paramref name="pass"/> -1 means every pass, as in Unity.</summary>
        public static void Blit(Texture source, RenderTexture dest, Material mat, int pass = -1)
        {
            NowImmediate.Blit(source, dest, mat, pass, Vector2.one, Vector2.zero);
        }

        /// <summary>Copies through <paramref name="mat"/> into whatever is currently bound.</summary>
        public static void Blit(Texture source, Material mat, int pass = -1)
        {
            NowRenderTarget destination = NowImmediate.activeTarget;
            Vector2 scale = Vector2.one;
            Vector2 offset = Vector2.zero;

            NowImmediate.Blit(source, in destination, mat, pass, in scale, in offset, 0, 0, null, NowRuntime.backend);
        }

        /// <summary>Copies a scaled and offset sub-rect of <paramref name="source"/>.</summary>
        public static void Blit(Texture source, RenderTexture dest, Vector2 scale, Vector2 offset)
        {
            NowImmediate.Blit(source, dest, null, 0, scale, offset);
        }

        /// <summary>Binds a target, or the host's back buffer when <paramref name="rt"/> is null.</summary>
        public static void SetRenderTarget(RenderTexture rt)
        {
            NowImmediate.SetActive(rt);
        }

        /// <summary>Binds the target an identifier names. A name id has no meaning outside a command buffer's replay.</summary>
        public static void SetRenderTarget(RenderTargetIdentifier rt)
        {
            NowRenderTarget target = NowImmediate.Resolve(in rt, null);
            NowImmediate.Bind(in target, null, NowRuntime.backend);
        }

        /// <summary>GPU-side copy with no shader and no target binding.</summary>
        public static void CopyTexture(Texture src, Texture dst)
        {
            NowRuntime.backend.CopyTexture(src, dst);
        }
    }
}
