// Tests for the U6 material unit: UnityEngine.Shader, UnityEngine.Material, UnityEngine.MaterialPropertyBlock and the
// NowUI.Engine helper types NowMaterialBag, NowShaderGlobals and NowShaderInfo.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 (member lists and the SetVectorArray rule), §3.10 (helper
// types), §4.1 (backend guarantees), §6.2 step 3 (the intern table is never reset), §7.5 (the "Material" row of the
// semantics suite: SetVectorArray copy/length, CopyPropertiesFromMaterial deep copy, mainTexture identity, HasProperty
// over declared-but-unset properties, and the allocation assertion).
//
// The allocation cases use GC.GetAllocatedBytesForCurrentThread, which is exact for the current thread and is the same
// instrument design §7.5 names for the steady-state frame gate.
using System;
using System.Collections.Generic;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class MaterialTests
    {
        // Every fixture that needs a shader goes through this, so the declared-property tests and the seeding tests
        // agree on what the fake program declares.
        private static Shader MakeShader(string name)
        {
            NowShaderInfo info = new NowShaderInfo(name, 1);
            info.DeclareFloat("_Declared", 0.25f);
            info.DeclareVector("_DeclaredVector", new Vector4(1f, 2f, 3f, 4f));
            info.DeclareTexture("_MainTex");
            // The constructor is internal by design (§3.5): under Unity, shaders come from the asset database, and
            // here they come from the host's resource provider. The tests assembly is a friend, so it can mint one.
            return new Shader(name, info);
        }

        // Any concrete Texture will do: these tests care about reference identity through the bag, not about pixels.
        private static Texture NewTexture()
        {
            return new Texture2D(1, 1);
        }

        // ---------------------------------------------------------------- Shader.PropertyToID

        [Test]
        public void PropertyToID_IsStableAndInterned()
        {
            int a = Shader.PropertyToID("_U6_Stable");
            int b = Shader.PropertyToID("_U6_Stable");

            Assert.That(b, Is.EqualTo(a));
            Assert.That(a, Is.GreaterThanOrEqualTo(1), "ids start at 1 so 0 stays free as 'no property'");
            Assert.That(Shader.PropertyToID("_U6_Other"), Is.Not.EqualTo(a));
        }

        [Test]
        public void PropertyToID_IsCaseSensitiveAndOrdinal()
        {
            Assert.That(Shader.PropertyToID("_U6_Case"), Is.Not.EqualTo(Shader.PropertyToID("_u6_case")));
        }

        [Test]
        public void PropertyToID_NullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => Shader.PropertyToID(null));
        }

        [Test]
        public void PropertyToID_EmptyStringIsAValidProperty()
        {
            // Unity interns "" like any other name; refusing it here would turn a harmless core typo into a crash.
            Assert.That(Shader.PropertyToID(""), Is.EqualTo(Shader.PropertyToID("")));
        }

        // Hazard D.1 #4 in miniature: thirteen core files call PropertyToID from a static *field initialiser*, which
        // runs at type load, long before a host or backend is registered. This nested type reproduces that exactly —
        // if PropertyToID ever needed NowRuntime, loading this type would be where it broke.
        private static class StaticInitialiserProbe
        {
            internal static readonly int id = Shader.PropertyToID("_U6_StaticInitialiser");
        }

        [Test]
        public void PropertyToID_WorksFromAStaticFieldInitialiser()
        {
            Assert.That(StaticInitialiserProbe.id, Is.GreaterThanOrEqualTo(1));
            Assert.That(Shader.PropertyToID("_U6_StaticInitialiser"), Is.EqualTo(StaticInitialiserProbe.id));
        }

        [Test]
        public void ResetAll_DoesNotResetTheInternTable()
        {
            // Design §6.2 step 3: core types cache ids in static readonly fields that a reset cannot re-run, so the
            // table must survive. Asserted through the id itself, not through the count, so a concurrently interned
            // name in another test cannot make this flaky.
            int before = Shader.PropertyToID("_U6_SurvivesReset");
            NowRuntime.ResetAll();
            Assert.That(Shader.PropertyToID("_U6_SurvivesReset"), Is.EqualTo(before));
            Assert.That(Shader.IDToName(before), Is.EqualTo("_U6_SurvivesReset"));
        }

        [Test]
        public void IDToName_RoundTripsAndRejectsUnknownIds()
        {
            int id = Shader.PropertyToID("_U6_RoundTrip");
            Assert.That(Shader.IDToName(id), Is.EqualTo("_U6_RoundTrip"));
            Assert.That(Shader.IDToName(0), Is.Null);
            Assert.That(Shader.IDToName(-1), Is.Null);
            Assert.That(Shader.IDToName(int.MaxValue), Is.Null);
        }

        [Test]
        public void PropertyToID_DoesNotAllocateForAnAlreadyInternedName()
        {
            const string name = "_U6_NoAllocIntern";
            Shader.PropertyToID(name);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
                Shader.PropertyToID(name);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        // ---------------------------------------------------------------- Shader globals

        [Test]
        public void SetGlobal_WritesThroughToNowRuntimeGlobals()
        {
            int floatID = Shader.PropertyToID("_U6_GlobalFloat");
            int vectorID = Shader.PropertyToID("_U6_GlobalVector");

            Shader.SetGlobalFloat(floatID, 2.5f);
            Shader.SetGlobalVector(vectorID, new Vector4(1f, 2f, 3f, 4f));

            Assert.That(Shader.GetGlobalFloat(floatID), Is.EqualTo(2.5f));
            Assert.That(Shader.GetGlobalVector(vectorID), Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
            Assert.That(NowRuntime.globals.GetFloat(floatID), Is.EqualTo(2.5f));
        }

        [Test]
        public void SetGlobalColor_AndGetGlobalVector_ShareOneSlot()
        {
            int id = Shader.PropertyToID("_U6_GlobalColor");
            Shader.SetGlobalColor(id, new Color(0.1f, 0.2f, 0.3f, 0.4f));

            Vector4 asVector = Shader.GetGlobalVector(id);
            Assert.That(asVector.x, Is.EqualTo(0.1f));
            Assert.That(asVector.w, Is.EqualTo(0.4f));
        }

        [Test]
        public void UnsetGlobals_ReadAsZero()
        {
            int id = Shader.PropertyToID("_U6_GlobalNeverSet");
            Assert.That(Shader.GetGlobalFloat(id), Is.EqualTo(0f));
            Assert.That(Shader.GetGlobalVector(id), Is.EqualTo(Vector4.zero));
            Assert.That(Shader.GetGlobalTexture(id), Is.Null);
        }

        [Test]
        public void ShaderGlobals_VersionMovesOnEveryWriteAndOnReset()
        {
            NowShaderGlobals globals = new NowShaderGlobals();
            int id = Shader.PropertyToID("_U6_GlobalsVersion");

            int v0 = globals.version;
            globals.SetFloat(id, 1f);
            int v1 = globals.version;
            globals.SetFloat(id, 1f);
            int v2 = globals.version;
            globals.Reset();
            int v3 = globals.version;

            Assert.That(v1, Is.GreaterThan(v0));
            // Re-setting the same value still moves the version: the shim cannot know the backend saw the old one.
            Assert.That(v2, Is.GreaterThan(v1));
            Assert.That(v3, Is.GreaterThan(v2));
            Assert.That(globals.GetFloat(id), Is.EqualTo(0f));
        }

        [Test]
        public void ShaderGlobals_VectorArrayCopiesAndPreservesLength()
        {
            NowShaderGlobals globals = new NowShaderGlobals();
            int id = Shader.PropertyToID("_U6_GlobalArray");

            Vector4[] scratch = new Vector4[3];
            scratch[0] = new Vector4(1f, 0f, 0f, 0f);
            globals.SetVectorArray(id, scratch);
            scratch[0] = new Vector4(9f, 9f, 9f, 9f);

            Vector4[] stored = globals.vectorArrays[id];
            Assert.That(stored.Length, Is.EqualTo(3));
            Assert.That(stored[0], Is.EqualTo(new Vector4(1f, 0f, 0f, 0f)));
            Assert.That(ReferenceEquals(stored, scratch), Is.False);
        }

        // ---------------------------------------------------------------- Material construction

        [Test]
        public void MaterialFromShader_TakesItsNameAndSeedsDeclaredDefaults()
        {
            Shader shader = MakeShader("NowUI/U6 Seed");
            Material material = new Material(shader);

            Assert.That(material.name, Is.EqualTo("NowUI/U6 Seed"));
            Assert.That(material.shader, Is.SameAs(shader));
            Assert.That(material.GetFloat("_Declared"), Is.EqualTo(0.25f));
            Assert.That(material.GetVector("_DeclaredVector"), Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
            Assert.That(material.passCount, Is.EqualTo(1));
        }

        [Test]
        public void MaterialFromNullShader_IsToleratedAndHasNoPasses()
        {
            // The shim does not throw where Unity merely logs: a host whose resource provider cannot resolve a shader
            // should take NowUI's existing null-material path rather than tear the frame down.
            Material material = new Material((Shader)null);
            Assert.That(material.shader, Is.Null);
            Assert.That(material.passCount, Is.Zero);
            Assert.That(material.HasProperty("_Anything"), Is.False);
        }

        [Test]
        public void CopyConstructor_DeepCopiesArraysAndKeywordsAndKeepsTheName()
        {
            Shader shader = MakeShader("NowUI/U6 Copy");
            Material source = new Material(shader);
            source.name = "source";
            source.SetFloat("_F", 3f);
            source.SetVectorArray("_Arr", new[] { new Vector4(1f, 1f, 1f, 1f), new Vector4(2f, 2f, 2f, 2f) });
            source.EnableKeyword("_U6_KEYWORD");

            Material copy = new Material(source);

            Assert.That(copy.name, Is.EqualTo("source"), "only Object.Instantiate appends (Clone)");
            Assert.That(copy.shader, Is.SameAs(shader));
            Assert.That(copy.GetFloat("_F"), Is.EqualTo(3f));
            Assert.That(copy.IsKeywordEnabled("_U6_KEYWORD"), Is.True);

            // The arrays must not be shared: writing through the source's stored buffer must not be visible in the copy.
            source.SetVectorArray("_Arr", new[] { new Vector4(7f, 7f, 7f, 7f), new Vector4(8f, 8f, 8f, 8f) });
            Assert.That(copy.GetVectorArray("_Arr")[0], Is.EqualTo(new Vector4(1f, 1f, 1f, 1f)));
        }

        [Test]
        public void Instantiate_ClonesTheBagAndAppendsCloneToTheName()
        {
            Material source = new Material(MakeShader("NowUI/U6 Instantiate"));
            source.name = "template";
            source.SetFloat("_F", 5f);

            Material clone = UnityEngine.Object.Instantiate(source);

            Assert.That(clone, Is.Not.SameAs(source));
            Assert.That(clone.name, Is.EqualTo("template(Clone)"));
            Assert.That(clone.GetFloat("_F"), Is.EqualTo(5f));
        }

        // ---------------------------------------------------------------- HasProperty

        [Test]
        public void HasProperty_IsTrueForDeclaredButUnsetShaderProperties()
        {
            // This is the whole point of NowShaderInfo: Now.cs:1020, NowFont.cs:1478 and NowSdf.cs:3508 gate a SetFloat
            // on HasProperty, and a bag-only answer would make every optional feature look absent.
            NowShaderInfo info = new NowShaderInfo("NowUI/U6 Declared", 1);
            info.DeclareFloat("_NowUITextOutlineOnlyPass", 0f);
            info.DeclareTexture("_NowPremultipliedTexture");
            Material material = new Material(new Shader("NowUI/U6 Declared", info));

            Assert.That(material.HasProperty("_NowUITextOutlineOnlyPass"), Is.True);
            Assert.That(material.HasProperty("_NowPremultipliedTexture"), Is.True,
                "a declared texture slot has no default value, but HasProperty must still see it");
            Assert.That(material.HasProperty("_NotDeclared"), Is.False);
        }

        [Test]
        public void HasProperty_IsTrueForAnythingTheBagHolds()
        {
            Material material = new Material(MakeShader("NowUI/U6 BagHas"));

            int id = Shader.PropertyToID("_U6_SetButNotDeclared");
            Assert.That(material.HasProperty(id), Is.False);
            material.SetFloat(id, 1f);
            Assert.That(material.HasProperty(id), Is.True);
        }

        [Test]
        public void HasProperty_SeesEveryBagKind()
        {
            Material material = new Material((Shader)null);

            material.SetInteger("_U6_HasInt", 3);
            material.SetVector("_U6_HasVector", Vector4.zero);
            material.SetMatrix("_U6_HasMatrix", Matrix4x4.identity);
            material.SetTexture("_U6_HasTexture", null);
            material.SetVectorArray("_U6_HasVectorArray", new[] { Vector4.zero });
            material.SetFloatArray("_U6_HasFloatArray", new[] { 0f });

            Assert.That(material.HasProperty("_U6_HasInt"), Is.True);
            Assert.That(material.HasProperty("_U6_HasVector"), Is.True);
            Assert.That(material.HasProperty("_U6_HasMatrix"), Is.True);
            Assert.That(material.HasProperty("_U6_HasTexture"), Is.True,
                "SetTexture(null) still declares the slot, exactly as an assigned-then-cleared sampler does");
            Assert.That(material.HasProperty("_U6_HasVectorArray"), Is.True);
            Assert.That(material.HasProperty("_U6_HasFloatArray"), Is.True);
        }

        // ---------------------------------------------------------------- typed get/set

        [Test]
        public void UnsetProperties_ReadAsUnityDefaults()
        {
            Material material = new Material((Shader)null);

            Assert.That(material.GetFloat("_U6_Unset"), Is.EqualTo(0f));
            Assert.That(material.GetInt("_U6_Unset"), Is.EqualTo(0));
            Assert.That(material.GetVector("_U6_Unset"), Is.EqualTo(Vector4.zero));
            Assert.That(material.GetColor("_U6_Unset"), Is.EqualTo(new Color(0f, 0f, 0f, 0f)));
            Assert.That(material.GetMatrix("_U6_Unset"), Is.EqualTo(Matrix4x4.zero));
            Assert.That(material.GetTexture("_U6_Unset"), Is.Null);
            Assert.That(material.GetVectorArray("_U6_Unset"), Is.Null);
            Assert.That(material.GetFloatArray("_U6_Unset"), Is.Null);
        }

        [Test]
        public void SetColorAndGetVector_ShareOneSlot()
        {
            Material material = new Material((Shader)null);
            material.SetColor("_U6_Color", new Color(0.1f, 0.2f, 0.3f, 0.4f));

            Assert.That(material.GetVector("_U6_Color"), Is.EqualTo(new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));

            material.SetVector("_U6_Color", new Vector4(1f, 0f, 0f, 1f));
            Assert.That(material.GetColor("_U6_Color"), Is.EqualTo(new Color(1f, 0f, 0f, 1f)));
        }

        [Test]
        public void SetInt_WritesTheFloatTableWhileSetIntegerDoesNot()
        {
            // Unity's legacy SetInt is an alias for SetFloat (an int and a float property are the same shader
            // register); SetInteger is the one that writes a real integer uniform.
            Material material = new Material((Shader)null);

            material.SetInt("_U6_LegacyInt", 7);
            Assert.That(material.GetFloat("_U6_LegacyInt"), Is.EqualTo(7f));
            Assert.That(material.GetInt("_U6_LegacyInt"), Is.EqualTo(7));
            Assert.That(material.GetInteger("_U6_LegacyInt"), Is.EqualTo(0));

            material.SetInteger("_U6_RealInt", 9);
            Assert.That(material.GetInteger("_U6_RealInt"), Is.EqualTo(9));
            Assert.That(material.GetFloat("_U6_RealInt"), Is.EqualTo(0f));
        }

        [Test]
        public void GetInt_TruncatesTowardZero()
        {
            Material material = new Material((Shader)null);
            material.SetFloat("_U6_Trunc", 2.9f);
            Assert.That(material.GetInt("_U6_Trunc"), Is.EqualTo(2));
            material.SetFloat("_U6_Trunc", -2.9f);
            Assert.That(material.GetInt("_U6_Trunc"), Is.EqualTo(-2));
        }

        [Test]
        public void StringAndIntOverloads_AddressTheSameSlot()
        {
            Material material = new Material((Shader)null);
            int id = Shader.PropertyToID("_U6_SameSlot");

            material.SetFloat("_U6_SameSlot", 1.5f);
            Assert.That(material.GetFloat(id), Is.EqualTo(1.5f));

            material.SetFloat(id, 2.5f);
            Assert.That(material.GetFloat("_U6_SameSlot"), Is.EqualTo(2.5f));
        }

        // ---------------------------------------------------------------- mainTexture identity (hazard D.1 #10)

        [Test]
        public void MainTexture_GetterReturnsTheIdenticalStoredInstance()
        {
            // NowGradient.cs:675/678 does ReferenceEquals(_material.mainTexture, atlas) to decide whether to re-upload
            // its gradient atlas. A getter that returned a copy or re-resolved would make it re-upload every frame.
            Material material = new Material(MakeShader("NowUI/U6 MainTex"));
            Texture atlas = NewTexture();

            material.mainTexture = atlas;

            Assert.That(ReferenceEquals(material.mainTexture, atlas), Is.True);
            Assert.That(ReferenceEquals(material.mainTexture, material.mainTexture), Is.True);
        }

        [Test]
        public void MainTexture_AliasesTheMainTexSlot()
        {
            Material material = new Material(MakeShader("NowUI/U6 MainTexAlias"));
            Texture atlas = NewTexture();

            material.SetTexture("_MainTex", atlas);
            Assert.That(ReferenceEquals(material.mainTexture, atlas), Is.True);

            Texture other = NewTexture();
            material.mainTexture = other;
            Assert.That(ReferenceEquals(material.GetTexture("_MainTex"), other), Is.True);
        }

        [Test]
        public void MainTextureScaleAndOffset_PackIntoMainTexST()
        {
            Material material = new Material((Shader)null);

            // Unset reads as the identity transform, which is what a shader assumes for an untouched _ST.
            Assert.That(material.mainTextureScale, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(material.mainTextureOffset, Is.EqualTo(new Vector2(0f, 0f)));

            material.mainTextureScale = new Vector2(2f, 3f);
            material.mainTextureOffset = new Vector2(0.25f, 0.5f);

            // Unity packs _ST as (scale.x, scale.y, offset.x, offset.y).
            Assert.That(material.GetVector("_MainTex_ST"), Is.EqualTo(new Vector4(2f, 3f, 0.25f, 0.5f)));
            Assert.That(material.mainTextureScale, Is.EqualTo(new Vector2(2f, 3f)));
            Assert.That(material.mainTextureOffset, Is.EqualTo(new Vector2(0.25f, 0.5f)));
        }

        // ---------------------------------------------------------------- SetVectorArray copy semantics

        [Test]
        public void SetVectorArray_CopiesTheCallersArray()
        {
            // NowMaskShader and NowSdf hand static scratch arrays here and overwrite them next frame.
            Material material = new Material((Shader)null);
            Vector4[] scratch = new Vector4[4];
            scratch[0] = new Vector4(1f, 1f, 1f, 1f);

            material.SetVectorArray("_U6_Scratch", scratch);
            scratch[0] = new Vector4(9f, 9f, 9f, 9f);

            Assert.That(material.GetVectorArray("_U6_Scratch")[0], Is.EqualTo(new Vector4(1f, 1f, 1f, 1f)));
        }

        [Test]
        public void SetVectorArray_PreservesTheCallersLength()
        {
            Material material = new Material((Shader)null);

            material.SetVectorArray("_U6_Len", new Vector4[8]);
            Assert.That(material.GetVectorArray("_U6_Len").Length, Is.EqualTo(8));

            material.SetVectorArray("_U6_Len", new Vector4[2]);
            Assert.That(material.GetVectorArray("_U6_Len").Length, Is.EqualTo(2),
                "a shorter array must shrink the stored property, not leave stale trailing entries");

            material.SetVectorArray("_U6_Len", new Vector4[64]);
            Assert.That(material.GetVectorArray("_U6_Len").Length, Is.EqualTo(64));
        }

        [Test]
        public void SetVectorArray_ReusesTheStoredArrayWhenTheLengthMatches()
        {
            Material material = new Material((Shader)null);
            int id = Shader.PropertyToID("_U6_Reuse");

            material.SetVectorArray(id, new Vector4[8]);
            Vector4[] stored = material.bag.vectorArrays[id];

            material.SetVectorArray(id, new Vector4[8]);
            Assert.That(ReferenceEquals(material.bag.vectorArrays[id], stored), Is.True,
                "the steady-state call must reuse the buffer, which is what makes it allocation-free");

            material.SetVectorArray(id, new Vector4[9]);
            Assert.That(ReferenceEquals(material.bag.vectorArrays[id], stored), Is.False);
        }

        [Test]
        public void SetVectorArray_ListOverloadHasTheSameCopySemantics()
        {
            Material material = new Material((Shader)null);
            List<Vector4> values = new List<Vector4> { new Vector4(1f, 2f, 3f, 4f), new Vector4(5f, 6f, 7f, 8f) };

            material.SetVectorArray("_U6_List", values);
            values[0] = new Vector4(9f, 9f, 9f, 9f);

            Vector4[] stored = material.GetVectorArray("_U6_List");
            Assert.That(stored.Length, Is.EqualTo(2));
            Assert.That(stored[0], Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
        }

        [Test]
        public void GetVectorArray_ReturnsACopyAndTheListOverloadClearsFirst()
        {
            Material material = new Material((Shader)null);
            material.SetVectorArray("_U6_GetCopy", new[] { new Vector4(1f, 0f, 0f, 0f) });

            Vector4[] first = material.GetVectorArray("_U6_GetCopy");
            first[0] = new Vector4(5f, 5f, 5f, 5f);
            Assert.That(material.GetVectorArray("_U6_GetCopy")[0], Is.EqualTo(new Vector4(1f, 0f, 0f, 0f)),
                "mutating the returned array must not reach into the material");

            List<Vector4> sink = new List<Vector4> { Vector4.zero, Vector4.zero, Vector4.zero };
            material.GetVectorArray(Shader.PropertyToID("_U6_GetCopy"), sink);
            Assert.That(sink.Count, Is.EqualTo(1), "Unity's list getters clear the list rather than appending");
        }

        [Test]
        public void SetVectorArray_RejectsNullAndEmpty()
        {
            Material material = new Material((Shader)null);

            Assert.Throws<ArgumentNullException>(() => material.SetVectorArray("_U6_Bad", (Vector4[])null));
            Assert.Throws<ArgumentException>(() => material.SetVectorArray("_U6_Bad", new Vector4[0]));
            Assert.Throws<ArgumentException>(() => material.SetVectorArray("_U6_Bad", new List<Vector4>()));
        }

        [Test]
        public void SetFloatArray_HasTheSameCopyAndLengthRules()
        {
            Material material = new Material((Shader)null);
            float[] scratch = { 1f, 2f, 3f };

            material.SetFloatArray("_U6_Floats", scratch);
            scratch[0] = 99f;

            float[] stored = material.GetFloatArray("_U6_Floats");
            Assert.That(stored.Length, Is.EqualTo(3));
            Assert.That(stored[0], Is.EqualTo(1f));

            material.SetFloatArray("_U6_Floats", new List<float> { 4f });
            Assert.That(material.GetFloatArray("_U6_Floats").Length, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- CopyPropertiesFromMaterial

        [Test]
        public void CopyPropertiesFromMaterial_DeepCopiesArraysAndDoesNotShareBuffers()
        {
            Material source = new Material((Shader)null);
            source.SetVectorArray("_U6_CopyArr", new[] { new Vector4(1f, 1f, 1f, 1f) });
            source.SetFloatArray("_U6_CopyFloats", new[] { 1f, 2f });

            Material destination = new Material((Shader)null);
            destination.CopyPropertiesFromMaterial(source);

            int arrayID = Shader.PropertyToID("_U6_CopyArr");
            Assert.That(ReferenceEquals(destination.bag.vectorArrays[arrayID], source.bag.vectorArrays[arrayID]),
                Is.False, "sharing the buffer would alias the two materials onto one array");
            Assert.That(destination.GetVectorArray("_U6_CopyArr")[0], Is.EqualTo(new Vector4(1f, 1f, 1f, 1f)));
            Assert.That(destination.GetFloatArray("_U6_CopyFloats").Length, Is.EqualTo(2));
        }

        [Test]
        public void CopyPropertiesFromMaterial_ReplacesRatherThanMergesAndKeepsTheShader()
        {
            Shader sourceShader = MakeShader("NowUI/U6 CopySource");
            Shader destinationShader = MakeShader("NowUI/U6 CopyDest");

            Material source = new Material(sourceShader);
            source.SetFloat("_U6_Only_In_Source", 1f);
            source.EnableKeyword("_U6_SOURCE_KEYWORD");

            Material destination = new Material(destinationShader);
            destination.SetFloat("_U6_Only_In_Destination", 2f);
            destination.EnableKeyword("_U6_DESTINATION_KEYWORD");

            destination.CopyPropertiesFromMaterial(source);

            Assert.That(destination.GetFloat("_U6_Only_In_Source"), Is.EqualTo(1f));
            Assert.That(destination.GetFloat("_U6_Only_In_Destination"), Is.EqualTo(0f),
                "the source's property sheet replaces the destination's rather than merging into it");
            Assert.That(destination.IsKeywordEnabled("_U6_SOURCE_KEYWORD"), Is.True);
            Assert.That(destination.IsKeywordEnabled("_U6_DESTINATION_KEYWORD"), Is.False);

            // Now.GetTexturedMaterial (Now.cs:953-1015) checks shader identity separately and then calls this, so
            // copying the shader here would make that check meaningless.
            Assert.That(destination.shader, Is.SameAs(destinationShader));
        }

        [Test]
        public void CopyPropertiesFromMaterial_DropsArrayPropertiesTheSourceDoesNotHave()
        {
            Material source = new Material((Shader)null);
            Material destination = new Material((Shader)null);
            destination.SetVectorArray("_U6_Stale", new[] { Vector4.zero });

            destination.CopyPropertiesFromMaterial(source);

            Assert.That(destination.GetVectorArray("_U6_Stale"), Is.Null);
            Assert.That(destination.HasProperty("_U6_Stale"), Is.False);
        }

        [Test]
        public void CopyPropertiesFromMaterial_SelfCopyIsANoOp()
        {
            Material material = new Material((Shader)null);
            material.SetFloat("_U6_Self", 4f);

            material.CopyPropertiesFromMaterial(material);

            Assert.That(material.GetFloat("_U6_Self"), Is.EqualTo(4f));
        }

        [Test]
        public void CopyPropertiesFromMaterial_NullThrows()
        {
            Material material = new Material((Shader)null);
            Assert.Throws<ArgumentNullException>(() => material.CopyPropertiesFromMaterial(null));
        }

        // ---------------------------------------------------------------- keywords

        [Test]
        public void Keywords_EnableDisableAndReadBack()
        {
            Material material = new Material((Shader)null);

            Assert.That(material.IsKeywordEnabled("_U6_KW"), Is.False);
            material.EnableKeyword("_U6_KW");
            Assert.That(material.IsKeywordEnabled("_U6_KW"), Is.True);
            Assert.That(material.shaderKeywords, Is.EquivalentTo(new[] { "_U6_KW" }));

            material.DisableKeyword("_U6_KW");
            Assert.That(material.IsKeywordEnabled("_U6_KW"), Is.False);
            Assert.That(material.shaderKeywords.Length, Is.Zero);
        }

        [Test]
        public void ShaderKeywords_SetterReplacesTheWholeSet()
        {
            Material material = new Material((Shader)null);
            material.EnableKeyword("_U6_OLD");

            material.shaderKeywords = new[] { "_U6_A", "_U6_B" };

            Assert.That(material.IsKeywordEnabled("_U6_OLD"), Is.False);
            Assert.That(material.shaderKeywords, Is.EquivalentTo(new[] { "_U6_A", "_U6_B" }));

            material.shaderKeywords = null;
            Assert.That(material.shaderKeywords.Length, Is.Zero);
        }

        // ---------------------------------------------------------------- version

        [Test]
        public void Version_MovesOnEverySetter()
        {
            // Design §4.1 guarantee 1: a backend caches by (instance id, version) and re-uploads only on a mismatch.
            Material material = new Material((Shader)null);

            uint before = material.version;
            material.SetFloat("_U6_Version", 1f);
            uint afterFloat = material.version;
            material.SetVector("_U6_Version2", Vector4.zero);
            uint afterVector = material.version;

            Assert.That(afterFloat, Is.GreaterThan(before));
            Assert.That(afterVector, Is.GreaterThan(afterFloat));
            Assert.That(material.bag.version, Is.GreaterThan(0u));
        }

        // ---------------------------------------------------------------- SetPass and destruction

        [Test]
        public void SetPass_RecordsThePairForTheImmediatePathAndAlwaysSucceeds()
        {
            // Design §3.5: SetPass returns true unconditionally. Under Unity a false return means the pass was culled
            // by the current shader LOD or a replacement tag, and the shim has neither.
            Material material = new Material(MakeShader("NowUI/U6 SetPass"));

            Assert.That(material.SetPass(0), Is.True);
            Assert.That(NowImmediate.activePass.material, Is.SameAs(material));
            Assert.That(NowImmediate.activePass.pass, Is.EqualTo(0));

            Assert.That(material.SetPass(3), Is.True, "an out-of-range pass is the backend's problem, not the shim's");
            Assert.That(NowImmediate.activePass.pass, Is.EqualTo(3));
        }

        [Test]
        public void Destroy_ReleasesTheMaterialAndMakesItFakeNull()
        {
            Material material = new Material((Shader)null);
            material.SetFloat("_U6_Destroyed", 1f);

            UnityEngine.Object.DestroyImmediate(material);

            Assert.That(material == null, Is.True, "fake-null: a destroyed Object compares equal to null");
            Assert.That(ReferenceEquals(material, null), Is.False);
        }

        // ---------------------------------------------------------------- MaterialPropertyBlock

        [Test]
        public void PropertyBlock_StartsEmptyAndClearsBackToEmpty()
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            Assert.That(block.isEmpty, Is.True);

            block.SetFloat("_U6_BlockFloat", 1f);
            Assert.That(block.isEmpty, Is.False);

            block.Clear();
            Assert.That(block.isEmpty, Is.True);
            Assert.That(block.GetFloat("_U6_BlockFloat"), Is.EqualTo(0f));
        }

        [Test]
        public void PropertyBlock_HasTheSameBagSemanticsAsMaterial()
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            Texture texture = NewTexture();

            block.SetFloat("_U6_BF", 2f);
            block.SetVector("_U6_BV", new Vector4(1f, 2f, 3f, 4f));
            block.SetTexture("_U6_BT", texture);

            Vector4[] scratch = { new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f) };
            block.SetVectorArray("_U6_BA", scratch);
            scratch[0] = new Vector4(9f, 9f, 9f, 9f);

            Assert.That(block.GetFloat("_U6_BF"), Is.EqualTo(2f));
            Assert.That(block.GetVector("_U6_BV"), Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
            Assert.That(ReferenceEquals(block.GetTexture("_U6_BT"), texture), Is.True);
            Assert.That(block.GetVectorArray("_U6_BA")[0], Is.EqualTo(new Vector4(1f, 0f, 0f, 0f)));
            Assert.That(block.GetVectorArray("_U6_BA").Length, Is.EqualTo(2));
            Assert.That(block.HasProperty("_U6_BF"), Is.True);
            Assert.That(block.HasProperty("_U6_Never"), Is.False);
        }

        [Test]
        public void PropertyBlock_SnapshotIsDetachedFromTheSourceBlock()
        {
            // NowMaskShader hands one shared block to consecutive CommandBuffer.DrawMesh calls and mutates it between
            // them; Unity copies at record time, so the shim's snapshot must not track later edits (design §3.5).
            MaterialPropertyBlock live = new MaterialPropertyBlock();
            live.SetFloat("_U6_Snap", 1f);
            live.SetVectorArray("_U6_SnapArr", new[] { new Vector4(1f, 1f, 1f, 1f) });

            MaterialPropertyBlock recorded = live.Snapshot();

            live.SetFloat("_U6_Snap", 2f);
            live.SetVectorArray("_U6_SnapArr", new[] { new Vector4(2f, 2f, 2f, 2f) });
            live.Clear();

            Assert.That(recorded.GetFloat("_U6_Snap"), Is.EqualTo(1f));
            Assert.That(recorded.GetVectorArray("_U6_SnapArr")[0], Is.EqualTo(new Vector4(1f, 1f, 1f, 1f)));
        }

        [Test]
        public void PropertyBlock_SnapshotIntoReusesTheDestination()
        {
            MaterialPropertyBlock live = new MaterialPropertyBlock();
            live.SetFloat("_U6_SnapInto", 3f);

            MaterialPropertyBlock pooled = new MaterialPropertyBlock();
            pooled.SetFloat("_U6_Stale", 7f);

            live.SnapshotInto(pooled);

            Assert.That(pooled.GetFloat("_U6_SnapInto"), Is.EqualTo(3f));
            Assert.That(pooled.HasProperty("_U6_Stale"), Is.False);
        }

        // ---------------------------------------------------------------- allocation (design §1.2, §7.5)

        [Test]
        public void RepeatedScalarSetters_AllocateNothing()
        {
            Material material = new Material((Shader)null);
            int floatID = Shader.PropertyToID("_U6_AllocFloat");
            int vectorID = Shader.PropertyToID("_U6_AllocVector");
            int intID = Shader.PropertyToID("_U6_AllocInt");
            int matrixID = Shader.PropertyToID("_U6_AllocMatrix");
            int textureID = Shader.PropertyToID("_U6_AllocTexture");

            // Warm the dictionaries: the first insert of a key is allowed to grow the buckets.
            SetAllScalars(material, floatID, vectorID, intID, matrixID, textureID);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++)
                SetAllScalars(material, floatID, vectorID, intID, matrixID, textureID);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        [Test]
        public void RepeatedVectorArraySetters_AllocateNothingWhenTheLengthIsStable()
        {
            // This is the case the design calls out: NowSdf.cs:4835-4907 pushes nine vector arrays per upload from
            // static 64- and 16-entry scratch buffers, every frame.
            Material material = new Material((Shader)null);
            int id = Shader.PropertyToID("_U6_AllocArray");
            Vector4[] scratch = new Vector4[64];
            List<Vector4> scratchList = new List<Vector4>();
            for (int i = 0; i < 16; i++)
                scratchList.Add(Vector4.zero);
            int listID = Shader.PropertyToID("_U6_AllocArrayList");

            material.SetVectorArray(id, scratch);
            material.SetVectorArray(listID, scratchList);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                material.SetVectorArray(id, scratch);
                material.SetVectorArray(listID, scratchList);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        [Test]
        public void PropertyBlockClearAndRefill_AllocatesNothing_TheDesignSteadyStateFrame()
        {
            // NowMaskShader's shared block is cleared and refilled every frame (NowMaskShader.cs:314-445).
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            int floatID = Shader.PropertyToID("_U6_BlockAllocFloat");
            int arrayID = Shader.PropertyToID("_U6_BlockAllocArray");
            Vector4[] scratch = new Vector4[8];

            for (int warm = 0; warm < 2; warm++)
            {
                block.Clear();
                block.SetFloat(floatID, 1f);
                block.SetVectorArray(arrayID, scratch);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++)
            {
                block.Clear();
                block.SetFloat(floatID, i);
                block.SetVectorArray(arrayID, scratch);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            // Design §7.5's allocation row gates exactly this cycle at zero bytes. It only holds because Clear()
            // retires the array buffers into the bag's recycle cache instead of dropping them.
            Assert.That(after - before, Is.Zero);
        }

        [Test]
        public void RepeatedCopyPropertiesFromMaterial_AllocatesNothingWhenTheShapeIsStable()
        {
            Material source = new Material((Shader)null);
            source.SetFloat("_U6_CopyAllocFloat", 1f);
            source.SetVectorArray("_U6_CopyAllocArray", new Vector4[8]);

            Material destination = new Material((Shader)null);
            destination.CopyPropertiesFromMaterial(source);
            destination.CopyPropertiesFromMaterial(source);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++)
                destination.CopyPropertiesFromMaterial(source);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero,
                "CopyFrom must reconcile the array dictionaries rather than clearing and reallocating them");
        }

        private static void SetAllScalars(Material material, int floatID, int vectorID, int intID, int matrixID, int textureID)
        {
            material.SetFloat(floatID, 1f);
            material.SetVector(vectorID, new Vector4(1f, 2f, 3f, 4f));
            material.SetInteger(intID, 3);
            material.SetMatrix(matrixID, Matrix4x4.identity);
            material.SetTexture(textureID, null);
        }
    }
}
