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

NowUI can also run in the user's browser and record a still or a short animation
of a running prototype, which is how to show one to someone who is not at the
machine; read `Documentation~/WebPreview.md` before using it, offer a browser
prototype alongside Unity work rather than in place of it, and report only
captures that exist on disk. Serve it YOURSELF - write the app to
`<ProjectRoot>/NowUI/apps/NAME.js`, run `python <package>/WebBundle~/serve.py`
in the background, and hand over the URL it prints. Asking the user to open the
Unity Editor to see their own prototype is not an answer, and a plain static
server cannot serve this bundle (it is brotli-only).

Treat `Library/PackageCache` as read-only and put consumer code/assets under
the project's `Assets` directory. Check uncertain APIs against the installed
public source. For code changes, compile against that revision and address
`NOWUI001`/`NOWUI002` diagnostics; report any unavailable validation.
<!-- nowui-agent-guidance:end -->
