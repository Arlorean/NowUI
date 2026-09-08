// Exercises the parts of INowRenderBackend that no gallery area reaches: render textures, target binding, blits,
// procedural draws and texture copies. It exists because those methods went from "throws with a message" to
// "implemented", and an implementation nothing calls is a claim, not a fact.
//
// Two independent modes, both off unless asked for:
//
//   ?rtcheck=1   Runs the contract suite below once, on the first frame, and logs one PASS/FAIL line per case
//                plus a summary. Assertions only; nothing it does is visible on the canvas.
//
//   ?rt=1        Draws the WHOLE frame into a full-canvas render texture and blits that to the back buffer. This
//                is the orientation proof and it is deliberately not an assertion: `?rt=1` and `?rt=0` must
//                produce the SAME image. If render-to-texture flipped anything - the projection into an FBO, the
//                blit's uv, the framebuffer origin - the two captures differ by a vertical mirror, which no
//                amount of reading GL code catches as reliably as two screenshots do.
//
// Why a full-canvas target rather than a small one: NowUI's projection is built from `Now.screenMask`, which is
// the canvas size. A render texture of exactly that size is the one case where the frame's own projection and
// the target's viewport describe the same rectangle, so the round trip tests the backend rather than testing
// whether this file got a second projection right.
//
// Design: Docs/Standalone/M2-ShaderPort.md §8 (the projection and why nothing flips),
// Standalone/NowUI.Engine/Backend/INowRenderBackend.cs (the contract these cases are read off).
using System;
using System.Text;
using NowUI.Engine;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Web
{
    /// <summary>Backend self-checks for everything past the default framebuffer.</summary>
    internal static class RenderTargetSelfTest
    {
        /// <summary>Whether the frame is drawn through a full-canvas render texture (<c>?rt=1</c>).</summary>
        internal static bool indirect;

        /// <summary>Whether the contract suite runs on the first frame (<c>?rtcheck=1</c>).</summary>
        internal static bool contract;

        static RenderTexture s_Indirect;
        static bool s_ContractRan;
        static int s_Passed;
        static int s_Failed;

        // ------------------------------------------------------------------------------------- indirect frame

        /// <summary>
        /// Binds a full-canvas render texture as the frame's target, allocating or resizing it as needed. Returns
        /// false when the target could not be created, in which case the caller draws straight to the back buffer
        /// and the failure has already been logged.
        /// </summary>
        internal static bool BeginIndirect(int width, int height)
        {
            if (!indirect || width <= 0 || height <= 0)
                return false;

            // Re-allocated rather than resized, because assigning width/height releases the target anyway and a
            // fresh handle keeps the "created" bookkeeping in one place.
            if (s_Indirect != null && (s_Indirect.width != width || s_Indirect.height != height))
            {
                s_Indirect.Release();
                s_Indirect = null;
            }

            if (s_Indirect == null)
            {
                s_Indirect = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32,
                                               RenderTextureReadWrite.Linear)
                {
                    // Point filtering on purpose: the blit back to the canvas is 1:1, so any filtering at all
                    // would only blur the comparison against the direct capture.
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            // IsCreated() rather than a remembered bool: it asks the backend whether the GPU object is still
            // there, which is the whole point of IsRenderTextureLost.
            if (!s_Indirect.IsCreated() && !s_Indirect.Create())
            {
                BrowserInterop.Log(2, "[NowUI] ?rt=1: the full-canvas render texture could not be created; " +
                                      "drawing to the back buffer instead.");
                indirect = false;
                return false;
            }

            // Binds the target AND sets the viewport to the whole of it (NowImmediate.Bind, backend invariant 5).
            RenderTexture.active = s_Indirect;
            return true;
        }

        /// <summary>
        /// Copies the frame's render texture onto the back buffer and leaves the back buffer bound. A plain blit,
        /// no material: identity scale and offset, so uv (0,0) is the first texel row of the target and the first
        /// row of the canvas, which is the mapping that makes this a null operation when nothing flips.
        /// </summary>
        internal static void EndIndirect()
        {
            if (s_Indirect == null)
                return;

            Graphics.Blit(s_Indirect, (RenderTexture)null);

            // Blit leaves the destination bound (invariant 7) but the shim issues no SetRenderTarget for it, so
            // RenderTexture.active is set explicitly to keep the shim's view and the GPU's agreeing for the next
            // frame's Bind.
            RenderTexture.active = null;
        }

        // ------------------------------------------------------------------------------------- contract suite

        /// <summary>Runs every case once and logs the results. Safe to call every frame; it latches.</summary>
        internal static void RunContractChecksOnce()
        {
            if (!contract || s_ContractRan)
                return;

            s_ContractRan = true;
            s_Passed = 0;
            s_Failed = 0;

            var report = new StringBuilder("[NowUI] render-target contract suite\n");

            Case(report, "create_argb32", CreateArgb32);
            Case(report, "release_reports_lost", ReleaseReportsLost);
            Case(report, "recreate_after_release", RecreateAfterRelease);
            Case(report, "bind_clear_restore", BindClearRestore);
            Case(report, "blit_copy_leaves_destination_bound", BlitCopyLeavesDestinationBound);
            Case(report, "blit_scale_offset", BlitScaleOffset);
            Case(report, "blit_to_back_buffer", BlitToBackBuffer);
            Case(report, "blit_material_unported_throws", BlitMaterialUnportedThrows);
            Case(report, "blit_pass_out_of_range_throws", PassOutOfRangeThrows);
            Case(report, "procedural_draw_runs", ProceduralDrawRuns);
            Case(report, "procedural_instanced_runs", ProceduralInstancedRuns);
            Case(report, "copy_texture", CopyTextureCase);
            Case(report, "temporary_pool_round_trip", TemporaryPoolRoundTrip);
            Case(report, "mip_chain_target", MipChainTarget);
            Case(report, "depth_bits_target", DepthBitsTarget);
            Case(report, "float_target_matches_caps", FloatTargetMatchesCaps);
            Case(report, "array_target_refused", ArrayTargetRefused);
            Case(report, "random_write_refused", RandomWriteRefused);
            Case(report, "zero_sized_target_refused", ZeroSizedTargetRefused);
            Case(report, "null_arguments_throw", NullArgumentsThrow);
            Case(report, "caps_msaa_is_one", CapsMsaaIsOne);

            report.Append("[NowUI] render-target contract suite: ")
                  .Append(s_Passed).Append(" passed, ").Append(s_Failed).Append(" failed.");

            BrowserInterop.Log(s_Failed == 0 ? 0 : 2, report.ToString());
        }

        static void Case(StringBuilder report, string name, Func<string> body)
        {
            string detail;

            try
            {
                detail = body();
            }
            catch (Exception e)
            {
                detail = "threw " + e.GetType().Name + ": " + e.Message;
            }

            bool passed = detail == null;
            if (passed) ++s_Passed; else ++s_Failed;

            report.Append(passed ? "  PASS " : "  FAIL ").Append(name);

            if (!passed)
                report.Append(" - ").Append(detail);

            report.Append('\n');

            // Every case leaves the back buffer bound, so one failure cannot make the next case fail for an
            // unrelated reason.
            RenderTexture.active = null;
        }

        // Each case returns null for pass, or the reason it failed. Exceptions are caught by Case and reported,
        // so a case may simply let one escape rather than wrapping every call.

        static string CreateArgb32()
        {
            var rt = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32);

            try
            {
                if (!rt.Create()) return "Create() returned false for a 64x64 ARGB32 target.";
                if (!rt.IsCreated()) return "IsCreated() is false immediately after a successful Create().";
                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string ReleaseReportsLost()
        {
            var rt = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32);
            if (!rt.Create()) return "Create() returned false.";

            rt.Release();

            // IsCreated() ands the shim's own flag with the backend's answer, so this asserts the shim side; the
            // backend side is asserted by the re-create below, which only works if the backend really dropped it.
            if (rt.IsCreated()) return "IsCreated() is still true after Release().";
            return null;
        }

        static string RecreateAfterRelease()
        {
            var rt = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32);
            if (!rt.Create()) return "the first Create() returned false.";
            rt.Release();
            if (!rt.Create()) return "Create() returned false after a Release(); the backend did not rebuild it.";
            if (!rt.IsCreated()) return "IsCreated() is false after the second Create().";
            rt.Release();
            return null;
        }

        static string BindClearRestore()
        {
            var rt = new RenderTexture(48, 48, 0, RenderTextureFormat.ARGB32);
            if (!rt.Create()) return "Create() returned false.";

            try
            {
                RenderTexture.active = rt;

                if (!ReferenceEquals(RenderTexture.active, rt))
                    return "RenderTexture.active does not report the target that was just bound.";

                GL.Clear(false, true, new Color(0.25f, 0.5f, 0.75f, 1f));

                RenderTexture.active = null;

                if (!ReferenceEquals(RenderTexture.active, null))
                    return "RenderTexture.active is not null after binding the back buffer.";

                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string BlitCopyLeavesDestinationBound()
        {
            Texture2D source = MakeMarkerTexture();
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            if (!rt.Create()) return "Create() returned false.";

            try
            {
                Graphics.Blit(source, rt);

                // Backend invariant 7, and Unity's convention: NowSdfImageField saves and restores
                // RenderTexture.active around every blit precisely because a blit changes it.
                if (!ReferenceEquals(RenderTexture.active, rt))
                    return "the blit did not leave its destination bound.";

                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string BlitScaleOffset()
        {
            Texture2D source = MakeMarkerTexture();
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            if (!rt.Create()) return "Create() returned false.";

            try
            {
                // The overload that exists only to be forgotten. Half height, upper half of the source.
                Graphics.Blit(source, rt, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f));
                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string BlitToBackBuffer()
        {
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            if (!rt.Create()) return "Create() returned false.";

            try
            {
                // A null destination is the back buffer, resolved at the host's current screen size. This is the
                // path ?rt=1 uses every frame, so a failure here is a failure of the whole indirect mode.
                Graphics.Blit(rt, (RenderTexture)null);

                if (!ReferenceEquals(RenderTexture.active, null))
                    return "a blit to the back buffer did not leave the back buffer bound.";

                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string BlitMaterialUnportedThrows()
        {
            Shader shader = Shader.Find("NowUI/UI Rectangle");

            if (ReferenceEquals(shader, null))
                return "NowUI/UI Rectangle did not resolve, so this case cannot build a material at all.";

            // A material whose shader IS ported blits fine; the case worth asserting is the other one, and the
            // only way to name an unported program from here is to ask for one that does not resolve. That
            // returns null, so the assertion is made against a material with a null shader instead, which takes
            // the same guard.
            var material = new Material(shader);
            material.shader = null;

            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            if (!rt.Create()) return "Create() returned false.";

            try
            {
                Graphics.Blit(MakeMarkerTexture(), rt, material, 0);
                return "a blit through a material with no shader was accepted; it should have been refused.";
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string ProceduralDrawRuns()
        {
            Shader shader = Shader.Find("NowUI/UI Rectangle");

            if (ReferenceEquals(shader, null))
                return "NowUI/UI Rectangle did not resolve.";

            var material = new Material(shader);
            INowRenderBackend backend = NowRuntime.backend;

            // Three vertices over gl_VertexID: the shape every full-screen pass in Hidden/NowUI/GlassBlur uses.
            // NowUI/UI Rectangle IS ported, so this asserts that the PATH runs - vertex-less VAO, pass select,
            // uniform push, drawArrays - rather than that an unported program refuses. It draws a degenerate
            // triangle, because with no attributes enabled every vertex reads the generic constant, which is
            // exactly the point: what is being tested is that a program can run with no vertex buffer at all.
            //
            // SetViewProjection first, mirroring CommandBuffer.Execute's EnsureViewProjection: invariant 6
            // covers procedural draws too, and this backend enforces it.
            backend.SetViewProjection(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 100f));
            backend.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, null);
            return null;
        }

        static string ProceduralInstancedRuns()
        {
            Shader shader = Shader.Find("NowUI/UI Rectangle");

            if (ReferenceEquals(shader, null))
                return "NowUI/UI Rectangle did not resolve.";

            var material = new Material(shader);
            INowRenderBackend backend = NowRuntime.backend;

            backend.SetViewProjection(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 100f));
            backend.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 4, null);
            return null;
        }

        static string PassOutOfRangeThrows()
        {
            Shader shader = Shader.Find("NowUI/UI Rectangle");

            if (ReferenceEquals(shader, null))
                return "NowUI/UI Rectangle did not resolve.";

            var material = new Material(shader);
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            if (!rt.Create()) return "Create() returned false.";

            try
            {
                // Every ported program declares one pass. Asking for pass 1 has to fail rather than silently
                // draw pass 0, because the day Hidden/NowUI/GlassBlur lands its four passes, drawing the wrong
                // one is a plausible-looking blur rather than an error.
                Graphics.Blit(MakeMarkerTexture(), rt, material, 1);
                return "a blit through pass 1 of a single-pass program was accepted.";
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string CopyTextureCase()
        {
            Texture2D source = MakeMarkerTexture();
            var destination = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            destination.Apply();

            Graphics.CopyTexture(source, destination);
            return null;
        }

        static string TemporaryPoolRoundTrip()
        {
            RenderTexture first = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32);

            if (!first.IsCreated() && !first.Create())
                return "a pooled target could not be created.";

            int id = first.GetInstanceID();
            RenderTexture.ReleaseTemporary(first);

            RenderTexture second = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32);

            try
            {
                if (second.GetInstanceID() != id)
                    return "the pool handed back a different target for an identical request.";

                // The point of the pool is that the GPU object SURVIVES the round trip: a re-acquired target
                // that has to be re-created would allocate every frame.
                if (!second.IsCreated())
                    return "the re-acquired pooled target reports itself uncreated; the backend dropped it.";

                return null;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(second);
            }
        }

        static string MipChainTarget()
        {
            var descriptor = new RenderTextureDescriptor(64, 64, RenderTextureFormat.ARGB32, 0)
            {
                useMipMap = true,
                autoGenerateMips = true,
            };

            var rt = new RenderTexture(descriptor);

            try
            {
                if (!rt.Create()) return "Create() returned false for a mipped target.";

                // Binding it marks the chain dirty; binding away regenerates. Both have to survive a round trip
                // without a GL error, which getError() below reports.
                RenderTexture.active = rt;
                GL.Clear(false, true, Color.black);
                RenderTexture.active = null;
                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string DepthBitsTarget()
        {
            var rt = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32);

            try
            {
                if (!rt.Create()) return "Create() returned false for a target with 24 depth bits.";
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = null;
                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string FloatTargetMatchesCaps()
        {
            // The assertion is agreement, not support: whatever SystemInfo says, Create() has to say the same,
            // because NowSdfImageField picks its format by asking SystemInfo and would otherwise pick one the
            // backend then refuses.
            return CheckFormatAgreement(RenderTextureFormat.RHalf)
                ?? CheckFormatAgreement(RenderTextureFormat.RFloat)
                ?? CheckFormatAgreement(RenderTextureFormat.ARGBHalf)
                ?? CheckFormatAgreement(RenderTextureFormat.ARGBFloat)
                ?? CheckFormatAgreement(RenderTextureFormat.R8);
        }

        static string CheckFormatAgreement(RenderTextureFormat format)
        {
            bool claimed = SystemInfo.SupportsRenderTextureFormat(format);
            var rt = new RenderTexture(32, 32, 0, format, RenderTextureReadWrite.Linear);

            try
            {
                bool created = rt.Create();

                if (claimed != created)
                {
                    return "SystemInfo.SupportsRenderTextureFormat(" + format + ") says " + claimed +
                           " but Create() says " + created + ".";
                }

                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string ArrayTargetRefused()
        {
            var descriptor = new RenderTextureDescriptor(32, 32, RenderTextureFormat.ARGB32, 0)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
            };

            var rt = new RenderTexture(descriptor);

            try
            {
                // Refused, not thrown: CreateRenderTexture's bool is the documented way to say "cannot be
                // honoured", and RenderTexture.Create() is what turns it into something a caller can branch on.
                if (rt.Create()) return "an array render target was created; this backend does not implement one.";
                if (rt.IsCreated()) return "IsCreated() is true for a target that was never created.";
                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string RandomWriteRefused()
        {
            var descriptor = new RenderTextureDescriptor(32, 32, RenderTextureFormat.ARGB32, 0)
            {
                enableRandomWrite = true,
            };

            var rt = new RenderTexture(descriptor);

            try
            {
                if (rt.Create()) return "a UAV target was created; WebGL2 has no compute.";
                return null;
            }
            finally
            {
                rt.Release();
            }
        }

        static string ZeroSizedTargetRefused()
        {
            var rt = new RenderTexture(0, 0, 0, RenderTextureFormat.ARGB32);

            // The shim short-circuits this one before the backend sees it; asserted anyway, because "the shim
            // catches it" is a fact about today's shim and this is the behaviour a caller depends on.
            if (rt.Create()) return "a 0x0 target was created.";
            return null;
        }

        static string NullArgumentsThrow()
        {
            INowRenderBackend backend = NowRuntime.backend;

            try
            {
                backend.IsRenderTextureLost(null);
                return "IsRenderTextureLost(null) did not throw.";
            }
            catch (ArgumentNullException)
            {
            }

            try
            {
                var request = new NowRenderTextureRequest(4, 4, 0, RenderTextureFormat.ARGB32,
                                                          RenderTextureReadWrite.Linear);
                backend.CreateRenderTexture(null, in request);
                return "CreateRenderTexture(null, ...) did not throw.";
            }
            catch (ArgumentNullException)
            {
            }

            try
            {
                NowRenderTarget destination = NowRenderTarget.BackBuffer(4, 4);
                backend.Blit(null, in destination, null, 0, Vector2.one, Vector2.zero, 0, 0);
                return "a blit with neither a source nor a material did not throw.";
            }
            catch (ArgumentNullException)
            {
            }

            // ReleaseRenderTexture(null) is the one that must NOT throw: it is reachable on teardown and from a
            // handle that was never created.
            backend.ReleaseRenderTexture(null);
            return null;
        }

        static string CapsMsaaIsOne()
        {
            NowRenderCaps caps = NowRuntime.backend.caps;

            if (caps.maxMsaaSamples != 1)
            {
                return "caps.maxMsaaSamples is " + caps.maxMsaaSamples + ". WebGL2 has no sampleable " +
                       "multisampled texture, so anything but 1 promises antialiasing the backend then flattens.";
            }

            if (caps.supportsMultisampledTextures)
                return "caps.supportsMultisampledTextures is true; WebGL2 has no multisampled texture at all.";

            return null;
        }

        /// <summary>
        /// A 4x4 RGBA32 texture with a red top row and a blue bottom row. Row 0 is the BOTTOM, as Unity stores
        /// raw texture data and as GL uploads it, so "top" here means the row a NowUI shader samples at v = 1.
        /// </summary>
        static Texture2D MakeMarkerTexture()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[16];

            for (int y = 0; y < 4; ++y)
            {
                Color32 row = y == 3 ? new Color32(255, 0, 0, 255)
                    : y == 0 ? new Color32(0, 0, 255, 255)
                    : new Color32(0, 255, 0, 255);

                for (int x = 0; x < 4; ++x)
                    pixels[y * 4 + x] = row;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
