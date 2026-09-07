// Tests for the U7 texture unit: UnityEngine.Texture, Texture2D, RenderTexture, RenderTextureDescriptor, Sprite,
// ImageConversion and the NowUI.Engine helper NowTemporaryRenderTexturePool.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 (member lists, the CPU-store and dirty-rect contract, the
// temporary-pool key and 8-frame trim), §1.2 (the CPU copy survives a seal), §4.1 (backend contract), §7.5 (the
// "Material / Mesh / Texture2D / NativeArray" row - raw-data aliasing, updateCount, bottom-up sub-rect placement -
// and the "Immediate path" row's pool keying and trim cases).
//
// Every test installs a fresh NullRenderBackend and empties the pool first, because the shim's texture state is
// process-wide by design (one pool, one memory counter, one set of lazily created constants) and NUnit gives no
// per-test process.
using System;
using NowUI.Engine;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class TextureTests
    {
        private NullRenderBackend m_Backend;
        private TestHost m_Host;

        [SetUp]
        public void SetUp()
        {
            NowTemporaryRenderTexturePool.Reset();

            m_Backend = new NullRenderBackend();
            m_Host = new TestHost();
            NowRuntime.Initialize(m_Host, m_Backend);
            NowRuntime.colorSpace = ColorSpace.Gamma;
            NowRuntime.releaseCpuCopiesOnSeal = false;
        }

        [TearDown]
        public void TearDown()
        {
            NowTemporaryRenderTexturePool.Reset();
            NowRuntime.releaseCpuCopiesOnSeal = false;
            NowRuntime.colorSpace = ColorSpace.Gamma;
        }

        // ------------------------------------------------------------------------------- Texture: sampler defaults

        [Test]
        public void Texture_DefaultsMatchUnity()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);

            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(texture.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(texture.wrapModeV, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(texture.wrapModeW, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(texture.anisoLevel, Is.EqualTo(1));
            Assert.That(texture.dimension, Is.EqualTo(TextureDimension.Tex2D));
            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(texture.updateCount, Is.EqualTo(0u));
            Assert.That(texture.isReadable, Is.True);
            Assert.That(texture.name, Is.EqualTo(""));
        }

        [Test]
        public void Texture_WrapModeSetterWritesAllThreeAxes()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            texture.wrapMode = TextureWrapMode.Clamp;

            Assert.That(texture.wrapModeU, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(texture.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(texture.wrapModeW, Is.EqualTo(TextureWrapMode.Clamp));

            texture.wrapModeU = TextureWrapMode.Mirror;

            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Mirror), "the getter reports the U axis");
            Assert.That(texture.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp), "V is untouched by the per-axis setter");
        }

        [Test]
        public void Texture_SamplerChangeNotifiesBackendOnlyOnChange()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            int before = m_Backend.samplerUpdates;

            texture.filterMode = FilterMode.Point;
            Assert.That(m_Backend.samplerUpdates, Is.EqualTo(before + 1));

            // The pooled-target case: NowSdfImageField assigns the same filter mode on every acquire, and a redundant
            // backend call per bake per frame is exactly what the change guard exists to prevent.
            texture.filterMode = FilterMode.Point;
            Assert.That(m_Backend.samplerUpdates, Is.EqualTo(before + 1));

            texture.wrapMode = TextureWrapMode.Clamp;
            Assert.That(m_Backend.samplerUpdates, Is.EqualTo(before + 2));

            texture.wrapMode = TextureWrapMode.Clamp;
            Assert.That(m_Backend.samplerUpdates, Is.EqualTo(before + 2));
        }

        [Test]
        public void Texture_AnisoLevelClampsInsteadOfThrowing()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            texture.anisoLevel = 64;
            Assert.That(texture.anisoLevel, Is.EqualTo(16));

            texture.anisoLevel = -5;
            Assert.That(texture.anisoLevel, Is.EqualTo(0));
        }

        [Test]
        public void Texture_CurrentTextureMemoryTracksTheCpuStore()
        {
            int before = Texture.currentTextureMemory;

            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Assert.That(Texture.currentTextureMemory - before, Is.EqualTo(64 * 64 * 4));

            UnityEngine.Object.DestroyImmediate(texture);
            Assert.That(Texture.currentTextureMemory, Is.EqualTo(before));
        }

        [Test]
        public void Texture_EntityIdIsStablePerInstance()
        {
            var a = new Texture2D(1, 1);
            var b = new Texture2D(1, 1);

            Assert.That(a.GetEntityId(), Is.EqualTo(a.GetInstanceID()));
            Assert.That(a.GetEntityId(), Is.Not.EqualTo(b.GetEntityId()));

            int id = a.GetEntityId();
            UnityEngine.Object.DestroyImmediate(a);
            Assert.That(a.GetEntityId(), Is.EqualTo(id), "NowSdf keys its cache on the id and must survive a destroy");
        }

        // ------------------------------------------------------------------------- Texture2D: shape and CPU store

        [Test]
        public void Texture2D_TwoArgumentConstructorIsRgba32WithAFullMipChain()
        {
            var texture = new Texture2D(8, 4);

            Assert.That(texture.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(texture.isLinear, Is.False);
            // 1 + floor(log2(8)) = 4 levels: 8x4, 4x2, 2x1, 1x1.
            Assert.That(texture.mipmapCount, Is.EqualTo(4));
            Assert.That(texture.pixels.Length, Is.EqualTo((8 * 4 + 4 * 2 + 2 * 1 + 1 * 1) * 4));
        }

        [Test]
        public void Texture2D_NoMipChainStoresExactlyOneLevel()
        {
            var texture = new Texture2D(16, 9, TextureFormat.RGBA32, false);

            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(texture.pixels.Length, Is.EqualTo(16 * 9 * 4));
        }

        [Test]
        public void Texture2D_LinearFlagRoundTrips()
        {
            Assert.That(new Texture2D(2, 2, TextureFormat.RGBA32, false, true).isLinear, Is.True);
            Assert.That(new Texture2D(2, 2, TextureFormat.RGBA32, false, false).isLinear, Is.False);
        }

        [Test]
        public void Texture2D_ExplicitMipCountIsClampedToTheChain()
        {
            Assert.That(new Texture2D(8, 8, TextureFormat.RGBA32, 2, false).mipmapCount, Is.EqualTo(2));
            Assert.That(new Texture2D(8, 8, TextureFormat.RGBA32, 99, false).mipmapCount, Is.EqualTo(4));
            Assert.That(new Texture2D(8, 8, TextureFormat.RGBA32, 0, false).mipmapCount, Is.EqualTo(1));
            Assert.That(new Texture2D(8, 8, TextureFormat.RGBA32, -1, false).mipmapCount, Is.EqualTo(4));
        }

        [Test]
        public void Texture2D_RawTextureDataViewsAliasEachOther()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            NativeArray<Color32> asColor = texture.GetRawTextureData<Color32>();
            NativeArray<byte> asBytes = texture.GetRawTextureData<byte>();

            Assert.That(asColor.Length, Is.EqualTo(4));
            Assert.That(asBytes.Length, Is.EqualTo(16));

            asColor[1] = new Color32(10, 20, 30, 40);

            Assert.That(asBytes[4], Is.EqualTo(10));
            Assert.That(asBytes[5], Is.EqualTo(20));
            Assert.That(asBytes[6], Is.EqualTo(30));
            Assert.That(asBytes[7], Is.EqualTo(40));

            // And the other way: a byte write is visible through the typed view, because neither owns the memory.
            asBytes[0] = 99;
            Assert.That(asColor[0].r, Is.EqualTo(99));
        }

        [Test]
        public void Texture2D_RawTextureDataAliasesTheCpuStoreItself()
        {
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false);

            NativeArray<byte> view = texture.GetRawTextureData<byte>();
            view[3] = 123;

            Assert.That(texture.pixels[3], Is.EqualTo(123));
        }

        [Test]
        public void Texture2D_GetRawTextureDataByteArrayIsACopy()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            byte[] copy = texture.GetRawTextureData();

            copy[0] = 200;

            Assert.That(texture.pixels[0], Is.EqualTo(0), "the byte[] overload must not alias the store");
        }

        [Test]
        public void Texture2D_SetPixels32RoundTripsThroughGetPixels32()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var source = new[]
            {
                new Color32(1, 2, 3, 4),
                new Color32(5, 6, 7, 8),
                new Color32(9, 10, 11, 12),
                new Color32(13, 14, 15, 16),
            };

            texture.SetPixels32(source);
            Color32[] read = texture.GetPixels32();

            Assert.That(read.Length, Is.EqualTo(4));

            for (int i = 0; i < source.Length; ++i)
            {
                Assert.That(read[i].r, Is.EqualTo(source[i].r), "pixel " + i);
                Assert.That(read[i].g, Is.EqualTo(source[i].g), "pixel " + i);
                Assert.That(read[i].b, Is.EqualTo(source[i].b), "pixel " + i);
                Assert.That(read[i].a, Is.EqualTo(source[i].a), "pixel " + i);
            }
        }

        [Test]
        public void Texture2D_SetPixels32SubRectPlacesRowsBottomUp()
        {
            // NowGradient's ramp atlas: one row at a time into a tall texture (NowGradient.cs:607). Row `y` counted
            // from the BOTTOM must be the row that changes, or every gradient in the process samples the wrong strip.
            var texture = new Texture2D(3, 4, TextureFormat.RGBA32, false);
            var row = new[] { new Color32(1, 1, 1, 1), new Color32(2, 2, 2, 2), new Color32(3, 3, 3, 3) };

            texture.SetPixels32(0, 2, 3, 1, row);

            Color32[] all = texture.GetPixels32();

            // Row 2 counted from the bottom starts at index 2 * width.
            Assert.That(all[6].r, Is.EqualTo(1));
            Assert.That(all[7].r, Is.EqualTo(2));
            Assert.That(all[8].r, Is.EqualTo(3));

            // Every other row is untouched.
            Assert.That(all[0].r, Is.EqualTo(0));
            Assert.That(all[5].r, Is.EqualTo(0));
            Assert.That(all[9].r, Is.EqualTo(0));
        }

        [Test]
        public void Texture2D_SetPixels32SubRectHonoursTheXOffset()
        {
            var texture = new Texture2D(4, 2, TextureFormat.RGBA32, false);
            var block = new[] { new Color32(7, 0, 0, 0), new Color32(8, 0, 0, 0) };

            texture.SetPixels32(1, 1, 2, 1, block);

            Color32[] all = texture.GetPixels32();

            Assert.That(all[4].r, Is.EqualTo(0));
            Assert.That(all[5].r, Is.EqualTo(7));
            Assert.That(all[6].r, Is.EqualTo(8));
            Assert.That(all[7].r, Is.EqualTo(0));
        }

        [Test]
        public void Texture2D_SetPixels32OutsideTheTextureThrows()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var block = new[] { new Color32(1, 1, 1, 1) };

            Assert.Throws<ArgumentException>(() => texture.SetPixels32(2, 0, 1, 1, block));
            Assert.Throws<ArgumentException>(() => texture.SetPixels32(0, 0, 3, 1, block));
            Assert.Throws<ArgumentException>(() => texture.SetPixels32(-1, 0, 1, 1, block));
        }

        [Test]
        public void Texture2D_SetPixels32WithTooFewColoursThrows()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            Assert.Throws<ArgumentException>(() => texture.SetPixels32(new[] { new Color32(1, 1, 1, 1) }));
        }

        [Test]
        public void Texture2D_LoadRawTextureDataRoundTrips()
        {
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false);
            var raw = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            texture.LoadRawTextureData(raw);

            Assert.That(texture.GetRawTextureData(), Is.EqualTo(raw));
        }

        [Test]
        public void Texture2D_LoadRawTextureDataTooShortThrows()
        {
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false);

            Assert.Throws<UnityException>(() => texture.LoadRawTextureData(new byte[4]));
        }

        [Test]
        public void Texture2D_LoadRawTextureDataFromNativeArrayRoundTrips()
        {
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false);
            var source = new NativeArray<Color32>(2, Allocator.Temp);
            source[0] = new Color32(1, 2, 3, 4);
            source[1] = new Color32(5, 6, 7, 8);

            texture.LoadRawTextureData(source);

            Color32[] read = texture.GetPixels32();
            Assert.That(read[0].r, Is.EqualTo(1));
            Assert.That(read[1].a, Is.EqualTo(8));

            source.Dispose();
        }

        [Test]
        public void Texture2D_GetAndSetPixelRoundTrip()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            texture.SetPixel(1, 1, new Color(1F, 0F, 0F, 1F));

            Assert.That(texture.GetPixel(1, 1).r, Is.EqualTo(1F).Within(1F / 255F));
            Assert.That(texture.GetPixel(0, 0).r, Is.EqualTo(0F));
        }

        [Test]
        public void Texture2D_FloatFormatRoundTripsThroughTheFloatCodec()
        {
            var texture = new Texture2D(2, 1, TextureFormat.RFloat, false);

            texture.SetPixel(0, 0, new Color(2.5F, 0F, 0F, 1F));

            Assert.That(texture.GetPixel(0, 0).r, Is.EqualTo(2.5F), "RFloat is not clamped to [0,1]");
            Assert.That(texture.pixels.Length, Is.EqualTo(2 * 4));
        }

        [Test]
        public void Texture2D_PackedFormatRefusesTexelAccessButStillSizes()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB565, false);

            Assert.That(texture.pixels.Length, Is.EqualTo(2 * 2 * 2), "raw paths still work for a packed format");
            Assert.Throws<UnityException>(() => texture.GetPixel(0, 0));
        }

        // -------------------------------------------------------------------------------------- Texture2D: Apply

        [Test]
        public void Apply_BumpsUpdateCountAndVersion()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            uint updateBefore = texture.updateCount;
            uint versionBefore = texture.version;

            texture.Apply();

            Assert.That(texture.updateCount, Is.EqualTo(updateBefore + 1));
            Assert.That(texture.version, Is.EqualTo(versionBefore + 1));

            texture.Apply();

            Assert.That(texture.updateCount, Is.EqualTo(updateBefore + 2), "NowSdf's staleness signal must move every Apply");
        }

        [Test]
        public void Apply_UploadsTheUnionOfTheSubRectsWrittenSinceTheLastApply()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            texture.Apply();

            var one = new[] { new Color32(1, 1, 1, 1) };
            texture.SetPixels32(1, 1, 1, 1, one);
            texture.SetPixels32(4, 3, 1, 1, one);

            texture.Apply(false, false);

            Assert.That(m_Backend.lastDirtyRect.x, Is.EqualTo(1));
            Assert.That(m_Backend.lastDirtyRect.y, Is.EqualTo(1));
            Assert.That(m_Backend.lastDirtyRect.width, Is.EqualTo(4));
            Assert.That(m_Backend.lastDirtyRect.height, Is.EqualTo(3));
        }

        [Test]
        public void Apply_WithNothingWrittenUploadsTheWholeTexture()
        {
            var texture = new Texture2D(5, 3, TextureFormat.RGBA32, false);
            texture.Apply();

            // Second Apply with no intervening write: Unity re-uploads everything rather than nothing.
            texture.Apply();

            Assert.That(m_Backend.lastDirtyRect.x, Is.EqualTo(0));
            Assert.That(m_Backend.lastDirtyRect.y, Is.EqualTo(0));
            Assert.That(m_Backend.lastDirtyRect.width, Is.EqualTo(5));
            Assert.That(m_Backend.lastDirtyRect.height, Is.EqualTo(3));
        }

        [Test]
        public void Apply_PassesTheWholeCpuStoreAndTheMipFlag()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            int uploadsBefore = m_Backend.textureUploads;

            texture.Apply(true, false);

            Assert.That(m_Backend.textureUploads, Is.EqualTo(uploadsBefore + 1));
            Assert.That(m_Backend.lastUploadByteCount, Is.EqualTo(4 * 4 * 4));
        }

        [Test]
        public void Apply_SealKeepsTheCpuCopyByDefault()
        {
            // Design §1.2: font atlas pages are sealed with Apply(false, true) (NowFont.cs:3465-3471), and a browser
            // that loses its WebGL context has to re-upload them, so the store must survive the seal.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            texture.Apply(false, true);

            Assert.That(texture.isReadable, Is.False);
            Assert.That(texture.pixels, Is.Not.Null, "the CPU store survives a seal by default");
            Assert.Throws<UnityException>(() => texture.GetRawTextureData<byte>());
        }

        [Test]
        public void Apply_SealDropsTheCpuCopyWhenTheHostAsksForIt()
        {
            NowRuntime.releaseCpuCopiesOnSeal = true;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.Apply(false, true);

            Assert.That(texture.pixels, Is.Null);
            Assert.That(Texture.currentTextureMemory, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void Apply_SealedTextureRefusesEveryReadableEntryPoint()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.Apply(false, true);

            Assert.Throws<UnityException>(() => texture.GetPixels32());
            Assert.Throws<UnityException>(() => texture.LoadRawTextureData(new byte[16]));
            Assert.Throws<UnityException>(() => texture.SetPixels32(new Color32[4]));
        }

        [Test]
        public void Texture2D_WidthAndHeightSettersAreInert()
        {
            // Unity refuses the resize and keeps the old size; honouring it would leave width disagreeing with the
            // CPU store and every row offset after it wrong.
            var texture = new Texture2D(4, 2, TextureFormat.RGBA32, false);

            texture.width = 64;
            texture.height = 64;

            Assert.That(texture.width, Is.EqualTo(4));
            Assert.That(texture.height, Is.EqualTo(2));
            Assert.That(texture.pixels.Length, Is.EqualTo(4 * 2 * 4));
        }

        [Test]
        public void Reinitialize_ResizesTheStore()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            texture.Reinitialize(4, 4);

            Assert.That(texture.width, Is.EqualTo(4));
            Assert.That(texture.height, Is.EqualTo(4));
            Assert.That(texture.pixels.Length, Is.EqualTo(4 * 4 * 4));
            Assert.That(texture.format, Is.EqualTo(TextureFormat.RGBA32));
        }

        [Test]
        public void Destroy_ReleasesTheBackendTextureAndTheStore()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            int releasesBefore = m_Backend.textureReleases;

            UnityEngine.Object.DestroyImmediate(texture);

            Assert.That(m_Backend.textureReleases, Is.EqualTo(releasesBefore + 1));
            Assert.That(texture == null, Is.True, "a destroyed texture fake-nulls");
            Assert.That(texture.pixels, Is.Null);
        }

        [Test]
        public void Instantiate_ClonesTheCpuStoreIndependently()
        {
            var original = new Texture2D(2, 1, TextureFormat.RGBA32, false);
            original.SetPixels32(new[] { new Color32(9, 0, 0, 0), new Color32(8, 0, 0, 0) });

            var clone = UnityEngine.Object.Instantiate(original);

            Assert.That(clone.GetPixels32()[0].r, Is.EqualTo(9));

            clone.SetPixels32(new[] { new Color32(1, 0, 0, 0), new Color32(2, 0, 0, 0) });

            Assert.That(original.GetPixels32()[0].r, Is.EqualTo(9), "the clone must not alias the original's store");
        }

        // -------------------------------------------------------------------------- Texture2D: built-in constants

        [Test]
        public void BuiltInTextures_AreLazyOnePixelSingletons()
        {
            Texture2D black = Texture2D.blackTexture;

            Assert.That(Texture2D.blackTexture, Is.SameAs(black), "created once, then cached");
            Assert.That(black.width, Is.EqualTo(1));
            Assert.That(black.height, Is.EqualTo(1));
            Assert.That(black.name, Is.Not.Empty);

            Color32 blackTexel = black.GetPixels32()[0];
            Assert.That(blackTexel.r, Is.EqualTo(0));
            Assert.That(blackTexel.a, Is.EqualTo(255));

            Color32 whiteTexel = Texture2D.whiteTexture.GetPixels32()[0];
            Assert.That(whiteTexel.r, Is.EqualTo(255));
            Assert.That(whiteTexel.a, Is.EqualTo(255));

            Assert.That(Texture2D.grayTexture.GetPixels32()[0].r, Is.EqualTo(128));
            Assert.That(Texture2D.redTexture.GetPixels32()[0].r, Is.EqualTo(255));
            Assert.That(Texture2D.redTexture.GetPixels32()[0].g, Is.EqualTo(0));
            Assert.That(Texture2D.normalTexture.GetPixels32()[0].b, Is.EqualTo(255));

            Assert.That(Texture2D.whiteTexture, Is.Not.SameAs(Texture2D.blackTexture));
        }

        // ------------------------------------------------------------------------------- RenderTextureDescriptor

        [Test]
        public void RenderTextureDescriptor_ConstructorDefaults()
        {
            var desc = new RenderTextureDescriptor(320, 240, RenderTextureFormat.ARGBHalf, 24);

            Assert.That(desc.width, Is.EqualTo(320));
            Assert.That(desc.height, Is.EqualTo(240));
            Assert.That(desc.colorFormat, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            Assert.That(desc.depthBufferBits, Is.EqualTo(24));
            Assert.That(desc.msaaSamples, Is.EqualTo(1));
            Assert.That(desc.volumeDepth, Is.EqualTo(1));
            Assert.That(desc.mipCount, Is.EqualTo(-1));
            Assert.That(desc.dimension, Is.EqualTo(TextureDimension.Tex2D));
            Assert.That(desc.vrUsage, Is.EqualTo(VRTextureUsage.None));
            Assert.That(desc.autoGenerateMips, Is.True);
            Assert.That(desc.useMipMap, Is.False);
            Assert.That(desc.bindMS, Is.False);
            Assert.That(desc.enableRandomWrite, Is.False);
            Assert.That(desc.sRGB, Is.False, "gamma rendering means a default target is not sRGB-converted");
        }

        [Test]
        public void RenderTextureDescriptor_SRgbFollowsTheRuntimeColorSpace()
        {
            NowRuntime.colorSpace = ColorSpace.Linear;

            Assert.That(new RenderTextureDescriptor(4, 4).sRGB, Is.True);
        }

        // -------------------------------------------------------------------------------------- RenderTexture

        [Test]
        public void RenderTexture_ConstructorFillsTheDescriptor()
        {
            var rt = new RenderTexture(128, 64, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);

            Assert.That(rt.width, Is.EqualTo(128));
            Assert.That(rt.height, Is.EqualTo(64));
            Assert.That(rt.depth, Is.EqualTo(24));
            Assert.That(rt.format, Is.EqualTo(RenderTextureFormat.RFloat));
            Assert.That(rt.descriptor.sRGB, Is.False);
            Assert.That(rt.dimension, Is.EqualTo(TextureDimension.Tex2D));
            Assert.That(rt.volumeDepth, Is.EqualTo(1));
            Assert.That(rt.antiAliasing, Is.EqualTo(1));
            Assert.That(rt.isReadable, Is.False);
            Assert.That(rt.IsCreated(), Is.False, "no constructor touches the backend (design §3.5)");
        }

        [Test]
        public void RenderTexture_ReadWriteSRgbResolvesToAnSRgbDescriptor()
        {
            var rt = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

            Assert.That(rt.descriptor.sRGB, Is.True);
        }

        [Test]
        public void RenderTexture_CreateIsIdempotentAndReleaseFrees()
        {
            var rt = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32);

            Assert.That(rt.Create(), Is.True);
            Assert.That(rt.Create(), Is.True);
            Assert.That(m_Backend.renderTextureCreates, Is.EqualTo(1), "an already-created target must not re-create");
            Assert.That(rt.IsCreated(), Is.True);

            rt.Release();

            Assert.That(rt.IsCreated(), Is.False);
            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(1));

            rt.Release();
            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(1), "a second Release is a no-op");
        }

        [Test]
        public void RenderTexture_CreateBuildsTheRequestFromTheDescriptor()
        {
            var recorder = new NowRecordingRenderBackend();
            NowRuntime.Initialize(m_Host, recorder);

            var rt = new RenderTexture(new RenderTextureDescriptor(16, 8, RenderTextureFormat.RHalf, 16)
            {
                useMipMap = true,
                msaaSamples = 4,
            });

            rt.Create();

            string log = recorder.ToLog();
            Assert.That(log, Does.Contain("16x8"));
            Assert.That(log, Does.Contain("depth=16"));
            Assert.That(log, Does.Contain("format=RHalf"));
            Assert.That(log, Does.Contain("msaa=4"));
            Assert.That(log, Does.Contain("useMipMap=true"));
            // A request never carries `Default`: the descriptor has already resolved the colour handling.
            Assert.That(log, Does.Contain("readWrite=Linear"));
        }

        [Test]
        public void RenderTexture_ChangingSizeReleasesSoTheNextCreateRebuilds()
        {
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            rt.Create();

            rt.width = 32;

            Assert.That(rt.IsCreated(), Is.False, "a target whose size no longer matches the GPU object is not created");
            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(1));

            rt.Create();

            Assert.That(m_Backend.renderTextureCreates, Is.EqualTo(2));
            Assert.That(rt.width, Is.EqualTo(32));
            Assert.That(rt.descriptor.width, Is.EqualTo(32));
        }

        [Test]
        public void RenderTexture_AssigningTheSameValueDoesNotRelease()
        {
            // NowSdfImageField assigns antiAliasing / useMipMap / autoGenerateMips on every acquire
            // (NowSdfImageField.cs:446-451). Those acquires come from the pool, whose key contains all three, so a
            // pooled target must survive the assignment untouched.
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            rt.Create();

            rt.antiAliasing = 1;
            rt.useMipMap = false;
            rt.autoGenerateMips = true;
            rt.width = 16;

            Assert.That(rt.IsCreated(), Is.True);
            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(0));
        }

        [Test]
        public void RenderTexture_IsCreatedReportsFalseAfterAContextLoss()
        {
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            Assert.That(rt.IsCreated(), Is.True);

            // A fresh backend has no GPU object for this handle, which is exactly what a WebGL context loss looks
            // like from the shim's side (NowSdf.cs:4731-4744 recovers through this branch).
            var replacement = new NullRenderBackend();
            NowRuntime.Initialize(m_Host, replacement);

            Assert.That(rt.IsCreated(), Is.False);
            Assert.That(rt.Create(), Is.True, "recovery must actually re-create rather than short-circuit");
            Assert.That(replacement.renderTextureCreates, Is.EqualTo(1));
            Assert.That(rt.IsCreated(), Is.True);
        }

        [Test]
        public void RenderTexture_UseMipMapDrivesMipmapCount()
        {
            var rt = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32);
            Assert.That(rt.mipmapCount, Is.EqualTo(1));

            rt.useMipMap = true;

            Assert.That(rt.mipmapCount, Is.EqualTo(7), "1 + floor(log2(64))");
        }

        [Test]
        public void RenderTexture_CopyConstructorCopiesSettingsNotTheGpuObject()
        {
            var source = new RenderTexture(8, 4, 16, RenderTextureFormat.RGHalf, RenderTextureReadWrite.sRGB);
            source.filterMode = FilterMode.Point;
            source.wrapMode = TextureWrapMode.Clamp;
            source.Create();

            var copy = new RenderTexture(source);

            Assert.That(copy.width, Is.EqualTo(8));
            Assert.That(copy.height, Is.EqualTo(4));
            Assert.That(copy.depth, Is.EqualTo(16));
            Assert.That(copy.format, Is.EqualTo(RenderTextureFormat.RGHalf));
            Assert.That(copy.descriptor.sRGB, Is.True);
            Assert.That(copy.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(copy.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(copy.IsCreated(), Is.False);
        }

        [Test]
        public void RenderTexture_DestroyReleasesTheGpuObject()
        {
            var rt = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
            rt.Create();

            UnityEngine.Object.DestroyImmediate(rt);

            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(1));
            Assert.That(m_Backend.liveRenderTextures, Is.EqualTo(0));
        }

        [Test]
        public void RenderTexture_IsADistinctTypeFromTexture2D()
        {
            // NowSdf.cs:3956 and NowSdfImageField.cs:156 both branch on `texture is RenderTexture`.
            Texture asBase = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
            Texture flat = new Texture2D(4, 4);

            Assert.That(asBase is RenderTexture, Is.True);
            Assert.That(flat is RenderTexture, Is.False);
        }

        [Test]
        public void RenderTexture_ActiveRoundTripsThroughTheImmediateState()
        {
            // Exercises the shim-side seam only: RenderTexture.active is defined (design §3.5) as
            // NowImmediate.activeTarget.texture / NowImmediate.SetActive, both owned by unit U11.
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            rt.Create();

            RenderTexture previous = RenderTexture.active;

            RenderTexture.active = rt;
            Assert.That(RenderTexture.active, Is.SameAs(rt));

            RenderTexture.active = null;
            Assert.That(RenderTexture.active, Is.Null);

            RenderTexture.active = previous;
        }

        // ------------------------------------------------------------------------------------ the temporary pool

        [Test]
        public void Pool_ReusesAReleasedTarget()
        {
            RenderTexture first = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32);

            Assert.That(first.name, Is.EqualTo("TempBuffer"));
            Assert.That(first.IsCreated(), Is.True, "GetTemporary hands back a created target");
            Assert.That(NowTemporaryRenderTexturePool.outstandingCount, Is.EqualTo(1));

            RenderTexture.ReleaseTemporary(first);

            Assert.That(NowTemporaryRenderTexturePool.outstandingCount, Is.EqualTo(0));
            Assert.That(NowTemporaryRenderTexturePool.freeCount, Is.EqualTo(1));

            RenderTexture second = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32);

            Assert.That(second, Is.SameAs(first));
            Assert.That(m_Backend.renderTextureCreates, Is.EqualTo(1), "a pool hit never touches the backend");
        }

        [Test]
        public void Pool_FilterAndWrapModeAreNotPartOfTheKey()
        {
            // Design §3.5: NowUI assigns filterMode/wrapMode AFTER the acquire (NowSdfImageField.cs:448-451), so
            // keying on them would guarantee a miss on every first acquire and double the pool.
            RenderTexture first = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.RFloat);
            first.filterMode = FilterMode.Point;
            first.wrapMode = TextureWrapMode.Clamp;
            RenderTexture.ReleaseTemporary(first);

            RenderTexture second = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.RFloat);

            Assert.That(second, Is.SameAs(first));
            Assert.That(NowTemporaryRenderTexturePool.shelfCount, Is.EqualTo(0), "the one entry came off the shelf");
            Assert.That(second.filterMode, Is.EqualTo(FilterMode.Bilinear), "a reused target arrives with fresh sampler state");
        }

        [Test]
        public void Pool_KeysOnSizeFormatDepthAndAntiAliasing()
        {
            RenderTexture a = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32);
            RenderTexture.ReleaseTemporary(a);

            // Each of these differs from `a` in exactly one keyed component and must therefore miss.
            RenderTexture differentWidth = RenderTexture.GetTemporary(64, 32, 0, RenderTextureFormat.ARGB32);
            RenderTexture differentHeight = RenderTexture.GetTemporary(32, 64, 0, RenderTextureFormat.ARGB32);
            RenderTexture differentDepth = RenderTexture.GetTemporary(32, 32, 24, RenderTextureFormat.ARGB32);
            RenderTexture differentFormat = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.RFloat);
            RenderTexture differentMsaa = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);

            Assert.That(differentWidth, Is.Not.SameAs(a));
            Assert.That(differentHeight, Is.Not.SameAs(a));
            Assert.That(differentDepth, Is.Not.SameAs(a));
            Assert.That(differentFormat, Is.Not.SameAs(a));
            Assert.That(differentMsaa, Is.Not.SameAs(a));
            Assert.That(m_Backend.renderTextureCreates, Is.EqualTo(6));

            // The original is still on its shelf, untouched by the five misses.
            RenderTexture again = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32);
            Assert.That(again, Is.SameAs(a));
        }

        [Test]
        public void Pool_KeysOnTheResolvedReadWriteMode()
        {
            RenderTexture linear = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture.ReleaseTemporary(linear);

            RenderTexture srgb = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

            Assert.That(srgb, Is.Not.SameAs(linear), "sRGB and linear targets are different resources");

            RenderTexture.ReleaseTemporary(srgb);

            // In gamma rendering `Default` resolves to Linear, so it must share the linear shelf rather than open a
            // third one.
            RenderTexture asDefault = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            Assert.That(asDefault, Is.SameAs(linear));
        }

        [Test]
        public void Pool_TrimsEntriesUnusedForEightFrames()
        {
            RenderTexture temp = RenderTexture.GetTemporary(48, 48, 0, RenderTextureFormat.ARGB32);
            RenderTexture.ReleaseTemporary(temp);

            for (int i = 0; i < NowTemporaryRenderTexturePool.TrimAfterFrames - 1; ++i)
            {
                NowRuntime.BeginFrame();
                NowRuntime.EndFrame();
            }

            Assert.That(NowTemporaryRenderTexturePool.freeCount, Is.EqualTo(1), "still warm after 7 frames");
            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(0));

            NowRuntime.BeginFrame();
            NowRuntime.EndFrame();

            Assert.That(NowTemporaryRenderTexturePool.freeCount, Is.EqualTo(0), "trimmed on the 8th frame");
            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(1));
        }

        [Test]
        public void Pool_DoesNotTrimAnOutstandingTarget()
        {
            RenderTexture temp = RenderTexture.GetTemporary(48, 48, 0, RenderTextureFormat.ARGB32);

            for (int i = 0; i < NowTemporaryRenderTexturePool.TrimAfterFrames + 2; ++i)
            {
                NowRuntime.BeginFrame();
                NowRuntime.EndFrame();
            }

            Assert.That(temp.IsCreated(), Is.True);
            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(0));

            RenderTexture.ReleaseTemporary(temp);
        }

        [Test]
        public void Pool_ReuseKeepsTheEntryWarm()
        {
            RenderTexture temp = RenderTexture.GetTemporary(24, 24, 0, RenderTextureFormat.ARGB32);
            RenderTexture.ReleaseTemporary(temp);

            for (int i = 0; i < 6; ++i)
            {
                NowRuntime.BeginFrame();
                NowRuntime.EndFrame();
            }

            // Re-acquiring and releasing restamps the entry, so the countdown restarts.
            RenderTexture again = RenderTexture.GetTemporary(24, 24, 0, RenderTextureFormat.ARGB32);
            Assert.That(again, Is.SameAs(temp));
            RenderTexture.ReleaseTemporary(again);

            for (int i = 0; i < 6; ++i)
            {
                NowRuntime.BeginFrame();
                NowRuntime.EndFrame();
            }

            Assert.That(NowTemporaryRenderTexturePool.freeCount, Is.EqualTo(1));
            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(0));
        }

        [Test]
        public void Pool_RejectsForeignAndDoubleReleases()
        {
            Assert.Throws<ArgumentNullException>(() => RenderTexture.ReleaseTemporary(null));

            var foreign = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
            Assert.Throws<ArgumentException>(() => RenderTexture.ReleaseTemporary(foreign));

            RenderTexture temp = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32);
            RenderTexture.ReleaseTemporary(temp);
            Assert.Throws<ArgumentException>(() => RenderTexture.ReleaseTemporary(temp));
        }

        [Test]
        public void Pool_ResetEmptiesEverything()
        {
            RenderTexture pooled = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32);
            RenderTexture.ReleaseTemporary(pooled);
            RenderTexture outstanding = RenderTexture.GetTemporary(20, 20, 0, RenderTextureFormat.ARGB32);

            NowTemporaryRenderTexturePool.Reset();

            Assert.That(NowTemporaryRenderTexturePool.freeCount, Is.EqualTo(0));
            Assert.That(NowTemporaryRenderTexturePool.outstandingCount, Is.EqualTo(0));
            Assert.That(m_Backend.renderTextureReleases, Is.EqualTo(1), "only the free entry had its GPU object freed");
            Assert.That(outstanding.IsCreated(), Is.True, "a target the caller still holds is left alone");
        }

        [Test]
        public void Pool_DescriptorOverloadNormalisesAZeroedDescriptor()
        {
            var bare = default(RenderTextureDescriptor);
            bare.width = 12;
            bare.height = 12;

            RenderTexture a = RenderTexture.GetTemporary(bare);

            Assert.That(a.antiAliasing, Is.EqualTo(1));
            Assert.That(a.volumeDepth, Is.EqualTo(1));
            Assert.That(a.dimension, Is.EqualTo(TextureDimension.Tex2D));

            RenderTexture.ReleaseTemporary(a);

            // The same resource described the normalised way must hit the same shelf.
            var normalised = new RenderTextureDescriptor(12, 12, RenderTextureFormat.ARGB32, 0);
            normalised.sRGB = false;
            RenderTexture b = RenderTexture.GetTemporary(normalised);

            Assert.That(b, Is.SameAs(a));
        }

        // --------------------------------------------------------------------------------------------- Sprite

        [Test]
        public void Sprite_CreateRecordsTheRectBorderAndPivot()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            texture.name = "atlas";
            var rect = new Rect(8F, 16F, 32F, 24F);
            var border = new Vector4(2F, 3F, 4F, 5F);

            Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5F, 0.25F), 64F, 0, SpriteMeshType.Tight, border);

            Assert.That(sprite.texture, Is.SameAs(texture));
            Assert.That(sprite.rect, Is.EqualTo(rect));
            Assert.That(sprite.textureRect, Is.EqualTo(rect), "nothing packs sprites here, so textureRect == rect");
            Assert.That(sprite.border, Is.EqualTo(border));
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(64F));
            Assert.That(sprite.name, Is.EqualTo("atlas"));

            // The argument is normalised; the property is in pixels.
            Assert.That(sprite.pivot.x, Is.EqualTo(16F));
            Assert.That(sprite.pivot.y, Is.EqualTo(6F));
        }

        [Test]
        public void Sprite_DefaultsMatchUnity()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);

            Sprite sprite = Sprite.Create(texture, new Rect(0F, 0F, 4F, 4F), new Vector2(0.5F, 0.5F));

            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(100F));
            Assert.That(sprite.border, Is.EqualTo(Vector4.zero));
        }

        [Test]
        public void Sprite_NonPositivePixelsPerUnitFallsBackToOneHundred()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);

            Sprite sprite = Sprite.Create(texture, new Rect(0F, 0F, 4F, 4F), Vector2.zero, 0F);

            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(100F));
        }

        // -------------------------------------------------------------------------------------- ImageConversion

        [Test]
        public void LoadImage_WithoutADecoderReturnsFalse()
        {
            // NowMarkdownImages.cs:424-473 treats false as "this image failed to decode" and shows its placeholder,
            // which is exactly the degraded path a decoder-less host should take (design §4.4).
            m_Host.decoder = null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            Assert.That(texture.LoadImage(new byte[] { 1, 2, 3, 4 }), Is.False);
        }

        [Test]
        public void LoadImage_RejectsEmptyInputWithoutCallingTheDecoder()
        {
            var decoder = new FakeDecoder(2, 2);
            m_Host.decoder = decoder;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            Assert.That(texture.LoadImage(null), Is.False);
            Assert.That(texture.LoadImage(Array.Empty<byte>()), Is.False);
            Assert.That(decoder.decodeCalls, Is.EqualTo(0));
        }

        [Test]
        public void LoadImage_ResizesTheTextureAndCopiesBottomUpRows()
        {
            var decoder = new FakeDecoder(3, 2);
            m_Host.decoder = decoder;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            uint updateBefore = texture.updateCount;

            Assert.That(texture.LoadImage(new byte[] { 0x89, 0x50 }), Is.True);

            Assert.That(texture.width, Is.EqualTo(3));
            Assert.That(texture.height, Is.EqualTo(2));
            Assert.That(texture.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(texture.updateCount, Is.GreaterThan(updateBefore), "LoadImage applies, so the staleness signal moves");

            // FakeDecoder writes the row index into the red channel, bottom row first, which is the decoder contract.
            Color32[] read = texture.GetPixels32();
            Assert.That(read[0].r, Is.EqualTo(0));
            Assert.That(read[3].r, Is.EqualTo(1));
            Assert.That(read[1].g, Is.EqualTo(1), "green carries the column index");
        }

        [Test]
        public void LoadImage_MarkNonReadableSealsTheTexture()
        {
            m_Host.decoder = new FakeDecoder(2, 2);

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);

            Assert.That(texture.LoadImage(new byte[] { 1 }, true), Is.True);
            Assert.That(texture.isReadable, Is.False);
        }

        [Test]
        public void LoadImage_ADecoderThatFailsLeavesTheTextureAlone()
        {
            m_Host.decoder = new FakeDecoder(2, 2) { succeed = false };

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);

            Assert.That(texture.LoadImage(new byte[] { 1 }), Is.False);
            Assert.That(texture.width, Is.EqualTo(4), "a failed decode must not resize the texture");
        }

        [Test]
        public void Encode_WithoutADecoderReturnsNull()
        {
            m_Host.decoder = null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            Assert.That(texture.EncodeToPNG(), Is.Null);
            Assert.That(texture.EncodeToJPG(), Is.Null);
        }

        [Test]
        public void Encode_PassesTheFormatAndClampsTheQuality()
        {
            var decoder = new FakeDecoder(1, 1);
            m_Host.decoder = decoder;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            Assert.That(texture.EncodeToPNG(), Is.Not.Null);
            Assert.That(decoder.lastFormat, Is.EqualTo(NowImageFormat.Png));

            texture.EncodeToJPG(500);
            Assert.That(decoder.lastFormat, Is.EqualTo(NowImageFormat.Jpg));
            Assert.That(decoder.lastQuality, Is.EqualTo(100));

            texture.EncodeToJPG(-3);
            Assert.That(decoder.lastQuality, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------------------------------ test doubles

        /// <summary>A host whose only interesting service is a swappable image decoder.</summary>
        private sealed class TestHost : INowHostServices
        {
            private readonly INowHostServices m_Inner = new DefaultHostServices();

            internal INowImageDecoder decoder;

            public INowClock clock => m_Inner.clock;

            public NowScreenInfo screen => m_Inner.screen;

            public INowLogger logger => m_Inner.logger;

            public INowClipboard clipboard => m_Inner.clipboard;

            public INowTouchKeyboard touchKeyboard => m_Inner.touchKeyboard;

            public INowResourceProvider resources => m_Inner.resources;

            public INowImageDecoder imageDecoder => decoder;

            public INowFetchProvider fetch => m_Inner.fetch;

            public RuntimePlatform platform => m_Inner.platform;

            public string persistentDataPath => m_Inner.persistentDataPath;

            public string dataPath => m_Inner.dataPath;

            public string[] layerNames => m_Inner.layerNames;
        }

        /// <summary>
        /// Produces a picture whose red channel is the row index and whose green channel is the column index, in
        /// bottom-up rows - so a test can tell a correct copy from a vertically flipped one.
        /// </summary>
        private sealed class FakeDecoder : INowImageDecoder
        {
            private readonly int m_Width;
            private readonly int m_Height;

            internal bool succeed = true;
            internal int decodeCalls;
            internal NowImageFormat lastFormat;
            internal int lastQuality;

            internal FakeDecoder(int width, int height)
            {
                m_Width = width;
                m_Height = height;
            }

            public bool TryDecode(ReadOnlySpan<byte> encoded, out int width, out int height, out byte[] rgba32BottomUp, out string error)
            {
                ++decodeCalls;

                if (!succeed)
                {
                    width = 0;
                    height = 0;
                    rgba32BottomUp = null;
                    error = "fake failure";
                    return false;
                }

                width = m_Width;
                height = m_Height;
                error = null;
                rgba32BottomUp = new byte[m_Width * m_Height * 4];

                for (int y = 0; y < m_Height; ++y)
                {
                    for (int x = 0; x < m_Width; ++x)
                    {
                        int offset = (y * m_Width + x) * 4;
                        rgba32BottomUp[offset] = (byte)y;
                        rgba32BottomUp[offset + 1] = (byte)x;
                        rgba32BottomUp[offset + 2] = 0;
                        rgba32BottomUp[offset + 3] = 255;
                    }
                }

                return true;
            }

            public byte[] TryEncode(Texture2D texture, NowImageFormat format, int quality)
            {
                lastFormat = format;
                lastQuality = quality;
                return new byte[] { 1, 2, 3 };
            }
        }
    }
}
