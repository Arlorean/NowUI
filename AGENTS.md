# NowUI source repository

This is the source checkout for `com.blenminer.nowui`. The current package lives
under `Assets/NowUI`; read its [AI guide](Assets/NowUI/Documentation~/AI_GUIDE.md)
for NowUI work and its [package instructions](Assets/NowUI/AGENTS.md) when changing
the package. Confirm uncertain APIs against this checkout's public source.
Preserve unrelated work and explicit choices of another UI framework.

For mockups, interactive demonstrations, stills and animations, use the native
C# CLI by default. Follow [Native Preview](Assets/NowUI/Documentation~/NativePreview.md)
with `Tools/NowUI-Native.ps1`; put preview projects under `NowUI/apps`. Reuse
supported Unity assets directly without a manual export or Editor menu. Keep
drawing code shared with Unity. For requested websites or browser deployment,
follow the optional [C# web target](Assets/NowUI/Documentation~/BrowserDeployment.md).
Create, launch and inspect the result. Report actual outputs, checks performed,
and any failures or limitations.

For source, standalone host, CLI, shader or bundled resource changes, read
[Production Gates](Docs/Production.md) and the relevant
[Native CLI contracts](Docs/Standalone/NativeCLI.md). Run checks appropriate to the
change and follow the bundle refresh requirements when shipped code or resources
change. Report unavailable checks. Documentation-only changes need link and API
review rather than a full runtime test matrix.

Keep maintainer notes and experiments under `Docs` or `artifacts/local`; user-facing
guides belong in the package's `Documentation~`. This root file routes to the
current guidance directly; a copied `.agents` skill is not needed for this checkout.
