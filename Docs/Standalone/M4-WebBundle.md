# M4 — Shipping the browser build to Unity users

The goal, in the owner's words: *"all i care for now is for this to be used by users that installed nowui in their
unity project, cant we ship a precompiled wasm library or something?"*

So the audience is a Unity developer who has already installed `com.blenminer.nowui`. They get NowUI running in a
browser, on their own machine, with their own JavaScript, **without a .NET SDK, without emscripten, without a
terminal, and without a network**.

---

## 1. The shape

| piece | where | who builds it |
|---|---|---|
| the precompiled browser build | `Assets/NowUI/WebBundle~/` — committed | a maintainer, on demand |
| the builder | `Tools/Build-NowUIWebBundle.ps1` | — |
| the exclude list | `Tools/Standalone/web-bundle-exclude.txt` | — |
| the Editor server | `Assets/NowUI/Editor/Web/NowWebPreviewServer.cs` | — |
| path resolution | `Assets/NowUI/Editor/Web/NowWebPreviewPaths.cs` | — |
| the window and menu | `Assets/NowUI/Editor/Web/NowWebPreviewWindow.cs` | — |
| **the user's own application** | `<ProjectRoot>/NowUI/apps/<name>.js` | the user |

The trailing `~` on `WebBundle~` is the whole of why committing 8 MB inside the package is safe on the Unity side:
Unity does not import, `.meta`, compile or reimport anything under a `~` folder. It is the same mechanism already
carrying `AI~`, `Analyzers~`, `Documentation~` and `Samples~`.

The user's application lives **outside the package and outside `Assets/`**, for two separate reasons:

* outside the package, because a package manager replaces the package folder wholesale on upgrade — an application
  stored there would be deleted by an update;
* outside `Assets/`, because Unity imports `.js` under `Assets/` as a `TextAsset`, so every save from an external
  editor would cost an AssetDatabase reimport and the authoring loop would be fighting the Editor.

`<ProjectRoot>/NowUI/` is still inside the user's own version control, which is where their source belongs.

---

## 2. Building the bundle

```powershell
pwsh -File Tools/Build-NowUIWebBundle.ps1
```

Measured on this machine, 2026-09-08:

| tree | files | bytes | MiB |
|---|---|---|---|
| `dotnet publish -c Release`, SDK defaults | 191 | 13,733,363 | 13.10 |
| `+ WasmFingerprintAssets=false`, `WasmFingerprintDotnetJs=false`, `CompressionEnabled=false` | 87 | 9,780,373 | 9.33 |
| `+ web-bundle-exclude.txt` → **the shipped bundle** | **87** | **8,489,520** | **8.10** |

Re-measured 2026-09-08 after the W10 drawing surface landed and the bundle was regenerated against it
(`surfaceHash 0x465611D3`, `w9-draw.js` added to the exclude list): **87 payload files, 8,567,275 B (8.17 MiB)**.
A browser's first load of that tree is **53 requests and 8,229,660 B (7.85 MiB)** — the difference is the files
a given page does not ask for. A warm reload is **28 requests and 11,548 B**: everything revalidates on its
`ETag` except the user's own application file, which is served `no-store` on purpose.

`-Compress` produces the same payload with `.br`/`.gz` siblings, for anyone dropping the tree on a real web server.
That variant is not what the Editor serves and not what is committed.

### Why those three properties

* **Fingerprinting off** makes filenames stable. That is what turns each later regeneration from ~5.5 MB of fresh
  git objects (every hashed filename changes on every publish) into ~0.6 MB. Verified across three independent
  publishes in this milestone: `_framework/dotnet.native.wasm` was **1,521,861 B** and
  `_framework/System.Private.CoreLib.wasm` **1,348,373 B** every time, so the framework half of the bundle is paid
  to git exactly once. The NowUI-authored assemblies are *not* byte-stable (`NowUI.Runtime.wasm` moved
  594,197 → 594,709 → 594,709 across builds), which is the ~0.6 MB.
* **Compression off** drops 52 `.br` + 52 `.gz` siblings, ~3.95 MB that buy nothing over a loopback socket at
  memory speed. Freshness is handled by the server's `ETag` instead (§4).

### The exclude list and the size guard

`Tools/Standalone/web-bundle-exclude.txt` currently holds one entry, `nowui-test-large.png` (1,296,705 B — 15% of
the bundle, and its only consumers are two 64 KB byte-cap probe rows of the gallery's `?area=remote`). **In the
shipped bundle those two probe rows report a 404, by design**; it is recorded in `bundle.json`'s `excluded` list.

The mechanism matters more than the entry. The script fails the build when the staged tree exceeds `-MaxBytes`
(default 10,000,000) or `-MaxFiles` (default 120), printing the ten largest staged files — so the *next* stray
multi-megabyte fixture is caught by a red build rather than by someone noticing months later.

Nothing under `_framework/` may ever be excluded: `blazor.boot.json` carries SHA-256 SRI hashes for `dotnet.js`,
`dotnet.native.js`, `dotnet.runtime.js`, `dotnet.native.wasm` and every assembly, so removing or editing a file
there makes the browser refuse the module with no useful message. The script throws if the exclude list names one.

### Two guards you will meet

* **The serving guard.** The script probes 127.0.0.1:8973–8982, 5000 and 5001 and refuses to publish if anything
  answers. Publishing over a served tree leaves a zero-byte wasm that the browser refuses on an SRI mismatch — the
  project's most expensive recurring bug, now enforced rather than remembered. `-SkipGuard` overrides.
* **The unconditional clean.** `obj/Release` and `bin/Release` are removed before every publish. That is the
  documented recovery from the zero-byte-wasm state, and a shipped bundle built on a poisoned intermediate is a
  failure nobody can diagnose from a bug report.

### Regeneration policy

The bundle is rebuilt **on demand, not on every release**. Rebuild when:

1. the JavaScript surface changes — `Abi.SurfaceHash` moves;
2. a Runtime change alters browser behaviour a user would notice;
3. the .NET SDK is upgraded.

A patch release that touches only Unity-side code ships the previous bundle. Staleness is made *visible* rather
than prevented: `bundle.json` records the commit, and the Editor window shows an info box when that commit is not
the current `HEAD`.

`bundle.json` is generated, never hand-edited:

```json
{
  "nowuiVersion": "1.12.0", "commit": "1edd44c", "dirty": true,
  "builtUtc": "...", "dotnetSdk": "9.0.101", "surfaceHash": "0xAB4FCFF5",
  "compressed": false, "files": 86, "bytes": 8489520,
  "excluded": ["nowui-test-large.png"]
}
```

`surfaceHash` is read out of `wwwroot/nowui/abi.js` with `node` — the JavaScript half of the same table `Abi.cs`
folds, so it is exactly the number a stale bundle would disagree with.

---

## 3. Committing it

```
git add -f "Assets/NowUI/WebBundle~"
```

The `-f` is required because of the root `.gitignore`'s bare `*.pdb` rule (the publish emits none —
`DebugType=none` — but the `-f` costs nothing and removes the question).

`Assets/NowUI/.npmignore` must **not** list `WebBundle~`. Verified with `npm pack Assets/NowUI --dry-run`:
`WebBundle~/_framework/dotnet.native.wasm` is in the tarball.

**Not covered by this milestone:** the `.unitypackage` install route. Tilde folders cannot appear in a
`.unitypackage` at all — its entries are keyed by the asset GUID in a `.meta` file, and Unity writes none under `~`
(which is why `Samples~` and `Documentation~` are already absent from that asset). The intended fix is a release
step that zips `Assets/NowUI/WebBundle~` to `NowUI-Web-<version>.zip` and attaches it to the GitHub release;
`NowWebPreviewPaths.BundleCandidates()` already checks `<ProjectRoot>/NowUI/WebBundle` as candidate 2 for people
who unzip it there.

---

## 4. The Editor server

`System.Net.HttpListener`, bound to **`http://127.0.0.1:PORT/` literally** — never `localhost` (which resolves to
`::1` first on a dual-stack machine, so a listener on one address and a browser on the other is a connection
refused with no explanation), and never `+` or `*` (which need admin on Windows *and* would put the user's project
folder on the LAN).

**Port.** 8973 by default, remembered in `EditorPrefs`, probed upward to 8982, then ephemeral. If a candidate port
is occupied, the server asks whoever holds it `GET /__nowui/id` over a hand-written one-line HTTP request; it
adopts that server only if the answer is *this* project's path. Without that check, project A's Editor would
happily hand the user project B's application and nothing on screen would say so. The whole sweep is capped at
750 ms so ten dead-but-accepting ports cannot freeze the Editor.

**Routes, in order:**

| route | serves |
|---|---|
| `GET /__nowui/id` | this project's root path — the identity probe above |
| `GET /__nowui/mtime?app=NAME` | the resolved app file's last-write ticks — the whole of `?watch=1` |
| `GET /<name>.js` (root, not reserved) | `<ProjectRoot>/NowUI/apps/<name>.js`, else the bundle's own |
| `GET /apps/<name>.js` | the same file, accepted so a later bundle can move its samples |
| everything else | the bundle, `/` → `/index.html` |

**The shadowing rule is the only interesting decision.** The user's folder is checked first, the bundle second.
That is what makes `?app=app` open the user's own file once seeded while the shipped sample remains the fallback if
they delete theirs, and it is what makes a package upgrade unable to overwrite their work: *the package never
writes to the folder that wins.*

`main.js`, `nowui-fetch.js`, `nowui-gl.js` and `nowui-input.js` are reserved and never resolve against the user's
folder, so an application accidentally named `main.js` cannot replace the page's boot script.

An application is a bare `NAME.js` at the site root because that is where the runtime looks for it — `BridgeHost`
resolves `../NAME.js` from `_framework/`. No change to `BridgeHost` was needed, and module identity is untouched:
`/app.js`'s `./nowui/nowui.js` and `/_framework/`'s `../nowui/bridge.js` still resolve to the same `/nowui/` URLs,
so both halves of the bridge get the same `bridge.js` instance.

**Caching.** Filenames no longer carry a content hash, so freshness is the server's job:

* bundle files → `Cache-Control: no-cache` + a strong `ETag` (`"<size>-<mtimeTicks>"`), with `304` on a matching
  `If-None-Match`. The browser revalidates every load (free on loopback) and can never serve a stale body after a
  rebuild. This finally puts the project's documented *"the .NET loader caches per URL and survives cache clearing,
  rebuilds and query-string busting"* hazard under our own control.
* the user's own file → `Cache-Control: no-store`. F5 always re-reads from disk.

**MIME is load-bearing, not decorative.** `.wasm` → `application/wasm` (the loader uses
`WebAssembly.instantiateStreaming`, which *refuses* anything else) and `.js` → `text/javascript` (a module script
with the wrong type is refused). Both failures are silent apart from one console line.

**No COOP/COEP headers**: the build is single-threaded, so `SharedArrayBuffer` isolation is not needed and adding
it would only break things.

**Lifecycle.** `[InitializeOnLoadMethod]` returns immediately under `Application.isBatchMode`, so nothing binds a
socket during a CI or test-harness run. `beforeAssemblyReload` closes the listener *without* clearing the
`SessionState` "should be running" flag, so a script recompile takes the server down and brings it straight back up
on the same port; `Stop()` clears the flag. The accept thread is `IsBackground = true`, which is what stops the
Editor hanging on quit, and it exits quietly on `ObjectDisposedException`/`HttpListenerException`.

---

## 5. The page

Three changes to `wwwroot/`, all in `index.html` and `main.js`:

1. **A boot overlay.** `#nowui-boot` paints "Starting NowUI…" over the canvas and is removed on the first drawn
   frame (a one-shot flag inside `frame()`, not a timer). `Program.cs`'s `RunAsync` catches its own start-up
   exceptions and reports them through `host.log` at level 2, which `main.js` owns — so a `?app=` file that will
   not parse now shows *"NowUI reported a start-up failure. SyntaxError: …"* on the page instead of leaving a black
   canvas and a console line nobody opens. A 12-second watchdog covers level-2 reports that are not the explicit
   `start-up failed` marker.
2. **The report panel default is inverted.** `bridge.js` draws a diagnostic `<pre>` across the bottom 45% of the
   page. That is right when the report *is* the picture (`?bridge=`) and wrong when the picture is an author's
   application. `main.js` now injects `#nowui-bridge-report { display: none }` when `?app=` is present and
   `?report=1` is not — a stylesheet rule rather than a removal, deliberately: the element is the oracle several
   headless drivers read, and `window.__nowuiBridgeReport` is untouched. `?bridge=` is unchanged.
3. **`?watch=1`.** With `?app=NAME`, the page polls `/__nowui/mtime?app=NAME` twice a second and reloads when the
   value moves. ~20 lines; a poll rather than a WebSocket or a `FileSystemWatcher` because the browser already has
   an F5 key. The endpoint only exists on the Editor's server, so the flag turns itself off elsewhere.

`NowUI.Web.csproj` also gained `CompressionIncludePatterns` for `**/*.ttf` and the GLSL extensions: the SDK's
default list carries `**/*.otf` but not `**/*.ttf`, so the four NotoSans faces (2,546,680 B raw, 942,742 B at
brotli-11) were shipping uncompressed on any hosted deploy. Inert when `CompressionEnabled=false`.

---

## 6. The first run, exactly

Preconditions: NowUI installed by UPM. No network. No .NET SDK. No terminal.

1. The user clicks **Tools ▸ NowUI ▸ Web Preview**.
2. In the same frame, behind the window: `BundleRoot()` resolves to
   `Library/PackageCache/com.blenminer.nowui@…/WebBundle~` and is validated by `_framework/blazor.boot.json`;
   `SeedApps()` creates `<ProjectRoot>/NowUI/apps`, copies the sample to `app.js` and writes a `README.md`; the
   server binds 127.0.0.1:8973 (opening a socket, not reading the bundle);
   `Application.OpenURL("http://127.0.0.1:8973/?app=app&report=0&watch=1&v=…")`.
3. The browser opens on black with "Starting NowUI…".
4. ~8.5 MB comes off loopback and the wasm is instantiated. Nothing leaves the machine: every URL is 127.0.0.1, the
   fonts are `Fixtures/NowUI/*.ttf` on disk, and there are no CDN references in `index.html` or `main.js`.
5. The overlay clears on the first frame and the application is on screen. **No debug panel.**
6. The window reads `Serving http://127.0.0.1:8973/` with the app's path beside it.

Second run: same port, warm module cache, sub-second. The loop afterwards is: open
`<ProjectRoot>/NowUI/apps/app.js` in any editor (or point an AI at it), save, F5 — or leave `?watch=1` on and skip
the F5. Unity is not involved and reimports nothing.

When it fails the user gets a sentence, not a stack trace: a missing bundle names all three paths searched and how
to get the zip; a busy port range names the fallback port; a boot failure is on the page; a broken `app.js` is on
the page; a foreign server on 8973 is skipped rather than adopted.

---

## 7. Known limits

* The bundle lags `HEAD` by design. Visibility, not automation, is the mitigation. If that proves annoying, the
  honest next step is a CI job that rebuilds on a `Standalone/` path filter and opens a PR — *not* an Editor-side
  rebuild, which would need the SDK the user does not have.
* `?area=remote` loses two byte-cap probe rows in the shipped bundle. The clean fix is re-encoding
  `nowui-test-large.png` to ~250 KB, which also saves another ~1.05 MB; it is a shared fixture, so it is recorded
  as a follow-up rather than folded in here.
* 8.5 MB of binaries ride along in every UPM checkout and npm tarball whether or not the user ever opens a browser.
  The only shrink that preserves offline first-run is a smaller payload — one font face instead of four
  (2,546,680 B is 30% of the bundle) or an AOT/relinked runtime. Both are real work.
* `HttpListener` behaviour under Mono's implementation during an *abrupt* domain reload has not been observed on a
  real Editor. The accept loop catches `ObjectDisposedException`/`HttpListenerException` and exits quietly, which
  should be enough; the fallback if it is not is a `TcpListener` plus ~60 lines of HTTP/1.1 GET.
