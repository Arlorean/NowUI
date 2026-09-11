// Tests for the U10 backend unit: INowRenderBackend, NowRenderCaps, NowRenderTarget, NowRenderTextureRequest,
// NullRenderBackend and NowRecordingRenderBackend.
// Design: Docs/Standalone/StandaloneCoreDesign.md §4.1 (the contract, the capability struct and the eight
// invariants), §4.6 (the null backend's caps, counters, validation and opt-in draw ring; the recording backend),
// §1.2 (the allocation rule), §7.5 (the "Immediate path" and "Allocation" rows of the semantics suite).
//
// The allocation case uses GC.GetAllocatedBytesForCurrentThread, which is exact for the current thread and is the
// instrument design §7.5 names for the steady-state frame gate.
using System;
using System.Collections.Generic;
using System.Globalization;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class U10BackendTests
    {
        // ------------------------------------------------------------------------------------------------ fixtures

        // The Shader constructor is internal by design (§3.5): standalone shaders come from the host's resource
        // provider, and the tests assembly is a friend so it can mint one.
        private static Shader MakeShader(string name)
        {
            return new Shader(name, new NowShaderInfo(name, 1));
        }

        private static Material MakeMaterial(string shaderName)
        {
            return new Material(MakeShader(shaderName));
        }

        private static Mesh MakeMesh(int subMeshCount)
        {
            Mesh mesh = new Mesh();
            mesh.subMeshCount = subMeshCount;
            return mesh;
        }

        // A capability report with nothing switched on, to prove the format queries really are answered from the
        // flags rather than hard-coded true.
        private static NowRenderCaps NoFormats()
        {
            return new NowRenderCaps(
                maxTextureSize: 2048,
                maxMsaaSamples: 1,
                supportsMultisampledTextures: false,
                supportsTextureArrays: false,
                supportsInstancing: false,
                supportsR8: false,
                supportsRHalf: false,
                supportsRFloat: false,
                supportsRGHalf: false,
                supportsRGFloat: false,
                supportsARGBHalf: false,
                supportsARGBFloat: false,
                supportsARGB32: false,
                supportsDepth: false,
                renderTargetsAreBottomUp: false,
                colorSpace: ColorSpace.Linear,
                deviceName: null,
                deviceType: GraphicsDeviceType.OpenGLES3,
                graphicsMemorySizeMb: 64);
        }

        // ---------------------------------------------------------------------------------------- NowRenderCaps

        [Test]
        public void Caps_ReportsEveryFieldItWasGiven()
        {
            NowRenderCaps caps = new NowRenderCaps(
                4096, 4, true, true, false,
                true, false, true, false, true, false, true, true, false,
                true, ColorSpace.Linear, "Test Device", GraphicsDeviceType.OpenGLES3, 512);

            Assert.That(caps.maxTextureSize, Is.EqualTo(4096));
            Assert.That(caps.maxMsaaSamples, Is.EqualTo(4));
            Assert.That(caps.supportsMultisampledTextures, Is.True);
            Assert.That(caps.supportsTextureArrays, Is.True);
            Assert.That(caps.supportsInstancing, Is.False);
            Assert.That(caps.supportsR8, Is.True);
            Assert.That(caps.supportsRHalf, Is.False);
            Assert.That(caps.supportsRFloat, Is.True);
            Assert.That(caps.supportsRGHalf, Is.False);
            Assert.That(caps.supportsRGFloat, Is.True);
            Assert.That(caps.supportsARGBHalf, Is.False);
            Assert.That(caps.supportsARGBFloat, Is.True);
            Assert.That(caps.supportsARGB32, Is.True);
            Assert.That(caps.supportsDepth, Is.False);
            Assert.That(caps.renderTargetsAreBottomUp, Is.True);
            Assert.That(caps.colorSpace, Is.EqualTo(ColorSpace.Linear));
            Assert.That(caps.deviceName, Is.EqualTo("Test Device"));
            Assert.That(caps.deviceType, Is.EqualTo(GraphicsDeviceType.OpenGLES3));
            Assert.That(caps.graphicsMemorySizeMb, Is.EqualTo(512));
        }

        // SystemInfo.graphicsDeviceName is a string under Unity and core code concatenates it, so the struct must
        // never hand back null.
        [Test]
        public void Caps_NullDeviceNameBecomesEmpty()
        {
            Assert.That(NoFormats().deviceName, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Caps_CreateAllFormats_AcceptsEveryRenderTextureFormat()
        {
            NowRenderCaps caps = NowRenderCaps.CreateAllFormats(
                16384, 1, false, true, true, true, ColorSpace.Gamma, "Null", GraphicsDeviceType.Null, 0);

            foreach (RenderTextureFormat format in (RenderTextureFormat[])Enum.GetValues(typeof(RenderTextureFormat)))
                Assert.That(caps.SupportsRenderTextureFormat(format), Is.True, format.ToString());
        }

        [Test]
        public void Caps_CreateAllFormats_AcceptsEveryTextureFormat()
        {
            NowRenderCaps caps = NowRenderCaps.CreateAllFormats(
                16384, 1, false, true, true, true, ColorSpace.Gamma, "Null", GraphicsDeviceType.Null, 0);

            foreach (TextureFormat format in (TextureFormat[])Enum.GetValues(typeof(TextureFormat)))
                Assert.That(caps.SupportsTextureFormat(format), Is.True, format.ToString());
        }

        [Test]
        public void Caps_NoFlags_RejectsEveryFormat()
        {
            NowRenderCaps caps = NoFormats();

            foreach (RenderTextureFormat format in (RenderTextureFormat[])Enum.GetValues(typeof(RenderTextureFormat)))
                Assert.That(caps.SupportsRenderTextureFormat(format), Is.False, format.ToString());

            foreach (TextureFormat format in (TextureFormat[])Enum.GetValues(typeof(TextureFormat)))
                Assert.That(caps.SupportsTextureFormat(format), Is.False, format.ToString());
        }

        // The classification rule §4.1 gives a backend: each format is answered by the capability class it belongs
        // to. These are the classes NowUI actually branches on - NowSdf on R8, NowGlassRenderer on the float and
        // half targets.
        [Test]
        public void Caps_FormatsAreAnsweredByTheirCapabilityClass()
        {
            NowRenderCaps r8Only = new NowRenderCaps(
                2048, 1, false, false, false,
                supportsR8: true, supportsRHalf: false, supportsRFloat: false, supportsRGHalf: false,
                supportsRGFloat: false, supportsARGBHalf: false, supportsARGBFloat: false, supportsARGB32: false,
                supportsDepth: false,
                renderTargetsAreBottomUp: true, colorSpace: ColorSpace.Gamma, deviceName: "R8",
                deviceType: GraphicsDeviceType.OpenGLES3, graphicsMemorySizeMb: 0);

            Assert.That(r8Only.SupportsRenderTextureFormat(RenderTextureFormat.R8), Is.True);
            Assert.That(r8Only.SupportsRenderTextureFormat(RenderTextureFormat.RG16), Is.True, "RG16 is 8 bits per channel");
            Assert.That(r8Only.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32), Is.False);
            Assert.That(r8Only.SupportsRenderTextureFormat(RenderTextureFormat.Depth), Is.False);
            Assert.That(r8Only.SupportsRenderTextureFormat(RenderTextureFormat.RHalf), Is.False);
            Assert.That(r8Only.SupportsTextureFormat(TextureFormat.R8), Is.True);
            Assert.That(r8Only.SupportsTextureFormat(TextureFormat.Alpha8), Is.True);
            Assert.That(r8Only.SupportsTextureFormat(TextureFormat.RGBA32), Is.False);

            NowRenderCaps colorOnly = new NowRenderCaps(
                2048, 1, false, false, false,
                supportsR8: false, supportsRHalf: false, supportsRFloat: false, supportsRGHalf: false,
                supportsRGFloat: false, supportsARGBHalf: false, supportsARGBFloat: false, supportsARGB32: true,
                supportsDepth: true,
                renderTargetsAreBottomUp: true, colorSpace: ColorSpace.Gamma, deviceName: "Color",
                deviceType: GraphicsDeviceType.OpenGLES3, graphicsMemorySizeMb: 0);

            Assert.That(colorOnly.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32), Is.True);
            Assert.That(colorOnly.SupportsRenderTextureFormat(RenderTextureFormat.Default), Is.True);
            Assert.That(colorOnly.SupportsRenderTextureFormat(RenderTextureFormat.Depth), Is.True);
            Assert.That(colorOnly.SupportsRenderTextureFormat(RenderTextureFormat.Shadowmap), Is.True);
            Assert.That(colorOnly.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf), Is.False);
            Assert.That(colorOnly.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat), Is.False);
            Assert.That(colorOnly.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat), Is.False);
            Assert.That(colorOnly.SupportsTextureFormat(TextureFormat.RGBAHalf), Is.False);
            Assert.That(colorOnly.SupportsTextureFormat(TextureFormat.DXT5), Is.True, "compressed colour is a colour format");
        }

        // ------------------------------------------------------------------------------------- NowRenderTarget

        [Test]
        public void RenderTarget_BackBufferHasNoTexture()
        {
            NowRenderTarget target = NowRenderTarget.BackBuffer(800, 600);

            Assert.That(target.isBackBuffer, Is.True);
            Assert.That(ReferenceEquals(target.texture, null), Is.True);
            Assert.That(target.width, Is.EqualTo(800));
            Assert.That(target.height, Is.EqualTo(600));
            Assert.That(target.depthSlice, Is.EqualTo(NowRenderTarget.AllDepthSlices));
            Assert.That(NowRenderTarget.AllDepthSlices, Is.EqualTo(-1));
        }

        [Test]
        public void RenderTarget_TextureTargetIsNotTheBackBuffer()
        {
            RenderTexture rt = new RenderTexture(64, 32, 0);
            NowRenderTarget target = new NowRenderTarget(rt, 0, CubemapFace.Unknown, 0, 64, 32);

            Assert.That(target.isBackBuffer, Is.False);
            Assert.That(target.texture, Is.SameAs(rt));
            Assert.That(target.mipLevel, Is.EqualTo(0));
            Assert.That(target.face, Is.EqualTo(CubemapFace.Unknown));
        }

        // The fake-null trap of VT §13: a destroyed RenderTexture compares equal to null through the overloaded
        // operator, and it must NOT be mistaken for the back buffer. isBackBuffer uses reference identity for
        // exactly this reason.
        [Test]
        public void RenderTarget_DestroyedTextureIsStillNotTheBackBuffer()
        {
            RenderTexture rt = new RenderTexture(8, 8, 0);
            NowRenderTarget target = new NowRenderTarget(rt, 0, CubemapFace.Unknown, 0, 8, 8);

            UnityEngine.Object.DestroyImmediate(rt);

            Assert.That(rt == null, Is.True, "the fake-null operator reports a destroyed object as null");
            Assert.That(target.isBackBuffer, Is.False);
        }

        // -------------------------------------------------------------------------------- NowRenderTextureRequest

        [Test]
        public void RenderTextureRequest_CarriesEveryLayoutHint()
        {
            NowRenderTextureRequest request = new NowRenderTextureRequest(
                256, 128, 24, 4, 3, 8,
                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear,
                TextureDimension.Tex2DArray, VRTextureUsage.TwoEyes,
                bindMS: true, useMipMap: true, autoGenerateMips: true, enableRandomWrite: true);

            Assert.That(request.width, Is.EqualTo(256));
            Assert.That(request.height, Is.EqualTo(128));
            Assert.That(request.depthBits, Is.EqualTo(24));
            Assert.That(request.volumeDepth, Is.EqualTo(4));
            Assert.That(request.mipCount, Is.EqualTo(3));
            Assert.That(request.msaaSamples, Is.EqualTo(8));
            Assert.That(request.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            Assert.That(request.readWrite, Is.EqualTo(RenderTextureReadWrite.Linear));
            Assert.That(request.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(request.vrUsage, Is.EqualTo(VRTextureUsage.TwoEyes));
            Assert.That(request.bindMS, Is.True);
            Assert.That(request.useMipMap, Is.True);
            Assert.That(request.autoGenerateMips, Is.True);
            Assert.That(request.enableRandomWrite, Is.True);
        }

        [Test]
        public void RenderTextureRequest_PlainTargetConstructorFillsTheDefaults()
        {
            NowRenderTextureRequest request = new NowRenderTextureRequest(
                64, 64, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            Assert.That(request.volumeDepth, Is.EqualTo(1));
            Assert.That(request.mipCount, Is.EqualTo(1));
            Assert.That(request.msaaSamples, Is.EqualTo(1));
            Assert.That(request.dimension, Is.EqualTo(TextureDimension.Tex2D));
            Assert.That(request.vrUsage, Is.EqualTo(VRTextureUsage.None));
            Assert.That(request.bindMS, Is.False);
            Assert.That(request.useMipMap, Is.False);
            Assert.That(request.autoGenerateMips, Is.False);
            Assert.That(request.enableRandomWrite, Is.False);
        }

        // --------------------------------------------------------------------------- NullRenderBackend: the caps

        [Test]
        public void NullBackend_ReportsTheDesignedCaps()
        {
            NowRenderCaps caps = new NullRenderBackend().caps;

            Assert.That(caps.maxTextureSize, Is.EqualTo(16384));
            Assert.That(caps.maxMsaaSamples, Is.EqualTo(1));
            Assert.That(caps.supportsMultisampledTextures, Is.False);
            Assert.That(caps.supportsTextureArrays, Is.True);
            Assert.That(caps.renderTargetsAreBottomUp, Is.True);
            Assert.That(caps.deviceName, Is.EqualTo("Null"));
            Assert.That(caps.deviceType, Is.EqualTo(GraphicsDeviceType.Null));
            Assert.That(caps.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat), Is.True);
            Assert.That(caps.SupportsTextureFormat(TextureFormat.RGBA32), Is.True);
        }

        // §4.1 requires SystemInfo and QualitySettings to agree with the backend about the colour space, so the null
        // backend reads the runtime's value on every access rather than latching it at construction.
        [Test]
        public void NullBackend_ColorSpaceTracksTheRuntime()
        {
            ColorSpace original = NowRuntime.colorSpace;
            try
            {
                NullRenderBackend backend = new NullRenderBackend();

                NowRuntime.colorSpace = ColorSpace.Gamma;
                Assert.That(backend.caps.colorSpace, Is.EqualTo(ColorSpace.Gamma));

                NowRuntime.colorSpace = ColorSpace.Linear;
                Assert.That(backend.caps.colorSpace, Is.EqualTo(ColorSpace.Linear));
            }
            finally
            {
                NowRuntime.colorSpace = original;
            }
        }

        // ------------------------------------------------------------------- NullRenderBackend: a synthetic frame

        [Test]
        public void NullBackend_AcceptsASyntheticFrame()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Mesh mesh = MakeMesh(2);
            Material material = MakeMaterial("NowUI/UI Rectangle");
            Texture2D texture = new Texture2D(2, 2);
            RenderTexture rt = new RenderTexture(64, 64, 0);
            NowRenderTarget target = new NowRenderTarget(rt, 0, CubemapFace.Unknown, 0, 64, 64);

            backend.BeginFrame(7);
            backend.CreateRenderTexture(rt, new NowRenderTextureRequest(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default));
            backend.UploadTexture2D(texture, new byte[16], new RectInt(0, 0, 2, 2), false);
            backend.UpdateSampler(texture);
            backend.SetRenderTarget(in target);
            backend.SetViewport(new Rect(0f, 0f, 64f, 64f));
            backend.ClearRenderTarget(true, true, Color.black, 1f);
            backend.SetViewProjection(Matrix4x4.identity, Matrix4x4.identity);
            backend.DrawMesh(mesh, 1, Matrix4x4.identity, material, 0, null);
            backend.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 6, 1, null);
            backend.Blit(texture, in target, null, -1, Vector2.one, Vector2.zero, 0, 0);
            backend.CopyTexture(texture, rt);
            backend.EndFrame();

            Assert.That(backend.frameBegins, Is.EqualTo(1));
            Assert.That(backend.frameEnds, Is.EqualTo(1));
            Assert.That(backend.lastFrameCount, Is.EqualTo(7));
            Assert.That(backend.renderTextureCreates, Is.EqualTo(1));
            Assert.That(backend.textureUploads, Is.EqualTo(1));
            Assert.That(backend.lastUploadByteCount, Is.EqualTo(16));
            Assert.That(backend.lastDirtyRect.width, Is.EqualTo(2));
            Assert.That(backend.lastDirtyRect.height, Is.EqualTo(2));
            Assert.That(backend.samplerUpdates, Is.EqualTo(1));
            Assert.That(backend.renderTargetSets, Is.EqualTo(1));
            Assert.That(backend.viewportSets, Is.EqualTo(1));
            Assert.That(backend.clears, Is.EqualTo(1));
            Assert.That(backend.lastClearDepth, Is.EqualTo(1f));
            Assert.That(backend.viewProjectionSets, Is.EqualTo(1));
            Assert.That(backend.meshDraws, Is.EqualTo(1));
            Assert.That(backend.proceduralDraws, Is.EqualTo(1));
            Assert.That(backend.blits, Is.EqualTo(1));
            Assert.That(backend.textureCopies, Is.EqualTo(1));
            Assert.That(backend.lastViewport.width, Is.EqualTo(64f));
        }

        // Invariant 7: a blit leaves the destination bound, so the next draw goes where Unity would have put it.
        [Test]
        public void NullBackend_BlitLeavesTheDestinationBound()
        {
            NullRenderBackend backend = new NullRenderBackend();
            RenderTexture source = new RenderTexture(16, 16, 0);
            RenderTexture destination = new RenderTexture(32, 32, 0);
            NowRenderTarget backBuffer = NowRenderTarget.BackBuffer(800, 600);
            NowRenderTarget target = new NowRenderTarget(destination, 0, CubemapFace.Unknown, 0, 32, 32);

            backend.SetRenderTarget(in backBuffer);
            Assert.That(backend.lastRenderTarget.isBackBuffer, Is.True);

            backend.Blit(source, in target, null, -1, Vector2.one, Vector2.zero, 0, 0);

            Assert.That(backend.lastRenderTarget.texture, Is.SameAs(destination));
            // The implicit bind is not a SetRenderTarget call, so the counter the "target then viewport" invariant is
            // asserted against does not move.
            Assert.That(backend.renderTargetSets, Is.EqualTo(1));
        }

        // A render texture is "lost" until it is created and again once it is released, which is what
        // RenderTexture.IsCreated() ands with its own flag.
        [Test]
        public void NullBackend_TracksCreatedRenderTextures()
        {
            NullRenderBackend backend = new NullRenderBackend();
            RenderTexture rt = new RenderTexture(8, 8, 0);
            NowRenderTextureRequest request = new NowRenderTextureRequest(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            Assert.That(backend.IsRenderTextureLost(rt), Is.True, "never created");
            Assert.That(backend.CreateRenderTexture(rt, in request), Is.True);
            Assert.That(backend.IsRenderTextureLost(rt), Is.False);
            Assert.That(backend.liveRenderTextures, Is.EqualTo(1));

            // Idempotent: creating twice is not two GPU objects.
            Assert.That(backend.CreateRenderTexture(rt, in request), Is.True);
            Assert.That(backend.liveRenderTextures, Is.EqualTo(1));

            backend.ReleaseRenderTexture(rt);
            Assert.That(backend.IsRenderTextureLost(rt), Is.True);
            Assert.That(backend.liveRenderTextures, Is.EqualTo(0));

            // Releasing twice is silent, as every release on this contract is.
            backend.ReleaseRenderTexture(rt);
            Assert.That(backend.renderTextureReleases, Is.EqualTo(2));
        }

        [Test]
        public void NullBackend_ResolvesEveryShader()
        {
            NullRenderBackend backend = new NullRenderBackend();

            Assert.That(backend.ResolveShader(MakeShader("NowUI/UI Rectangle")), Is.True);
            Assert.That(backend.shaderResolves, Is.EqualTo(1));
        }

        [Test]
        public void NullBackend_ResetClearsCountersAndCreatedTargets()
        {
            NullRenderBackend backend = new NullRenderBackend();
            RenderTexture rt = new RenderTexture(8, 8, 0);
            Mesh mesh = MakeMesh(1);
            Material material = MakeMaterial("s");

            backend.recordDraws = true;
            backend.BeginFrame(1);
            backend.CreateRenderTexture(rt, new NowRenderTextureRequest(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default));
            backend.DrawMesh(mesh, 0, Matrix4x4.identity, material, 0, null);
            backend.EndFrame();

            backend.Reset();

            Assert.That(backend.frameBegins, Is.Zero);
            Assert.That(backend.frameEnds, Is.Zero);
            Assert.That(backend.lastFrameCount, Is.Zero);
            Assert.That(backend.meshDraws, Is.Zero);
            Assert.That(backend.renderTextureCreates, Is.Zero);
            Assert.That(backend.liveRenderTextures, Is.Zero);
            Assert.That(backend.recordedDrawCount, Is.Zero);
            Assert.That(backend.lastRenderTarget.isBackBuffer, Is.True, "a cleared target reads as the back buffer");
            // A reset is the standalone domain reload: every GPU object is gone, so the target reports itself lost.
            Assert.That(backend.IsRenderTextureLost(rt), Is.True);
            // The ring's storage and its enabled flag survive, so a fixture can reset between cases cheaply.
            Assert.That(backend.recordDraws, Is.True);
        }

        // ------------------------------------------------------------------ NullRenderBackend: argument validation

        [Test]
        public void NullBackend_RejectsNullResourceArguments()
        {
            NullRenderBackend backend = new NullRenderBackend();
            NowRenderTextureRequest request = new NowRenderTextureRequest(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Texture2D texture = new Texture2D(1, 1);

            Assert.Throws<ArgumentNullException>(() => backend.UploadTexture2D(null, new byte[4], new RectInt(0, 0, 1, 1), false));
            Assert.Throws<ArgumentNullException>(() => backend.UpdateSampler(null));
            Assert.Throws<ArgumentNullException>(() => backend.ReleaseTexture(null));
            Assert.Throws<ArgumentNullException>(() => backend.CreateRenderTexture(null, in request));
            Assert.Throws<ArgumentNullException>(() => backend.IsRenderTextureLost(null));
            Assert.Throws<ArgumentNullException>(() => backend.ReleaseRenderTexture(null));
            Assert.Throws<ArgumentNullException>(() => backend.ReleaseMesh(null));
            Assert.Throws<ArgumentNullException>(() => backend.ReleaseMaterial(null));
            Assert.Throws<ArgumentNullException>(() => backend.ResolveShader(null));
            Assert.Throws<ArgumentNullException>(() => backend.CopyTexture(null, texture));
            Assert.Throws<ArgumentNullException>(() => backend.CopyTexture(texture, null));
        }

        [Test]
        public void NullBackend_RejectsNullDrawArguments()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Mesh mesh = MakeMesh(1);
            Material material = MakeMaterial("s");

            Assert.Throws<ArgumentNullException>(() => backend.DrawMesh(null, 0, Matrix4x4.identity, material, 0, null));
            Assert.Throws<ArgumentNullException>(() => backend.DrawMesh(mesh, 0, Matrix4x4.identity, null, 0, null));
            Assert.Throws<ArgumentNullException>(() => backend.DrawProcedural(Matrix4x4.identity, null, 0, MeshTopology.Triangles, 3, 1, null));

            Assert.That(backend.meshDraws, Is.Zero, "a rejected call is not a draw");
            Assert.That(backend.proceduralDraws, Is.Zero);
        }

        [Test]
        public void NullBackend_RejectsOutOfRangeSubMesh()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Mesh mesh = MakeMesh(2);
            Material material = MakeMaterial("s");

            Assert.Throws<ArgumentOutOfRangeException>(() => backend.DrawMesh(mesh, -1, Matrix4x4.identity, material, 0, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => backend.DrawMesh(mesh, 2, Matrix4x4.identity, material, 0, null));

            // The valid range really is [0, subMeshCount).
            Assert.DoesNotThrow(() => backend.DrawMesh(mesh, 0, Matrix4x4.identity, material, 0, null));
            Assert.DoesNotThrow(() => backend.DrawMesh(mesh, 1, Matrix4x4.identity, material, 0, null));
        }

        [Test]
        public void NullBackend_RejectsNegativeProceduralCounts()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Material material = MakeMaterial("s");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => backend.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, -1, 1, null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => backend.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, -1, null));
        }

        // A material-only blit (no source) is Unity's idiom for filling a target from uniforms alone, so it is
        // accepted; a blit with neither a source nor a material has nothing to read and is not.
        [Test]
        public void NullBackend_BlitNeedsASourceOrAMaterial()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Material material = MakeMaterial("s");
            NowRenderTarget target = NowRenderTarget.BackBuffer(64, 64);

            Assert.Throws<ArgumentNullException>(
                () => backend.Blit(null, in target, null, -1, Vector2.one, Vector2.zero, 0, 0));

            Assert.DoesNotThrow(
                () => backend.Blit(null, in target, material, 0, Vector2.one, Vector2.zero, 0, 0));
            Assert.That(backend.blits, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------------ NullRenderBackend: the draw ring

        [Test]
        public void DrawRing_IsOffByDefault()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Mesh mesh = MakeMesh(1);
            Material material = MakeMaterial("s");

            Assert.That(backend.recordDraws, Is.False);

            backend.DrawMesh(mesh, 0, Matrix4x4.identity, material, 0, null);

            Assert.That(backend.meshDraws, Is.EqualTo(1), "the counter still moves");
            Assert.That(backend.recordedDrawCount, Is.Zero, "but nothing is recorded");
            Assert.Throws<ArgumentOutOfRangeException>(() => backend.GetDrawRecord(0));
        }

        [Test]
        public void DrawRing_RecordsMeshAndProceduralDraws()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Mesh mesh = MakeMesh(3);
            Material material = MakeMaterial("s");
            RenderTexture rt = new RenderTexture(16, 16, 0);
            NowRenderTarget target = new NowRenderTarget(rt, 0, CubemapFace.Unknown, 0, 16, 16);

            backend.recordDraws = true;
            backend.SetRenderTarget(in target);

            Matrix4x4 model = Matrix4x4.identity;
            model[0, 3] = 5f;
            backend.DrawMesh(mesh, 2, model, material, 1, null);
            backend.DrawProcedural(Matrix4x4.identity, material, 4, MeshTopology.Triangles, 6, 1, null);

            Assert.That(backend.recordedDrawCount, Is.EqualTo(2));

            NowDrawRecord first = backend.GetDrawRecord(0);
            Assert.That(first.meshId, Is.EqualTo(mesh.GetInstanceID()));
            Assert.That(first.subMesh, Is.EqualTo(2));
            Assert.That(first.materialId, Is.EqualTo(material.GetInstanceID()));
            Assert.That(first.pass, Is.EqualTo(1));
            Assert.That(first.targetId, Is.EqualTo(rt.GetInstanceID()));
            Assert.That(first.model[0, 3], Is.EqualTo(5f));

            // A procedural draw has no mesh, so meshId 0 marks it and the vertex count takes the sub-mesh slot.
            NowDrawRecord second = backend.GetDrawRecord(1);
            Assert.That(second.meshId, Is.Zero);
            Assert.That(second.subMesh, Is.EqualTo(6));
            Assert.That(second.pass, Is.EqualTo(4));
        }

        [Test]
        public void DrawRing_RecordsTheBackBufferAsTargetZero()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Mesh mesh = MakeMesh(1);
            Material material = MakeMaterial("s");
            NowRenderTarget backBuffer = NowRenderTarget.BackBuffer(800, 600);

            backend.recordDraws = true;
            backend.SetRenderTarget(in backBuffer);
            backend.DrawMesh(mesh, 0, Matrix4x4.identity, material, 0, null);

            Assert.That(backend.GetDrawRecord(0).targetId, Is.Zero);
        }

        [Test]
        public void DrawRing_WrapsAtCapacityAndKeepsTheNewest()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Mesh mesh = MakeMesh(1);
            Material material = MakeMaterial("s");

            backend.recordDraws = true;

            int total = NullRenderBackend.DrawRingCapacity + 10;
            for (int i = 0; i < total; i++)
            {
                Matrix4x4 model = Matrix4x4.identity;
                model[0, 3] = i;
                backend.DrawMesh(mesh, 0, model, material, i, null);
            }

            Assert.That(backend.recordedDrawCount, Is.EqualTo(NullRenderBackend.DrawRingCapacity));
            Assert.That(backend.meshDraws, Is.EqualTo(total));

            // Index 0 is the oldest entry the ring still holds - draw number 10 - and the last is the newest.
            Assert.That(backend.GetDrawRecord(0).pass, Is.EqualTo(10));
            Assert.That(backend.GetDrawRecord(0).model[0, 3], Is.EqualTo(10f));
            Assert.That(backend.GetDrawRecord(NullRenderBackend.DrawRingCapacity - 1).pass, Is.EqualTo(total - 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => backend.GetDrawRecord(NullRenderBackend.DrawRingCapacity));
        }

        // -------------------------------------------------------------------------------- the allocation-zero gate

        // Design §1.2 and §7.5: with the ring off, a steady-state frame through the null backend must allocate
        // nothing. This is the earliest warning we get about M2 browser performance and it costs one test.
        [Test]
        public void NullBackend_SyntheticFrameAllocatesNothing()
        {
            NullRenderBackend backend = new NullRenderBackend();
            Mesh mesh = MakeMesh(1);
            Material material = MakeMaterial("s");
            Texture2D texture = new Texture2D(2, 2);
            RenderTexture rt = new RenderTexture(64, 64, 0);
            byte[] pixels = new byte[16];

            // Two warm-up frames: the first JITs every method on the path, and the second lets the created-target
            // set and every counter reach the state the measured frame will find them in.
            RunFrame(backend, mesh, material, texture, rt, pixels);
            RunFrame(backend, mesh, material, texture, rt, pixels);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunFrame(backend, mesh, material, texture, rt, pixels);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(backend.recordDraws, Is.False);
            Assert.That(allocated, Is.Zero, "a steady-state frame through the null backend must allocate nothing");
        }

        // Deliberately assertion-free: an NUnit constraint would allocate inside the measured region.
        private static void RunFrame(NullRenderBackend backend, Mesh mesh, Material material, Texture2D texture,
                                     RenderTexture rt, byte[] pixels)
        {
            NowRenderTarget target = new NowRenderTarget(rt, 0, CubemapFace.Unknown, 0, 64, 64);
            NowRenderTextureRequest request = new NowRenderTextureRequest(
                64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Rect viewport = new Rect(0f, 0f, 64f, 64f);
            Matrix4x4 identity = Matrix4x4.identity;

            backend.BeginFrame(1);
            backend.CreateRenderTexture(rt, in request);
            backend.IsRenderTextureLost(rt);
            backend.UploadTexture2D(texture, pixels, new RectInt(0, 0, 2, 2), false);
            backend.UpdateSampler(texture);
            backend.SetRenderTarget(in target);
            backend.SetViewport(in viewport);
            backend.ClearRenderTarget(true, true, Color.black, 1f);
            backend.SetViewProjection(in identity, in identity);

            for (int i = 0; i < 64; i++)
            {
                backend.DrawMesh(mesh, 0, in identity, material, 0, null);
                backend.DrawProcedural(in identity, material, 0, MeshTopology.Triangles, 6, 1, null);
            }

            backend.Blit(texture, in target, null, -1, Vector2.one, Vector2.zero, 0, 0);
            backend.CopyTexture(texture, rt);
            backend.EndFrame();
        }

        // -------------------------------------------------------------------------- NowRecordingRenderBackend

        [Test]
        public void Recording_LogsOpsInCallOrder()
        {
            NowRecordingRenderBackend backend = new NowRecordingRenderBackend();
            Mesh mesh = MakeMesh(1);
            Material material = MakeMaterial("NowUI/UI Rectangle");
            RenderTexture rt = new RenderTexture(64, 64, 0);
            NowRenderTarget target = new NowRenderTarget(rt, 0, CubemapFace.Unknown, 0, 64, 64);

            backend.BeginFrame(3);
            backend.SetRenderTarget(in target);
            backend.SetViewport(new Rect(0f, 0f, 64f, 64f));
            backend.ClearRenderTarget(true, true, Color.black, 1f);
            backend.SetViewProjection(Matrix4x4.identity, Matrix4x4.identity);
            backend.DrawMesh(mesh, 0, Matrix4x4.identity, material, 0, null);
            backend.EndFrame();

            IReadOnlyList<string> ops = backend.ops;
            Assert.That(ops.Count, Is.EqualTo(7));
            Assert.That(ops[0], Is.EqualTo("BeginFrame(3)"));
            Assert.That(ops[1], Does.StartWith("SetRenderTarget(rt0"));
            Assert.That(ops[2], Is.EqualTo("SetViewport((0, 0, 64, 64))"));
            Assert.That(ops[3], Does.StartWith("ClearRenderTarget(depth=true, color=true, rgba("));
            Assert.That(ops[4], Does.StartWith("SetViewProjection(view=["));
            Assert.That(ops[5], Does.StartWith("DrawMesh(mesh0, subMesh=0, material=mat0, pass=0, properties=none"));
            Assert.That(ops[6], Is.EqualTo("EndFrame()"));

            // The matrices are written in row-major reading order, which is how the design and NowUI's projection
            // code write them, not in Matrix4x4's column-major storage order.
            Assert.That(ops[4], Is.EqualTo(
                "SetViewProjection(view=[1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1], projection=[1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1])"));

            // ToLog is the form the M2 harness diffs.
            Assert.That(backend.ToLog(), Is.EqualTo(string.Join("\n", ops)));
        }

        // The log has to be stable run to run for the M2 diff to mean anything, so objects appear as first-seen
        // aliases rather than as process-global instance ids.
        [Test]
        public void Recording_AliasesAreAssignedInFirstSeenOrderPerKind()
        {
            NowRecordingRenderBackend backend = new NowRecordingRenderBackend();
            Mesh firstMesh = MakeMesh(1);
            Mesh secondMesh = MakeMesh(1);
            Material material = MakeMaterial("s");

            backend.DrawMesh(secondMesh, 0, Matrix4x4.identity, material, 0, null);
            backend.DrawMesh(firstMesh, 0, Matrix4x4.identity, material, 0, null);
            backend.DrawMesh(secondMesh, 0, Matrix4x4.identity, material, 0, null);

            Assert.That(backend.ops[0], Does.StartWith("DrawMesh(mesh0,"));
            Assert.That(backend.ops[1], Does.StartWith("DrawMesh(mesh1,"));
            Assert.That(backend.ops[2], Does.StartWith("DrawMesh(mesh0,"), "the same object keeps its alias");

            // Meshes and materials number independently.
            Assert.That(backend.ops[0], Does.Contain("material=mat0"));

            backend.Clear();
            Assert.That(backend.ops.Count, Is.Zero);

            backend.DrawMesh(firstMesh, 0, Matrix4x4.identity, material, 0, null);
            Assert.That(backend.ops[0], Does.StartWith("DrawMesh(mesh0,"), "Clear restarts the numbering");
        }

        [Test]
        public void Recording_ForwardsToTheWrappedBackend()
        {
            NullRenderBackend inner = new NullRenderBackend();
            NowRecordingRenderBackend backend = new NowRecordingRenderBackend(inner);
            Mesh mesh = MakeMesh(1);
            Material material = MakeMaterial("s");

            Assert.That(backend.inner, Is.SameAs(inner));
            Assert.That(backend.caps.deviceName, Is.EqualTo("Null"));

            backend.BeginFrame(1);
            backend.DrawMesh(mesh, 0, Matrix4x4.identity, material, 0, null);
            backend.EndFrame();

            Assert.That(inner.frameBegins, Is.EqualTo(1));
            Assert.That(inner.meshDraws, Is.EqualTo(1));
            Assert.That(inner.frameEnds, Is.EqualTo(1));
        }

        // Validation runs before the log entry is written, so a rejected call leaves no trace: an op log is a record
        // of what the GPU was asked to do, not of what the caller attempted.
        [Test]
        public void Recording_DoesNotLogRejectedCalls()
        {
            NowRecordingRenderBackend backend = new NowRecordingRenderBackend();
            Mesh mesh = MakeMesh(1);
            Material material = MakeMaterial("s");

            Assert.Throws<ArgumentOutOfRangeException>(() => backend.DrawMesh(mesh, 5, Matrix4x4.identity, material, 0, null));
            Assert.Throws<ArgumentNullException>(() => backend.ReleaseMesh(null));

            Assert.That(backend.ops.Count, Is.Zero);
        }

        // IsRenderTextureLost is a query that core code makes an arbitrary number of times through
        // RenderTexture.IsCreated(); logging it would make two logs of the same frame differ for no reason.
        [Test]
        public void Recording_DoesNotLogQueries()
        {
            NowRecordingRenderBackend backend = new NowRecordingRenderBackend();
            RenderTexture rt = new RenderTexture(8, 8, 0);

            NowRenderCaps ignored = backend.caps;
            backend.IsRenderTextureLost(rt);
            backend.IsRenderTextureLost(rt);

            Assert.That(ignored.deviceName, Is.EqualTo("Null"));
            Assert.That(backend.ops.Count, Is.Zero);

            // A create is not a query, so it is logged - with its result.
            backend.CreateRenderTexture(rt, new NowRenderTextureRequest(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default));
            Assert.That(backend.ops.Count, Is.EqualTo(1));
            Assert.That(backend.ops[0], Does.StartWith("CreateRenderTexture(rt0, 8x8, depth=0, format=ARGB32"));
            Assert.That(backend.ops[0], Does.EndWith(") -> true"));
        }

        [Test]
        public void Recording_LogsTheBackBufferByName()
        {
            NowRecordingRenderBackend backend = new NowRecordingRenderBackend();
            NowRenderTarget backBuffer = NowRenderTarget.BackBuffer(800, 600);

            backend.SetRenderTarget(in backBuffer);

            Assert.That(backend.ops[0], Is.EqualTo("SetRenderTarget(backbuffer, mip=0, face=Unknown, slice=-1, 800x600)"));
        }

        [Test]
        public void Recording_LogsBlitOperands()
        {
            NowRecordingRenderBackend backend = new NowRecordingRenderBackend();
            Texture2D source = new Texture2D(2, 2);
            RenderTexture destination = new RenderTexture(4, 4, 0);
            NowRenderTarget target = new NowRenderTarget(destination, 0, CubemapFace.Unknown, 0, 4, 4);
            Material material = MakeMaterial("s");

            backend.Blit(source, in target, material, 2, new Vector2(1f, -1f), new Vector2(0f, 1f), 0, 0);

            Assert.That(backend.ops[0], Does.StartWith("Blit(source=tex0, destination=rt0"));
            Assert.That(backend.ops[0], Does.Contain("material=mat0, pass=2"));
            Assert.That(backend.ops[0], Does.Contain("scale=(1, -1)"));
            Assert.That(backend.ops[0], Does.Contain("offset=(0, 1)"));

            // A material-only blit says so rather than printing a null alias.
            backend.Blit(null, in target, material, 0, Vector2.one, Vector2.zero, 0, 0);
            Assert.That(backend.ops[1], Does.StartWith("Blit(source=none,"));
        }

        // The log formats with the invariant culture, so a machine whose decimal separator is a comma produces the
        // same bytes as one whose separator is a point. Asserting it directly is cheaper than discovering it in CI.
        [Test]
        public void Recording_FormatsWithTheInvariantCulture()
        {
            NowRecordingRenderBackend backend = new NowRecordingRenderBackend();

            backend.SetViewport(new Rect(0.5f, 0f, 64f, 32f));

            Assert.That(backend.ops[0], Does.Contain("0.5"));
            Assert.That(backend.ops[0], Does.Not.Contain("0,5"));
            Assert.That(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator, Is.EqualTo("."));
        }
    }
}
