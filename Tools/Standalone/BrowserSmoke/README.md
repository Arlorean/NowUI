# Optional browser smoke

The manually dispatched `browser.yml` workflow builds the browser kit, packs and
installs the CLI, and publishes an unchanged `nowui init` scene outside the source
checkout. It uses the prepared portable WASM native objects; it does not require
Unity or project asset export. Native CI does not install these tools.

The C# and Node input tests exercise shared controls and internal DOM transport.
The Chromium test then checks the published application starts without browser
errors, paints the canvas, handles two real pointer clicks through C# state, and
continues rendering after a resize. It saves screenshots and browser diagnostics.
The smoke is a behavior check, not a pixel baseline or hardware IME/gamepad test.

```sh
dotnet test Standalone/NowUI.Browser.Tests -c Debug
node --test Standalone/NowUI.Browser.Tests/BrowserInputTransport.test.mjs
npm --prefix Tools/Standalone/BrowserSmoke ci
cd Tools/Standalone/BrowserSmoke
npx playwright install chromium
node validate-site.mjs /path/to/published-site --scaffold
node smoke.mjs http://127.0.0.1:8080/ /path/to/results --scaffold
```

Start the server separately with `nowui serve /path/to/published-site --port 8080`.
Omit `--scaffold` to check another C# scene without assuming the generated sample's
button position or absence of Unity assets. A graphics-capable Chromium/WebGL2
environment is required; CI uses Chromium's software graphics backend.

For the project-asset playground, pass `--playground` instead. This selects a
1120 × 780 viewport, edits the caption, switches to the light theme, selects the
Heart gallery card and captures F8 in the shared key field. It also records 600
C# frame CPU samples after 120 warmup frames (median/p95 and startup time in
`browser.json`) and saves `browser-reference.png` from a fresh scene at
`?time=0.5&dpr=1`. Captures preserve the WebGL drawing buffer; CPU timings are
diagnostic and exclude GPU completion, so they are not a native/browser speed
comparison. The optional CI job uses the asset-free scaffold smoke by default.
