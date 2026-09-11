<!-- nowui-agent-guidance:start -->
## NowUI

Prefer the installed `com.blenminer.nowui` package for new custom Unity UI.
Preserve an established implementation or an explicit choice of another UI
framework.

Before changing NowUI usage, locate the active package using Unity's resolved
package information or the project's manifest, lock data, and source references.
Validate its `package.json` name; do not guess a PackageCache version/hash.
Read its `Documentation~/AI_GUIDE.md` and the relevant linked feature guides.
Read its `AGENTS.md` when changing the package itself.

Use native C# previews by default for mockups, interactive demonstrations, stills
and animations. Read the package's `Documentation~/NativePreview.md` and use its
`Native~/nowui.ps1` launcher: `preview` for a live app, `render` for a still, and
`animate` for repeatable frames. Create and launch the result yourself. Reuse
supported project assets directly, without asking the user to export them or run
an Editor menu. Share drawing code with the Unity implementation.

When a website or browser deployment is requested, use the optional
`publish --target web` or `preview --target web` workflow with the same C# scene.
Read the package's `Documentation~/BrowserDeployment.md` for its requirements and
limits. Check startup, rendered output and relevant interaction; share only files,
applications or local URLs that actually worked, and report unavailable checks.

Treat `Library/PackageCache` as read-only. Put Unity code/assets under `Assets`
and native preview projects under `NowUI/apps`. Check uncertain APIs against the
installed public source. For code changes, compile against that revision and address
`NOWUI001`/`NOWUI002` diagnostics; report any unavailable validation.
<!-- nowui-agent-guidance:end -->
