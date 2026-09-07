// Assembly-level test policy for the engine-free run of NowUI's Unity test suite.
//
// Every NowUI subsystem is a process-global static (NowInput, NowFocus, NowOverlay, NowControlState, the theme, the
// default font, the draw-list pools). Unity runs EditMode tests sequentially on the main thread, and these tests were
// written for that: they reset statics in [SetUp] and expect nobody else to touch them meanwhile. Parallel fixtures
// would interleave those statics and produce failures that say nothing about the shim, so parallelism is off - both by
// NonParallelizable (the default policy for everything in the assembly) and by a worker count of one.
//
// NowStandaloneFrame is the per-test action: one Time.frameCount advance per test plus Unity's log policy. See
// Support/NowStandaloneTestHost.cs for why it lives at the assembly level.
using NUnit.Framework;

[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]
[assembly: NowStandaloneFrame]
