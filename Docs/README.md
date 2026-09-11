# NowUI maintainer documentation

Public, version-matched package documentation lives under
[`Assets/NowUI/Documentation~`](../Assets/NowUI/Documentation~/README.md).

This directory contains repository-only material that must not be treated as
shipped public API:

- [Production gates](Production.md): source-checkout validation, CI, visual
  baselines, allocation gates, and release preparation.
- [Native C# apps and capture](Standalone/NativeCLI.md): live previews, direct
  Unity assets, source reload, and deterministic animation recording.
- [Browser deployment](../Assets/NowUI/Documentation~/BrowserDeployment.md): optional
  static web output for the same C# scenes, with automatic asset provisioning.
- [Native performance](Standalone/NativePerformance.md): measured mesh submission,
  Lottie tessellation, allocations, pixel equivalence, and a Unity comparison.
- [Benchmark guide](Benchmarks.md): feature coverage, repeatable CPU/render runs,
  allocations, cache pressure, timing tails, and known measurement gaps.
- [Expanded feature overview](BenchmarkOverview-2026-09-06.md): three-run results,
  SDF GPU scaling, document/graph hot spots, and corrected allocation instrumentation.
- [Benchmark follow-up](BenchmarkFollowup-2026-09-06.md): indexed graph evaluation,
  clipped rich-text comparisons, verified allocation gates, and resolved test failures.
- [September optimization pass](PerformanceReport-2026-09-06.md): measured
  CPU reductions, allocation checks, visual equivalence, and validation limits.
- [September baseline](PerformanceReport-2026-09-01.md): earlier optimization
  evidence and remaining hot paths.
- [Performance and abuse report](PerformanceReport-2026-08-06.md):
  scaling guidance, dated benchmark evidence, and artifact paths.
- [July performance report](PerformanceReport-2026-07-13.md): prior isolated
  optimization evidence and artifact paths.
- [Transform system design](TransformDesign.md): an internal proposal for
  unshipped work.
- [Touch scroll gesture arbitration](TouchScrollGestureDesign.md): the
  maintainer design for safe child-to-scroll touch capture and flicking.
- [Package footprint plan](PackageFootprintPlan.md): measured packaging cuts
  and the native/font package seams needed for a materially smaller install.
