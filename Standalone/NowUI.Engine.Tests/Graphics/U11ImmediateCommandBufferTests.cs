// Tests for the U11 immediate-path unit: NowImmediate, UnityEngine.GL, UnityEngine.Graphics,
// UnityEngine.Rendering.CommandBuffer and NowCommandOp.
// Design: Docs/Standalone/StandaloneCoreDesign.md §4.2 (the state machine and Resolve), §4.3 (CommandBuffer and its
// replay rules), §4.1 (the backend contract; invariants 5, 6 and 7 are asserted by name below), §3.5 (GL.cs and
// Graphics.cs member lists, "Blit leaves the destination bound"), §7.5 (the "Immediate path" row of the semantics
// suite: replay order, temp-RT resolution, unreleased temporaries, the two invariants, and
// Graphics.ExecuteCommandBuffer restoring the previous target).
//
// Every assertion reads the recording backend's op log rather than the shim's own fields wherever it can. That is
// the point of the log: it is what a WebGL2 backend will be diffed against in M2, so a test that reads it is testing
// the thing M2 depends on.
using System;
using System.Collections.Generic;
using NowUI.Engine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class U11ImmediateCommandBufferTests
    {
        private const int ScreenWidth = 800;
        private const int ScreenHeight = 600;

        private NowRecordingRenderBackend m_Recorder;
        private ImmediateHost m_Host;

        [SetUp]
        public void SetUp()
        {
            m_Recorder = new NowRecordingRenderBackend();
            m_Host = new ImmediateHost(ScreenWidth, ScreenHeight);

            NowRuntime.Initialize(m_Host, m_Recorder);
            NowImmediate.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            NowImmediate.Reset();
            NowRuntime.profilerSink = null;

            // Passing null for both restores the defaults the static constructor installed, so a fixture that runs
            // after this one is not left talking to a recorder that has gone out of scope.
            NowRuntime.Initialize(null, null);
        }

        // ------------------------------------------------------------------------------------------------ fixtures

        private static Shader MakeShader(string name)
        {
            return new Shader(name, new NowShaderInfo(name, 1));
        }

        private static Material MakeMaterial(string shaderName)
        {
            return new Material(MakeShader(shaderName));
        }

        private static Mesh MakeMesh()
        {
            Mesh mesh = new Mesh();
            mesh.subMeshCount = 1;
            return mesh;
        }

        /// <summary>A created target, with the creation already dropped from the log so a test reads only its own ops.</summary>
        private RenderTexture MakeTarget(int width, int height)
        {
            RenderTexture rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            m_Recorder.Clear();
            return rt;
        }

        /// <summary>The op names in call order, with their arguments stripped - what "replay order" means.</summary>
        private List<string> OpNames()
        {
            List<string> names = new List<string>(m_Recorder.ops.Count);

            for (int i = 0; i < m_Recorder.ops.Count; i++)
            {
                string line = m_Recorder.ops[i];
                int paren = line.IndexOf('(');
                names.Add(paren < 0 ? line : line.Substring(0, paren));
            }

            return names;
        }

        private string Op(int index)
        {
            return m_Recorder.ops[index];
        }

        // -------------------------------------------------------------------------------- NowImmediate: bind state

        [Test]
        public void SetActive_Null_BindsTheBackBufferAtTheHostScreenSize()
        {
            RenderTexture.active = null;

            Assert.That(RenderTexture.active, Is.Null);
            Assert.That(OpNames(), Is.EqualTo(new[] { "SetRenderTarget", "SetViewport" }));
            Assert.That(Op(0), Does.Contain("backbuffer"));
            Assert.That(Op(0), Does.Contain("800x600"));
            Assert.That(Op(1), Is.EqualTo("SetViewport((0, 0, 800, 600))"));
        }

        [Test]
        public void SetActive_RenderTexture_BindsItAndSetsTheFullViewport()
        {
            RenderTexture rt = MakeTarget(64, 32);

            RenderTexture.active = rt;

            Assert.That(RenderTexture.active, Is.SameAs(rt));
            Assert.That(OpNames(), Is.EqualTo(new[] { "SetRenderTarget", "SetViewport" }));
            Assert.That(Op(0), Does.Contain("64x32"));
            Assert.That(Op(1), Is.EqualTo("SetViewport((0, 0, 64, 32))"));
        }

        [Test]
        public void SetActive_CreatesAnUncreatedTarget_BecauseCreationIsLazy()
        {
            RenderTexture rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
            m_Recorder.Clear();

            Assert.That(rt.IsCreated(), Is.False);

            RenderTexture.active = rt;

            Assert.That(rt.IsCreated(), Is.True);
            Assert.That(OpNames(), Is.EqualTo(new[] { "CreateRenderTexture", "SetRenderTarget", "SetViewport" }));
        }

        [Test]
        public void Binding_A_Target_BumpsItsUpdateCount()
        {
            RenderTexture rt = MakeTarget(8, 8);
            uint before = rt.updateCount;

            RenderTexture.active = rt;

            // NowSdf treats updateCount as its staleness signal (hazard D.1 #11), and a target that has just been
            // bound for drawing is exactly what "stale" has to mean.
            Assert.That(rt.updateCount, Is.EqualTo(before + 1u));
        }

        [Test]
        public void SetRenderTarget_IsAlwaysFollowedBySetViewport()
        {
            RenderTexture a = MakeTarget(32, 16);
            RenderTexture b = MakeTarget(8, 8);

            RenderTexture.active = a;
            RenderTexture.active = b;
            RenderTexture.active = null;

            AssertViewportFollowsEveryRenderTarget();
        }

        // ------------------------------------------------------------------------------------- NowImmediate: Reset

        [Test]
        public void Reset_ClearsTargetMatricesPassAndToggles()
        {
            RenderTexture rt = MakeTarget(8, 8);
            RenderTexture.active = rt;
            GL.LoadProjectionMatrix(Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            GL.modelview = Matrix4x4.Scale(new Vector3(2f, 2f, 2f));
            GL.invertCulling = true;
            GL.sRGBWrite = true;
            MakeMaterial("NowUI/Reset").SetPass(0);

            NowImmediate.Reset();

            Assert.That(RenderTexture.active, Is.Null);
            Assert.That(GL.modelview, Is.EqualTo(Matrix4x4.identity));
            Assert.That(NowImmediate.projection, Is.EqualTo(Matrix4x4.identity));
            Assert.That(NowImmediate.activePass.material, Is.Null);
            Assert.That(NowImmediate.activePass.pass, Is.EqualTo(0));
            Assert.That(GL.invertCulling, Is.False);
            Assert.That(GL.sRGBWrite, Is.False);
        }

        // --------------------------------------------------------------------------------------------------- GL

        [Test]
        public void GL_PushPopMatrix_RestoresBothMatricesTogether()
        {
            Matrix4x4 modelView = Matrix4x4.Translate(new Vector3(1f, 2f, 3f));
            Matrix4x4 projection = Matrix4x4.Ortho(0f, 4f, 0f, 4f, -1f, 100f);

            GL.modelview = modelView;
            GL.LoadProjectionMatrix(projection);

            GL.PushMatrix();
            GL.LoadIdentity();
            GL.LoadProjectionMatrix(Matrix4x4.zero);

            Assert.That(GL.modelview, Is.EqualTo(Matrix4x4.identity));
            Assert.That(NowImmediate.projection, Is.EqualTo(Matrix4x4.zero));

            GL.PopMatrix();

            Assert.That(GL.modelview, Is.EqualTo(modelView));
            Assert.That(NowImmediate.projection, Is.EqualTo(projection));
        }

        [Test]
        public void GL_PushMatrix_Nests()
        {
            GL.modelview = Matrix4x4.Translate(new Vector3(1f, 0f, 0f));
            GL.PushMatrix();
            GL.modelview = Matrix4x4.Translate(new Vector3(2f, 0f, 0f));
            GL.PushMatrix();
            GL.modelview = Matrix4x4.Translate(new Vector3(3f, 0f, 0f));

            GL.PopMatrix();
            Assert.That(GL.modelview, Is.EqualTo(Matrix4x4.Translate(new Vector3(2f, 0f, 0f))));

            GL.PopMatrix();
            Assert.That(GL.modelview, Is.EqualTo(Matrix4x4.Translate(new Vector3(1f, 0f, 0f))));
        }

        [Test]
        public void GL_PopMatrix_WithoutPush_LogsAnErrorAndLeavesTheMatricesAlone()
        {
            Matrix4x4 modelView = Matrix4x4.Scale(new Vector3(3f, 3f, 3f));
            GL.modelview = modelView;
            m_Host.logMessages.Clear();

            GL.PopMatrix();

            // An unmatched pop inside a paint routine must not take the frame down; Unity reports it and carries on.
            Assert.That(GL.modelview, Is.EqualTo(modelView));
            Assert.That(m_Host.logMessages.Count, Is.EqualTo(1));
            Assert.That(m_Host.logMessages[0], Does.Contain("PopMatrix"));
        }

        [Test]
        public void GL_MultMatrix_RightMultipliesTheModelview()
        {
            Matrix4x4 first = Matrix4x4.Translate(new Vector3(1f, 0f, 0f));
            Matrix4x4 second = Matrix4x4.Scale(new Vector3(2f, 2f, 2f));

            GL.modelview = first;
            GL.MultMatrix(second);

            Assert.That(GL.modelview, Is.EqualTo(first * second));
        }

        [Test]
        public void GL_Clear_ReachesTheBackendWithUnitysDefaultDepthOfOne()
        {
            GL.Clear(true, true, new Color(0.25f, 0.5f, 0.75f, 1f));

            Assert.That(OpNames(), Is.EqualTo(new[] { "ClearRenderTarget" }));
            Assert.That(Op(0), Does.Contain("z=1"));
            Assert.That(Op(0), Does.Contain("rgba(0.25, 0.5, 0.75, 1)"));
        }

        [Test]
        public void GL_Clear_TakesAnExplicitDepth()
        {
            GL.Clear(true, false, Color.black, 0.5f);

            Assert.That(Op(0), Does.Contain("depth=true"));
            Assert.That(Op(0), Does.Contain("color=false"));
            Assert.That(Op(0), Does.Contain("z=0.5"));
        }

        [Test]
        public void GL_Viewport_SetsTheViewportWithoutRebinding()
        {
            GL.Viewport(new Rect(4f, 8f, 16f, 32f));

            Assert.That(OpNames(), Is.EqualTo(new[] { "SetViewport" }));
            Assert.That(Op(0), Is.EqualTo("SetViewport((4, 8, 16, 32))"));
        }

        [Test]
        public void GL_GetGPUProjectionMatrix_IsAnIdentityTransform()
        {
            Matrix4x4 projection = Matrix4x4.Ortho(0f, 800f, -600f, 0f, -1f, 100f);

            // The backend owns clip-space conventions (design §3.5, §4.7); folding one in here would apply it twice.
            Assert.That(GL.GetGPUProjectionMatrix(projection, true), Is.EqualTo(projection));
            Assert.That(GL.GetGPUProjectionMatrix(projection, false), Is.EqualTo(projection));
        }

        [Test]
        public void GL_LoadPixelMatrix_UsesTheBoundTargetSize()
        {
            RenderTexture rt = MakeTarget(40, 20);
            RenderTexture.active = rt;

            GL.LoadPixelMatrix();

            Assert.That(NowImmediate.projection, Is.EqualTo(Matrix4x4.Ortho(0f, 40f, 0f, 20f, -1f, 100f)));
            Assert.That(GL.modelview, Is.EqualTo(Matrix4x4.identity));
        }

        [Test]
        public void GL_LoadOrtho_MapsTheUnitSquare()
        {
            GL.LoadOrtho();

            Assert.That(NowImmediate.projection, Is.EqualTo(Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 100f)));
        }

        // --------------------------------------------------------------------------------------------- Graphics

        [Test]
        public void DrawMeshNow_UsesTheMaterialSetPassSelected_AndPushesTheViewProjectionFirst()
        {
            Mesh mesh = MakeMesh();
            Material material = MakeMaterial("NowUI/UI Rectangle");
            Matrix4x4 view = Matrix4x4.Translate(new Vector3(0f, 0f, -5f));
            Matrix4x4 projection = Matrix4x4.Ortho(0f, 800f, -600f, 0f, -1f, 100f);

            GL.modelview = view;
            GL.LoadProjectionMatrix(projection);
            material.SetPass(0);
            m_Recorder.Clear();

            Graphics.DrawMeshNow(mesh, Matrix4x4.identity);

            // Invariant 6: every draw is preceded by at least one SetViewProjection.
            Assert.That(OpNames(), Is.EqualTo(new[] { "SetViewProjection", "DrawMesh" }));
            Assert.That(Op(1), Does.Contain("material=mat0"));
            Assert.That(Op(1), Does.Contain("subMesh=0"));
            Assert.That(Op(1), Does.Contain("properties=none"));
            Assert.That(m_Recorder.inner.meshDraws, Is.EqualTo(1));
            Assert.That(m_Recorder.inner.lastView, Is.EqualTo(view));
            Assert.That(m_Recorder.inner.lastProjection, Is.EqualTo(projection));
        }

        [Test]
        public void DrawMeshNow_PassesTheSubMeshIndexThrough()
        {
            Mesh mesh = new Mesh();
            mesh.subMeshCount = 3;
            MakeMaterial("NowUI/UI Rectangle").SetPass(2);
            m_Recorder.Clear();

            Graphics.DrawMeshNow(mesh, Matrix4x4.identity, 2);

            Assert.That(Op(1), Does.Contain("subMesh=2"));
            Assert.That(Op(1), Does.Contain("pass=2"));
        }

        [Test]
        public void DrawMeshNow_PositionRotation_BuildsATrsMatrixWithUnitScale()
        {
            Mesh mesh = MakeMesh();
            MakeMaterial("NowUI/UI Rectangle").SetPass(0);
            m_Recorder.Clear();

            Vector3 position = new Vector3(1f, 2f, 3f);
            Quaternion rotation = Quaternion.Euler(0f, 90f, 0f);

            Graphics.DrawMeshNow(mesh, position, rotation);

            NullRenderBackend inner = m_Recorder.inner;
            inner.recordDraws = true;
            m_Recorder.Clear();
            Graphics.DrawMeshNow(mesh, position, rotation);

            Assert.That(inner.recordedDrawCount, Is.EqualTo(1));
            Assert.That(inner.GetDrawRecord(0).model, Is.EqualTo(Matrix4x4.TRS(position, rotation, Vector3.one)));
        }

        [Test]
        public void Blit_LeavesTheDestinationBound()
        {
            RenderTexture source = MakeTarget(8, 8);
            RenderTexture destination = MakeTarget(16, 16);
            RenderTexture.active = source;
            m_Recorder.Clear();

            Graphics.Blit(source, destination);

            // Invariant 7 and Unity's convention: NowSdfImageField saves and restores RenderTexture.active around
            // every blit precisely because the destination stays bound.
            Assert.That(RenderTexture.active, Is.SameAs(destination));

            // The implicit bind is NOT a SetRenderTarget call, so no viewport follows it and invariant 5 is untouched.
            Assert.That(OpNames(), Is.EqualTo(new[] { "Blit" }));
            Assert.That(Op(0), Does.Contain("material=none"));
            Assert.That(Op(0), Does.Contain("scale=(1, 1)"));
            Assert.That(Op(0), Does.Contain("offset=(0, 0)"));
        }

        [Test]
        public void Blit_WithAMaterial_DefaultsToPassMinusOne()
        {
            RenderTexture source = MakeTarget(8, 8);
            RenderTexture destination = MakeTarget(8, 8);
            Material material = MakeMaterial("NowUI/Blur");
            m_Recorder.Clear();

            Graphics.Blit(source, destination, material);

            Assert.That(Op(0), Does.Contain("material=mat0"));
            Assert.That(Op(0), Does.Contain("pass=-1"));
        }

        [Test]
        public void Blit_ScaleAndOffset_ReachTheBackend()
        {
            RenderTexture source = MakeTarget(8, 8);
            RenderTexture destination = MakeTarget(8, 8);
            m_Recorder.Clear();

            Graphics.Blit(source, destination, new Vector2(0.5f, 0.25f), new Vector2(0.125f, 0.75f));

            Assert.That(Op(0), Does.Contain("scale=(0.5, 0.25)"));
            Assert.That(Op(0), Does.Contain("offset=(0.125, 0.75)"));
        }

        [Test]
        public void Blit_WithoutADestination_TargetsWhateverIsBound()
        {
            RenderTexture source = MakeTarget(8, 8);
            RenderTexture destination = MakeTarget(16, 16);
            Material material = MakeMaterial("NowUI/Blur");
            RenderTexture.active = destination;
            m_Recorder.Clear();

            Graphics.Blit(source, material, 1);

            Assert.That(Op(0), Does.Contain("destination=rt"));
            Assert.That(Op(0), Does.Contain("16x16"));
            Assert.That(Op(0), Does.Contain("pass=1"));
            Assert.That(RenderTexture.active, Is.SameAs(destination));
        }

        [Test]
        public void Blit_ToTheBackBuffer_UsesTheHostScreenSize()
        {
            RenderTexture source = MakeTarget(8, 8);
            m_Recorder.Clear();

            Graphics.Blit(source, (RenderTexture)null);

            Assert.That(Op(0), Does.Contain("destination=backbuffer"));
            Assert.That(Op(0), Does.Contain("800x600"));
            Assert.That(RenderTexture.active, Is.Null);
        }

        [Test]
        public void CopyTexture_ReachesTheBackend()
        {
            RenderTexture source = MakeTarget(4, 4);
            RenderTexture destination = MakeTarget(4, 4);
            m_Recorder.Clear();

            Graphics.CopyTexture(source, destination);

            Assert.That(OpNames(), Is.EqualTo(new[] { "CopyTexture" }));
            Assert.That(m_Recorder.inner.textureCopies, Is.EqualTo(1));
        }

        [Test]
        public void SetRenderTarget_ByIdentifier_ResolvesCameraTargetToTheBackBuffer()
        {
            RenderTexture rt = MakeTarget(8, 8);
            RenderTexture.active = rt;
            m_Recorder.Clear();

            Graphics.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));

            Assert.That(RenderTexture.active, Is.Null);
            Assert.That(Op(0), Does.Contain("backbuffer"));
            Assert.That(Op(1), Is.EqualTo("SetViewport((0, 0, 800, 600))"));
        }

        // ------------------------------------------------------------------------------- CommandBuffer: recording

        [Test]
        public void CommandBuffer_Name_DefaultsAndRoundTrips()
        {
            using (CommandBuffer buffer = new CommandBuffer())
            {
                Assert.That(buffer.name, Is.EqualTo("Unnamed command buffer"));

                buffer.name = "NowUI Glass";
                Assert.That(buffer.name, Is.EqualTo("NowUI Glass"));
            }
        }

        [Test]
        public void CommandBuffer_Clear_DropsOpsAndSizeGoesToZero()
        {
            CommandBuffer buffer = new CommandBuffer();
            buffer.SetViewport(new Rect(0f, 0f, 8f, 8f));
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);

            Assert.That(buffer.opCount, Is.EqualTo(2));
            Assert.That(buffer.sizeInBytes, Is.GreaterThan(0));

            buffer.Clear();

            Assert.That(buffer.opCount, Is.EqualTo(0));
            Assert.That(buffer.sizeInBytes, Is.EqualTo(0));
        }

        [Test]
        public void CommandBuffer_Release_MakesEveryLaterUseThrow()
        {
            CommandBuffer buffer = new CommandBuffer();
            buffer.SetViewport(new Rect(0f, 0f, 1f, 1f));
            buffer.Release();

            Assert.That(buffer.released, Is.True);
            Assert.Throws<ObjectDisposedException>(() => buffer.SetViewport(new Rect(0f, 0f, 1f, 1f)));
            Assert.Throws<ObjectDisposedException>(() => buffer.Clear());
            Assert.Throws<ObjectDisposedException>(() => Graphics.ExecuteCommandBuffer(buffer));

            // Releasing twice is silent: a Dispose after an explicit Release is a normal shape.
            Assert.DoesNotThrow(() => buffer.Release());
            Assert.DoesNotThrow(() => buffer.Dispose());
        }

        [Test]
        public void CommandBuffer_Dispose_Releases()
        {
            CommandBuffer buffer = new CommandBuffer();

            using (buffer)
            {
                buffer.SetViewport(new Rect(0f, 0f, 1f, 1f));
            }

            Assert.That(buffer.released, Is.True);
        }

        // ---------------------------------------------------------------------------------- CommandBuffer: replay

        [Test]
        public void Replay_RunsEveryOpInRecordedOrder()
        {
            RenderTexture target = MakeTarget(64, 64);
            Mesh mesh = MakeMesh();
            Material material = MakeMaterial("NowUI/UI Rectangle");

            CommandBuffer buffer = new CommandBuffer();
            buffer.name = "order";
            buffer.SetRenderTarget(target);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            buffer.ClearRenderTarget(true, true, Color.clear);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            buffer.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 6);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(OpNames(), Is.EqualTo(new[]
            {
                "SetRenderTarget",
                "SetViewport",
                "SetViewProjection",
                "ClearRenderTarget",
                "DrawMesh",
                "DrawProcedural",
                // ExecuteCommandBuffer restores what was bound before it ran (design §4.3).
                "SetRenderTarget",
                "SetViewport",
            }));
        }

        [Test]
        public void Replay_SetRenderTarget_IsAlwaysFollowedBySetViewport()
        {
            RenderTexture a = MakeTarget(32, 32);
            RenderTexture b = MakeTarget(8, 4);

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetRenderTarget(a);
            buffer.SetRenderTarget(b);
            buffer.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            AssertViewportFollowsEveryRenderTarget();
        }

        [Test]
        public void Replay_SetRenderTarget_SetsTheViewportToTheWholeTarget()
        {
            RenderTexture target = MakeTarget(48, 24);

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetRenderTarget(target);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(Op(1), Is.EqualTo("SetViewport((0, 0, 48, 24))"));
        }

        [Test]
        public void Replay_ExplicitViewport_SurvivesTheBind()
        {
            RenderTexture target = MakeTarget(48, 24);

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetRenderTarget(target);
            buffer.SetViewport(new Rect(4f, 4f, 8f, 8f));

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(Op(1), Is.EqualTo("SetViewport((0, 0, 48, 24))"));
            Assert.That(Op(2), Is.EqualTo("SetViewport((4, 4, 8, 8))"));
        }

        [Test]
        public void Replay_EveryDrawIsPrecededByAViewProjection_EvenWhenTheBufferNeverSetOne()
        {
            Mesh mesh = MakeMesh();
            Material material = MakeMaterial("NowUI/UI Rectangle");
            Matrix4x4 view = Matrix4x4.Translate(new Vector3(0f, 0f, -1f));
            Matrix4x4 projection = Matrix4x4.Ortho(0f, 8f, 0f, 8f, -1f, 100f);

            GL.modelview = view;
            GL.LoadProjectionMatrix(projection);

            CommandBuffer buffer = new CommandBuffer();
            buffer.DrawMesh(mesh, Matrix4x4.identity, material);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            // Invariant 6 holds, and exactly one SetViewProjection is emitted: a buffer with no matrices of its own
            // inherits the immediate path's, and it is pushed once rather than once per draw.
            Assert.That(OpNames(), Is.EqualTo(new[] { "SetViewProjection", "DrawMesh", "DrawMesh" }));
            Assert.That(m_Recorder.inner.lastView, Is.EqualTo(view));
            Assert.That(m_Recorder.inner.lastProjection, Is.EqualTo(projection));
        }

        [Test]
        public void Replay_ABufferThatSetsItsOwnMatrices_GetsNoExtraViewProjection()
        {
            Mesh mesh = MakeMesh();
            Material material = MakeMaterial("NowUI/UI Rectangle");

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(OpNames(), Is.EqualTo(new[] { "SetViewProjection", "DrawMesh" }));
        }

        [Test]
        public void Replay_DrawMesh_DefaultsToSubMeshZeroAndPassMinusOne()
        {
            Mesh mesh = MakeMesh();
            Material material = MakeMaterial("NowUI/UI Rectangle");

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(Op(1), Does.Contain("subMesh=0"));
            Assert.That(Op(1), Does.Contain("pass=-1"));
        }

        [Test]
        public void Replay_DrawProcedural_CarriesTopologyVertexAndInstanceCounts()
        {
            Material material = MakeMaterial("NowUI/Procedural");

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            buffer.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Lines, 12, 4);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(Op(1), Does.Contain("topology=Lines"));
            Assert.That(Op(1), Does.Contain("vertices=12"));
            Assert.That(Op(1), Does.Contain("instances=4"));
            Assert.That(Op(1), Does.Contain("pass=1"));
        }

        // ------------------------------------------------------------------- CommandBuffer: property-block snapshot

        [Test]
        public void DrawMesh_SnapshotsThePropertyBlock_SoASharedBlockCanBeMutatedBetweenBatches()
        {
            Mesh mesh = MakeMesh();
            Material material = MakeMaterial("NowUI/Mask");
            int id = Shader.PropertyToID("_U11MaskRect");

            // This is NowMaskShader's shape: one block, handed to consecutive DrawMesh calls, mutated between them.
            MaterialPropertyBlock shared = new MaterialPropertyBlock();

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);

            shared.SetFloat(id, 1f);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, shared);

            shared.SetFloat(id, 2f);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, shared);

            NullRenderBackend inner = m_Recorder.inner;
            List<MaterialPropertyBlock> seen = new List<MaterialPropertyBlock>();
            RecordingBlockBackend blockRecorder = new RecordingBlockBackend(inner, seen);

            NowRuntime.Initialize(m_Host, blockRecorder);
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(seen.Count, Is.EqualTo(2));
            Assert.That(seen[0], Is.Not.SameAs(shared));
            Assert.That(seen[1], Is.Not.SameAs(shared));
            Assert.That(seen[0], Is.Not.SameAs(seen[1]));
            Assert.That(seen[0].GetFloat(id), Is.EqualTo(1f));
            Assert.That(seen[1].GetFloat(id), Is.EqualTo(2f));

            // And after the replay the caller's own block still holds what the caller last put in it.
            Assert.That(shared.GetFloat(id), Is.EqualTo(2f));
        }

        [Test]
        public void DrawMesh_WithNoBlock_PassesNull()
        {
            Mesh mesh = MakeMesh();
            Material material = MakeMaterial("NowUI/UI Rectangle");

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, null);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(Op(1), Does.Contain("properties=none"));
        }

        // ----------------------------------------------------------------------------- CommandBuffer: temporaries

        [Test]
        public void GetTemporaryRT_ResolvesByNameIdForLaterOps()
        {
            int id = Shader.PropertyToID("_U11Temp");

            CommandBuffer buffer = new CommandBuffer();
            buffer.GetTemporaryRT(id, 32, 16, 0, FilterMode.Point, RenderTextureFormat.ARGB32);
            buffer.SetRenderTarget(id);
            buffer.ReleaseTemporaryRT(id);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            List<string> names = OpNames();

            // The temporary is a real RenderTexture by the time the backend sees it: a backend never sees a nameID
            // (design §4.1 guarantee 4).
            int bind = names.IndexOf("SetRenderTarget");
            Assert.That(bind, Is.GreaterThanOrEqualTo(0));
            Assert.That(Op(bind), Does.Contain("32x16"));
            Assert.That(Op(bind), Does.Not.Contain("backbuffer"));
            Assert.That(names[bind + 1], Is.EqualTo("SetViewport"));
        }

        [Test]
        public void GetTemporaryRT_AppliesTheFilterModeAfterTheAcquire()
        {
            int id = Shader.PropertyToID("_U11Filter");

            CommandBuffer buffer = new CommandBuffer();
            buffer.GetTemporaryRT(id, 8, 8, 0, FilterMode.Point, RenderTextureFormat.ARGB32);
            buffer.SetGlobalTexture(Shader.PropertyToID("_U11FilterTex"), id);
            buffer.ReleaseTemporaryRT(id);

            Graphics.ExecuteCommandBuffer(buffer);

            Texture texture = NowRuntime.globals.GetTexture(Shader.PropertyToID("_U11FilterTex"));
            Assert.That(texture, Is.Not.Null);

            // filterMode is deliberately not part of the pool key, so it has to be applied after the acquire - which
            // is exactly what NowSdfImageField does by hand.
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Point));
        }

        [Test]
        public void ReleaseTemporaryRT_ForAnIdThatWasNeverAllocated_IsIgnored()
        {
            CommandBuffer buffer = new CommandBuffer();
            buffer.ReleaseTemporaryRT(Shader.PropertyToID("_U11Never"));

            Assert.DoesNotThrow(() => Graphics.ExecuteCommandBuffer(buffer));
        }

        [Test]
        public void UnreleasedTemporaries_AreFreedWhenExecutionEnds()
        {
            int a = Shader.PropertyToID("_U11LeakA");
            int b = Shader.PropertyToID("_U11LeakB");

            CommandBuffer buffer = new CommandBuffer();
            buffer.GetTemporaryRT(a, 8, 8, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            buffer.GetTemporaryRT(b, 8, 8, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            buffer.ReleaseTemporaryRT(a);

            int outstandingBefore = NowTemporaryRenderTexturePool.outstandingCount;

            Graphics.ExecuteCommandBuffer(buffer);

            // Unity frees a buffer's temporaries at the end of execution; the shim must too, or the pool holds an
            // entry nobody can name again for the life of the process.
            Assert.That(NowTemporaryRenderTexturePool.outstandingCount, Is.EqualTo(outstandingBefore));
        }

        [Test]
        public void UnreleasedTemporaries_AreFreedEvenWhenAnOpThrows()
        {
            int id = Shader.PropertyToID("_U11Throwing");

            CommandBuffer buffer = new CommandBuffer();
            buffer.GetTemporaryRT(id, 8, 8, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            // A name id nothing allocated: Resolve throws, and the release walk still has to run.
            buffer.SetRenderTarget(Shader.PropertyToID("_U11Missing"));

            int outstandingBefore = NowTemporaryRenderTexturePool.outstandingCount;

            Assert.Throws<ArgumentException>(() => Graphics.ExecuteCommandBuffer(buffer));
            Assert.That(NowTemporaryRenderTexturePool.outstandingCount, Is.EqualTo(outstandingBefore));
        }

        [Test]
        public void GetTemporaryRT_Twice_UnderTheSameId_ReplacesAndReturnsThePrevious()
        {
            int id = Shader.PropertyToID("_U11Replace");

            CommandBuffer buffer = new CommandBuffer();
            buffer.GetTemporaryRT(id, 8, 8, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            buffer.GetTemporaryRT(id, 16, 16, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            buffer.SetRenderTarget(id);

            int outstandingBefore = NowTemporaryRenderTexturePool.outstandingCount;

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            List<string> names = OpNames();
            int bind = names.IndexOf("SetRenderTarget");

            Assert.That(Op(bind), Does.Contain("16x16"));
            Assert.That(NowTemporaryRenderTexturePool.outstandingCount, Is.EqualTo(outstandingBefore));
        }

        [Test]
        public void GetTemporaryRT_FromADescriptor_UsesIt()
        {
            int id = Shader.PropertyToID("_U11Descriptor");
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(24, 12, RenderTextureFormat.ARGB32, 0);

            CommandBuffer buffer = new CommandBuffer();
            buffer.GetTemporaryRT(id, descriptor, FilterMode.Trilinear);
            buffer.SetRenderTarget(id);
            buffer.ReleaseTemporaryRT(id);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            int bind = OpNames().IndexOf("SetRenderTarget");
            Assert.That(Op(bind), Does.Contain("24x12"));
        }

        // ------------------------------------------------------------------------------------ CommandBuffer: blits

        [Test]
        public void Replay_Blit_ResolvesBothEndsAndLeavesTheDestinationBound()
        {
            int source = Shader.PropertyToID("_U11BlitSource");
            RenderTexture destination = MakeTarget(16, 16);

            CommandBuffer buffer = new CommandBuffer();
            buffer.GetTemporaryRT(source, 8, 8, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            buffer.Blit(source, destination);
            buffer.ReleaseTemporaryRT(source);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            int blit = OpNames().IndexOf("Blit");
            Assert.That(blit, Is.GreaterThanOrEqualTo(0));
            Assert.That(Op(blit), Does.Contain("destination=rt"));
            Assert.That(Op(blit), Does.Contain("16x16"));
            Assert.That(Op(blit), Does.Not.Contain("source=none"));
        }

        [Test]
        public void Replay_Blit_FromATextureHandle_UsesTheHandle()
        {
            Texture2D source = new Texture2D(4, 4);
            RenderTexture destination = MakeTarget(8, 8);

            CommandBuffer buffer = new CommandBuffer();
            buffer.Blit(source, destination);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(Op(0), Does.StartWith("Blit(source=tex0"));
        }

        [Test]
        public void Replay_Blit_CarriesDepthSlices()
        {
            RenderTexture source = MakeTarget(8, 8);
            RenderTexture destination = MakeTarget(8, 8);

            CommandBuffer buffer = new CommandBuffer();
            buffer.Blit(source, destination, new Vector2(1f, 1f), new Vector2(0f, 0f), 2, 3);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(Op(0), Does.Contain("sourceSlice=2"));
            Assert.That(Op(0), Does.Contain("destinationSlice=3"));
        }

        // ---------------------------------------------------------------------------------- CommandBuffer: globals

        [Test]
        public void SetGlobals_LandInTheGlobalsBagAtReplayTime_NotAtRecordTime()
        {
            int floatId = Shader.PropertyToID("_U11GlobalFloat");
            int intId = Shader.PropertyToID("_U11GlobalInt");
            int vectorId = Shader.PropertyToID("_U11GlobalVector");
            int colorId = Shader.PropertyToID("_U11GlobalColor");
            int matrixId = Shader.PropertyToID("_U11GlobalMatrix");
            int textureId = Shader.PropertyToID("_U11GlobalTexture");
            Texture2D texture = new Texture2D(2, 2);

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetGlobalFloat(floatId, 1.5f);
            buffer.SetGlobalInt(intId, 7);
            buffer.SetGlobalVector(vectorId, new Vector4(1f, 2f, 3f, 4f));
            buffer.SetGlobalColor(colorId, new Color(0.1f, 0.2f, 0.3f, 0.4f));
            buffer.SetGlobalMatrix(matrixId, Matrix4x4.identity);
            buffer.SetGlobalTexture(textureId, texture);

            // Recording writes nothing: Unity's globals are device state, and a single-threaded executor sees the ops
            // in order, so a global set inside a buffer must not be visible before the buffer runs.
            Assert.That(NowRuntime.globals.GetFloat(floatId), Is.EqualTo(0f));
            Assert.That(NowRuntime.globals.GetTexture(textureId), Is.Null);

            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(NowRuntime.globals.GetFloat(floatId), Is.EqualTo(1.5f));
            Assert.That(NowRuntime.globals.GetInt(intId), Is.EqualTo(7));
            Assert.That(NowRuntime.globals.GetVector(vectorId), Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
            Assert.That(NowRuntime.globals.GetVector(colorId), Is.EqualTo(new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));
            Assert.That(NowRuntime.globals.GetMatrix(matrixId), Is.EqualTo(Matrix4x4.identity));
            Assert.That(NowRuntime.globals.GetTexture(textureId), Is.SameAs(texture));
        }

        [Test]
        public void SetGlobalTexture_ByIdentifier_ResolvesTheTemporaryAtReplayTime()
        {
            int temporaryId = Shader.PropertyToID("_U11GlobalTempRT");
            int slot = Shader.PropertyToID("_U11GlobalTempSlot");

            CommandBuffer buffer = new CommandBuffer();
            buffer.GetTemporaryRT(temporaryId, 8, 8, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            buffer.SetGlobalTexture(slot, temporaryId);
            buffer.ReleaseTemporaryRT(temporaryId);

            Graphics.ExecuteCommandBuffer(buffer);

            Texture bound = NowRuntime.globals.GetTexture(slot);
            Assert.That(bound, Is.Not.Null);
            Assert.That(bound, Is.InstanceOf<RenderTexture>());
            Assert.That(((RenderTexture)bound).width, Is.EqualTo(8));
        }

        [Test]
        public void SetGlobals_ByName_InternTheSameIdAsPropertyToID()
        {
            CommandBuffer buffer = new CommandBuffer();
            buffer.SetGlobalFloat("_U11ByName", 3f);
            buffer.SetGlobalVector("_U11ByNameVector", new Vector4(9f, 0f, 0f, 0f));

            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(NowRuntime.globals.GetFloat(Shader.PropertyToID("_U11ByName")), Is.EqualTo(3f));
            Assert.That(NowRuntime.globals.GetVector(Shader.PropertyToID("_U11ByNameVector")).x, Is.EqualTo(9f));
        }

        // ---------------------------------------------------------------------------------- CommandBuffer: samples

        [Test]
        public void BeginAndEndSample_ReachTheProfilerSinkAtReplayTime()
        {
            RecordingProfilerSink sink = new RecordingProfilerSink();
            NowRuntime.profilerSink = sink;

            CommandBuffer buffer = new CommandBuffer();
            buffer.BeginSample("NowUI.Glass");
            buffer.EndSample("NowUI.Glass");

            Assert.That(sink.entries.Count, Is.EqualTo(0));

            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(sink.entries, Is.EqualTo(new[] { "begin:NowUI.Glass", "end:NowUI.Glass" }));
        }

        // ----------------------------------------------------------------------------- ExecuteCommandBuffer: restore

        [Test]
        public void ExecuteCommandBuffer_RestoresThePreviouslyActiveTarget()
        {
            RenderTexture outer = MakeTarget(32, 32);
            RenderTexture inner = MakeTarget(8, 8);

            RenderTexture.active = outer;

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetRenderTarget(inner);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            // Now.cs:2158/2170 reads RenderTexture.active straight after ExecuteCommandBuffer and expects its own
            // target back (design §4.3).
            Assert.That(RenderTexture.active, Is.SameAs(outer));
            Assert.That(OpNames(), Is.EqualTo(new[] { "SetRenderTarget", "SetViewport", "SetRenderTarget", "SetViewport" }));
            Assert.That(Op(2), Does.Contain("32x32"));
        }

        [Test]
        public void ExecuteCommandBuffer_ThatNeverRebinds_CostsNoRestore()
        {
            RenderTexture outer = MakeTarget(32, 32);
            RenderTexture.active = outer;

            CommandBuffer buffer = new CommandBuffer();
            buffer.ClearRenderTarget(true, true, Color.black);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(OpNames(), Is.EqualTo(new[] { "ClearRenderTarget" }));
            Assert.That(RenderTexture.active, Is.SameAs(outer));
        }

        [Test]
        public void ExecuteCommandBuffer_RestoresAfterABlitToo()
        {
            RenderTexture outer = MakeTarget(32, 32);
            RenderTexture source = MakeTarget(8, 8);
            RenderTexture destination = MakeTarget(4, 4);

            RenderTexture.active = outer;

            CommandBuffer buffer = new CommandBuffer();
            buffer.Blit(source, destination);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            Assert.That(RenderTexture.active, Is.SameAs(outer));
            Assert.That(OpNames(), Is.EqualTo(new[] { "Blit", "SetRenderTarget", "SetViewport" }));
        }

        [Test]
        public void ExecuteCommandBuffer_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Graphics.ExecuteCommandBuffer(null));
        }

        // --------------------------------------------------------------------------------------- Resolve semantics

        [Test]
        public void Resolve_DefaultIdentifier_MeansTheCurrentTarget()
        {
            RenderTexture rt = MakeTarget(16, 8);
            RenderTexture.active = rt;
            m_Recorder.Clear();

            Graphics.SetRenderTarget(default(RenderTargetIdentifier));

            Assert.That(RenderTexture.active, Is.SameAs(rt));
            Assert.That(Op(0), Does.Contain("16x8"));
        }

        [Test]
        public void Resolve_ATexture2D_AsATarget_Throws()
        {
            Texture2D texture = new Texture2D(4, 4);

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => Graphics.SetRenderTarget(new RenderTargetIdentifier(texture)));

            Assert.That(error.Message, Does.Contain("not a RenderTexture"));
        }

        [Test]
        public void Resolve_AnUnallocatedNameId_Throws()
        {
            CommandBuffer buffer = new CommandBuffer();
            buffer.SetRenderTarget(Shader.PropertyToID("_U11Unallocated"));

            ArgumentException error = Assert.Throws<ArgumentException>(() => Graphics.ExecuteCommandBuffer(buffer));
            Assert.That(error.Message, Does.Contain("_U11Unallocated"));
        }

        [Test]
        public void Resolve_ABuiltinWithNoStandaloneEquivalent_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => Graphics.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.Depth)));
        }

        [Test]
        public void Resolve_CurrentActive_IsTheBackBuffer()
        {
            RenderTexture rt = MakeTarget(8, 8);
            RenderTexture.active = rt;
            m_Recorder.Clear();

            Graphics.SetRenderTarget(new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive));

            Assert.That(Op(0), Does.Contain("backbuffer"));
        }

        [Test]
        public void Resolve_AMipLevel_ShrinksTheViewport()
        {
            RenderTexture rt = MakeTarget(64, 32);

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetRenderTarget(new RenderTargetIdentifier(rt), 2, CubemapFace.Unknown, 0);

            m_Recorder.Clear();
            Graphics.ExecuteCommandBuffer(buffer);

            // Every graphics API floors a mip dimension at 1; 64>>2 = 16 and 32>>2 = 8.
            Assert.That(Op(0), Does.Contain("mip=2"));
            Assert.That(Op(0), Does.Contain("16x8"));
            Assert.That(Op(1), Is.EqualTo("SetViewport((0, 0, 16, 8))"));
        }

        // ------------------------------------------------------------------------------------------- allocation

        [Test]
        public void Replay_OfARecordedBuffer_AllocatesNothing()
        {
            Mesh mesh = MakeMesh();
            Material material = MakeMaterial("NowUI/UI Rectangle");
            RenderTexture target = MakeTarget(16, 16);

            CommandBuffer buffer = new CommandBuffer();
            buffer.SetRenderTarget(target);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            buffer.ClearRenderTarget(true, true, Color.clear);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);

            // The null backend with its draw ring off is the instrument design §7.5 names; the recording backend
            // allocates a string per op by construction and is deliberately not used here.
            NowRuntime.Initialize(m_Host, new NullRenderBackend());

            for (int warmup = 0; warmup < 8; warmup++)
                Graphics.ExecuteCommandBuffer(buffer);

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 32; i++)
                Graphics.ExecuteCommandBuffer(buffer);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero, "CommandBuffer replay must allocate nothing per op (design §1.2, §4.3).");
        }

        [Test]
        public void ReRecordingABuffer_AllocatesNothingOnceItsCapacityIsWarm()
        {
            Mesh mesh = MakeMesh();
            Material material = MakeMaterial("NowUI/UI Rectangle");
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetFloat(Shader.PropertyToID("_U11Alloc"), 1f);

            CommandBuffer buffer = new CommandBuffer();

            for (int warmup = 0; warmup < 8; warmup++)
                Record(buffer, mesh, material, block);

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 32; i++)
                Record(buffer, mesh, material, block);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero,
                "Re-recording must reuse the op list and the pooled property-block snapshots (design §1.2, §4.3).");
        }

        private static void Record(CommandBuffer buffer, Mesh mesh, Material material, MaterialPropertyBlock block)
        {
            buffer.Clear();
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, block);
            buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, block);
        }

        // ------------------------------------------------------------------------------------------------ helpers

        private void AssertViewportFollowsEveryRenderTarget()
        {
            List<string> names = OpNames();

            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] != "SetRenderTarget")
                    continue;

                Assert.That(i + 1, Is.LessThan(names.Count), "SetRenderTarget at " + i + " is the last op.");
                Assert.That(names[i + 1], Is.EqualTo("SetViewport"),
                    "Backend invariant 5: SetRenderTarget is always followed by SetViewport.");
            }
        }

        // ------------------------------------------------------------------------------------------- test doubles

        /// <summary>A host with a known screen size and a logger that keeps what it was told.</summary>
        private sealed class ImmediateHost : INowHostServices, INowLogger
        {
            private readonly NowScreenInfo m_Screen;

            internal readonly List<string> logMessages = new List<string>();

            internal ImmediateHost(int width, int height)
            {
                m_Screen = new NowScreenInfo(width, height, 96f);
            }

            public INowClock clock => new NowStopwatchClock();

            public NowScreenInfo screen => m_Screen;

            public INowLogger logger => this;

            public INowClipboard clipboard => null;

            public INowTouchKeyboard touchKeyboard => null;

            public INowResourceProvider resources => NowEmptyResourceProvider.instance;

            public INowImageDecoder imageDecoder => null;

            public INowFetchProvider fetch => null;

            public RuntimePlatform platform => RuntimePlatform.WindowsPlayer;

            public string persistentDataPath => "";

            public string dataPath => "";

            public string[] layerNames => new string[32];

            public void Log(LogType type, string message, Exception exception, UnityEngine.Object context)
            {
                logMessages.Add(type + ": " + message);
            }
        }

        /// <summary>A backend that keeps the property blocks it was handed, to prove the snapshot really is detached.</summary>
        private sealed class RecordingBlockBackend : INowRenderBackend
        {
            private readonly NullRenderBackend m_Inner;
            private readonly List<MaterialPropertyBlock> m_Blocks;

            internal RecordingBlockBackend(NullRenderBackend inner, List<MaterialPropertyBlock> blocks)
            {
                m_Inner = inner;
                m_Blocks = blocks;
            }

            public NowRenderCaps caps => m_Inner.caps;

            public void BeginFrame(int frameCount) => m_Inner.BeginFrame(frameCount);

            public void EndFrame() => m_Inner.EndFrame();

            public void UploadTexture2D(Texture2D texture, ReadOnlySpan<byte> pixels, RectInt dirtyRect, bool generateMips) =>
                m_Inner.UploadTexture2D(texture, pixels, dirtyRect, generateMips);

            public void UpdateSampler(Texture texture) => m_Inner.UpdateSampler(texture);

            public void ReleaseTexture(Texture texture) => m_Inner.ReleaseTexture(texture);

            public bool CreateRenderTexture(RenderTexture texture, in NowRenderTextureRequest request) =>
                m_Inner.CreateRenderTexture(texture, in request);

            public bool IsRenderTextureLost(RenderTexture texture) => m_Inner.IsRenderTextureLost(texture);

            public void ReleaseRenderTexture(RenderTexture texture) => m_Inner.ReleaseRenderTexture(texture);

            public void ReleaseMesh(Mesh mesh) => m_Inner.ReleaseMesh(mesh);

            public void ReleaseMaterial(Material material) => m_Inner.ReleaseMaterial(material);

            public bool ResolveShader(Shader shader) => m_Inner.ResolveShader(shader);

            public void SetRenderTarget(in NowRenderTarget target) => m_Inner.SetRenderTarget(in target);

            public void SetViewport(in Rect pixelRect) => m_Inner.SetViewport(in pixelRect);

            public void SetViewProjection(in Matrix4x4 view, in Matrix4x4 projection) =>
                m_Inner.SetViewProjection(in view, in projection);

            public void ClearRenderTarget(bool clearDepth, bool clearColor, in Color color, float depth) =>
                m_Inner.ClearRenderTarget(clearDepth, clearColor, in color, depth);

            public void DrawMesh(Mesh mesh, int subMesh, in Matrix4x4 model, Material material, int pass,
                                 MaterialPropertyBlock properties)
            {
                m_Blocks.Add(properties);
                m_Inner.DrawMesh(mesh, subMesh, in model, material, pass, properties);
            }

            public void DrawProcedural(in Matrix4x4 model, Material material, int pass, MeshTopology topology,
                                       int vertexCount, int instanceCount, MaterialPropertyBlock properties)
            {
                m_Blocks.Add(properties);
                m_Inner.DrawProcedural(in model, material, pass, topology, vertexCount, instanceCount, properties);
            }

            public void Blit(Texture source, in NowRenderTarget destination, Material material, int pass,
                             in Vector2 scale, in Vector2 offset, int sourceDepthSlice, int destinationDepthSlice) =>
                m_Inner.Blit(source, in destination, material, pass, in scale, in offset, sourceDepthSlice,
                             destinationDepthSlice);

            public void CopyTexture(Texture source, Texture destination) => m_Inner.CopyTexture(source, destination);
        }

        /// <summary>Records the profiler scopes a replay opens and closes.</summary>
        private sealed class RecordingProfilerSink : INowProfilerSink
        {
            internal readonly List<string> entries = new List<string>();

            public void Begin(string name)
            {
                entries.Add("begin:" + name);
            }

            public void End(string name)
            {
                entries.Add("end:" + name);
            }
        }
    }
}
