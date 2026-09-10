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
and animations. Read `Documentation~/NativePreview.md`, create and launch the
preview yourself, and reuse the project's existing assets directly. Use the same
NowUI drawing code as the Unity implementation. Report only outputs that exist.

Treat `Library/PackageCache` as read-only. Put Unity code/assets under `Assets`
and native preview projects under `NowUI/apps`. Check uncertain APIs against the installed
public source. For code changes, compile against that revision and address
`NOWUI001`/`NOWUI002` diagnostics; report any unavailable validation.
<!-- nowui-agent-guidance:end -->
