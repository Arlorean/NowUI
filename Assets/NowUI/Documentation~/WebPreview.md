# Web Preview: running NowUI in a browser, and showing it to someone

NowUI ships a precompiled WebAssembly build of itself inside this package, so
it runs in a browser with no .NET SDK, no build step and no network.
Applications are written in JavaScript.

**Serving it does not need the Unity Editor, and an assistant should never ask
for the Editor when it can serve the bundle itself.** There are two ways in, and
the first is the one to reach for:

```
python <package>/WebBundle~/serve.py --app NAME     # anyone who can run a script
Tools > NowUI > Web Preview                          # the Editor's own, for authoring
```

`serve.py` is in the package, needs only Python 3 and nothing from `pip`, finds
the project and the applications folder by itself, and prints a URL per
application. Start it, hand over the link, and the reader clicks it. The Editor's
window does the same job on `http://127.0.0.1:8973/` and adds live reload on
save, which makes it the better tool while a person is iterating — and the wrong
one for handing someone a running page, because it needs Unity open and a menu
clicked.

This guide is for the case where **someone needs to see what was built** — a
reviewer, a teammate, or a user reading a conversation on a phone. It covers
what the browser surface can and cannot do, how to record a still or a short
animation from inside the page, and what happens on every path that fails.

- [When to reach for the browser](#when-to-reach-for-the-browser)
- [Serving it yourself](#serving-it-yourself)
- [Writing an application](#writing-an-application)
- [The five mistakes](#the-five-mistakes)
- [Showing the result](#showing-the-result)
- [Capture flags](#capture-flags)
- [Keep the window visible](#keep-the-window-visible)
- [What each browser can record](#what-each-browser-can-record)
- [When something is missing](#when-something-is-missing)

## When to reach for the browser

Reach for it when **all three** hold:

1. the subject is NowUI itself, or what a layout or control looks and feels like;
2. seeing it move, or clicking it, changes the answer;
3. the artifact is disposable — a prototype, an illustration, a demo.

Do **not** reach for it when the user is building Unity UI in a Unity host, when
the request is for C#, when the answer is an edit to an existing scene, when the
question is Unity-only (world space, render pipelines, IMGUI, mobile safe areas),
or when the package has no `WebBundle~` folder.

**Never silently substitute a web prototype for the Unity work that was asked
for.** The browser surface is a strict subset and the two hosts are not
interchangeable. Offer a prototype *alongside* the real work, saying which is
which.

## Serving it yourself

```
python <package>/WebBundle~/serve.py                 # every application, one URL each
python <package>/WebBundle~/serve.py --app calculator
python <package>/WebBundle~/serve.py --port 8080 --project /path/to/UnityProject
```

It prints the URLs it can serve and then stays in the foreground, so run it in
the background and read the port back from its output — it takes 8973 when that
is free, walks up to 8992, and falls back to an ephemeral port rather than
failing. **Read the port it printed; do not assume 8973.**

Three routes, matching the Editor's own server so an application behaves the
same either way:

| Request | Served from |
| --- | --- |
| `/?app=NAME` → `/NAME.js` | `<ProjectRoot>/NowUI/apps/NAME.js`, falling back to the bundle's samples |
| everything else | the bundle itself |
| `/assets/...` | `<ProjectRoot>/Assets/...`, pictures, Lottie documents and fonts only |
| `/docs/` and `/docs/NAME.md` | the package's own documentation - an index, then each document. `Documentation~` is a sibling of `WebBundle~`, so this works from a clone and from an installed copy alike. The package's own README answers to `/docs/package.md`, because `Documentation~/README.md` is a different document and claimed the obvious name first |

The project root is inferred from where the bundle sits (`<ProjectRoot>/Assets/
NowUI/WebBundle~`), so from a normal install there is nothing to configure. Pass
`--project` when the bundle has been copied somewhere else.

**Why not `python -m http.server`.** The bundle is stored BROTLI-ONLY: every
large file is `NAME.br` and the raw original is deleted, which is what keeps the
committed artifact around a third of its size. A plain static server answers 404
for all of them, and one that finds `NAME.br` and sends it without
`Content-Encoding: br` hands the browser compressed bytes to parse as
JavaScript. Both failures read as "the bundle is broken" rather than "the server
is wrong", and neither is worth rediscovering — that is the single thing
`serve.py` exists to get right. Any other server is fine if it does the same.

**What it does not do:** accept captures. `?shot=` and `?clip=` post their bytes
back to the *Editor's* server, which is what writes them into the project; a
static server has nowhere to put them. Use the Editor's preview when a capture
has to land on disk, or drive the browser and screenshot it from outside.

## Writing an application

An application is one JavaScript file in `<ProjectRoot>/NowUI/apps/NAME.js`,
outside this package and outside `Assets/`, so a package upgrade cannot
overwrite it and Unity never reimports it. It is served at `?app=NAME`.

```js
import { start, ui } from './nowui/nowui.js';

const state = { count: 0, mode: 'Compact' };

start(() => {
    ui.column({ padding: 24, gap: 12, grow: 1 }, () => {
        ui.heading('Hello');
        if (ui.button('Add')) state.count++;
        ui.text('count: ' + state.count);
    });
});
```

`./nowui/nowui.js` is served by the same local server out of this package's
bundle. Do not copy it beside the application: both halves of the bridge have to
reach the same module instance, and they only do that through the shared URL.

**The surface itself is documented in the bundle**, in comments beside every
function: `WebBundle~/nowui/nowui.js`. It is one file and it is the authority —
read the entry for a function before calling it rather than inferring the
signature from the C# API, which is a different and larger surface.

**It is not readable with a plain file read.** The shipped bundle stores that
file Brotli-compressed and drops the raw original, so on disk it is
`WebBundle~/nowui/nowui.js.br` and `cat` gets binary. Two ways to read it:

- **While the preview is running**, the server decompresses it for you:
  `http://127.0.0.1:8973/nowui/nowui.js` returns the ~82 KB of source. This is
  the path to prefer; it needs nothing but the Editor that is already open.
- **With the Editor closed**, decompress it once, with whatever is on the
  machine — `brotli -d nowui.js.br`, or
  `node -e "require('fs').writeFileSync('nowui.js',require('zlib').brotliDecompressSync(require('fs').readFileSync('nowui.js.br')))"`.
  Neither ships with the package; if neither is present, ask the user to start
  the preview and read it over HTTP instead.

What the browser surface covers: layout, containers, every control, `ui.theme`,
`ui.split`, and eleven drawing primitives — `ui.canvas`, `ui.rect`, `ui.circle`,
`ui.line`, `ui.bezier`, `ui.triangle`, `ui.polygon`, `ui.gradient`, `ui.mask`,
`ui.image` and `ui.lottie` — with colour, radius, stroke and dash options.

**Images and Lottie come from a URL, not from the bundle.** `ui.image(box, url)`
and `ui.lottie(box, url)` fetch at runtime and cache by URL, so nothing is
embedded in the WebAssembly and the download is the browser's, with its cache.
Any URL the page can reach works, which in a preview means anything the Editor's
own server serves. An image that has not arrived draws a muted placeholder so
the layout keeps its shape; a Lottie draws nothing until it is there, because it
is usually an accent rather than content. `ui.lottie` plays from the page clock
on its own — pass `time` in seconds to drive it yourself.

`ui.image` takes a **`fit`**, and it decides where the corners go as much as
where the pixels go:

| `fit` | the picture | the shape `radius` and `stroke` follow |
| --- | --- | --- |
| `'contain'` (default) | whole picture, centred, letterboxed | the **picture** — the quad shrinks to the source's aspect |
| `'cover'` | fills the box, cropped centrally on one axis | the **box** — the quad is unchanged, the UVs move |
| `'stretch'` | distorted to the box | the box |

The default is `contain` rather than `stretch` on purpose: an author who writes
a URL means "show me this picture", and a silently squashed photograph reads as
a rendering bug. A rounded avatar usually wants `'cover'`; an illustration that
carries its own margins wants `'contain'`.

Neither mode is new machinery. `contain` is `NowRectangle.preserveAspect`, which
NowUI has always had (`Now.cs:2279` shrinks and centres the quad), and `cover`
is a `uvRect` computed from the two aspect ratios — which works without
deforming the rounded corners because the shader keeps the shape's distance
field in full-quad space no matter where the UVs point.

**`ui.markdown(key, source, opts)`** renders a Markdown document, and it is the
one control that lays itself out rather than taking a box:

```js
ui.scroll('body', { padding: 22 }, () => {
  const link = ui.markdown('doc', source, { fontSize: 15 });
  if (link) open(link);          // the link the reader clicked, or null
});
```

That shape is the point. The document measures itself in the flow, so the scroll
container sizes and clips it without being told a height — where a rect-shaped
markdown would have to report its height to JavaScript and be handed it back a
frame later, which is a frame of jitter every time the text or the width changes.
Tables, code fences, inline code and links all render, and colours come from the
ambient `ui.theme`.

It is cheaper to redraw than it looks. The parse and the layout are cached, and
so is the text itself: a string is volatile the first time the recorder sees it
and interned the second, after which the handle lasts the session — a document
that does not change costs **zero text bytes per frame**, measured at frame 3060
of the docs viewer. One that *does* change every frame never reaches that second
sighting, so it stays volatile and leaks nothing.

Links are **reported, never followed**. A relative `Layout.md` is another
document and the application decides what that means; an `https://` link is the
web and the application decides that too. That is what makes an in-page docs
viewer four lines instead of a parser. `NowUI/apps/docs.js` is one, and the
package's own documentation is served for it under `/docs/` — see
[Serving it yourself](#serving-it-yourself).

What it does not: arbitrary transforms, SDF, the node graph and the code editor.
Those are Unity-side only.

## The five mistakes

These are the ones that are actually made. Everything else is in
`nowui/nowui.js`.

**1. Three functions throw by name.** `ui.reset`, `ui.overlay` and
`ui.contextMenu` are declared and not implemented; each throws a named error
saying so. Do not write them. The list is exported as `NOT_IMPLEMENTED`, and the
module asserts against it at load, so it cannot drift from this page.

**2. Ten controls refuse `{ label }`.** They have no label of their own, so a
label option would be silently dropped — instead it throws, naming the fix.

| Refuse `{ label }` — put a `ui.text` beside them | Take `{ label }` |
| --- | --- |
| `numberField`, `slider`, `intSlider`, `dropdown`, `combo`, `colorField`, `datePicker`, `timePicker`, `tabs`, `progress` | `button`, `checkbox`, `selectable`, `foldout`, `switch` |

```js
ui.row({ gap: 12, align: 'center' }, () => {
    ui.text('Load', { width: 64 });
    state.load = ui.slider('load', state.load, 0, 1, { grow: 1 });
});
```

A `button`, `checkbox`, `selectable` or `foldout` with no `{ label }` uses its
key as the label. A `switch` is the exception: its label defaults to empty,
because a switch's key names what it controls rather than what it says.

**3. `dropdown` and `combo` take and return the OPTION; `tabs` takes an INDEX.**

```js
state.window = ui.dropdown('window', state.window, ['1 minute', '5 minutes']);  // a string
state.view   = ui.tabs('view', state.view, ['Throughput', 'Latency']);          // an integer
```

Passing an index to a dropdown throws rather than quietly showing the wrong
option.

**4. Every value a control returns describes the PREVIOUS frame**, and a popup
selection is delivered two frames after the click. Only `ui.frame` is current.
This is normal for the surface and it is visible in a recording: a click lands
one frame after it happens. Do not treat it as a bug.

**5. Builders are consumed by the call.** There are no dangling builders here as
there are in C#; every `ui.*` call draws, and every scope takes a callback.

## Showing the result

Four rungs. The cheapest is also the richest, which is the thing that gets
inverted most often.

| Rung | The reader gets | Needs | Falls back to |
| --- | --- | --- | --- |
| **0. the link** | a real, interactive app | a server, which you start yourself | a still, if the reader is away from a computer |
| **1. a still** | one PNG inline | the tab visible for about one frame | rung 0 |
| **2a. an animated WebP** | motion, inline in a conversation | the tab visible for the clip | rung 1 |
| **2b. a WebM or MP4** | motion, smaller file, **not inline** | the same | rung 2a |

Rung 0 is the best answer whenever the reader is at a computer. The recordings
exist for the reader who is not.

**What an agent can do unaided:** write `<ProjectRoot>/NowUI/apps/NAME.js`,
**start `WebBundle~/serve.py` and hand over a working URL**, read
`<ProjectRoot>/NowUI/captures/`, and give an absolute path to what landed there.
Hosting is part of the job, not something to delegate: a reply that ends "now
open Tools > NowUI > Web Preview" has handed the user a chore in place of a
result.

**What it cannot do:** open the browser on the user's machine, bring a window to
the front, or click anything. So the one manual step that does remain is the
reader clicking the link — and, for a CAPTURE, leaving that window in front while
it records, since a hidden tab draws no frames. Say that plainly rather than
implying the picture appears by itself.

A server started for someone else outlives the reply that produced it only as
long as the process does. Say which port it is on and that it stops when the
session does, so a dead link later is expected rather than mysterious.

Never claim a file that has not been seen on disk. A capture that did not happen
leaves either nothing or a `.txt` explaining itself; both are readable answers,
and neither is a picture.

## Capture flags

Add them to any preview URL. The page records itself — `canvas.toBlob` plus a
small RIFF muxer, or `MediaRecorder` — and posts the bytes to the Editor, which
writes them to `<ProjectRoot>/NowUI/captures/NAME.EXT`.

```
?app=NAME&dpr=1&shot=1&name=login
?app=NAME&dpr=1&clip=5&fps=12&scale=0.5&name=login-flow
?app=NAME&dpr=1&clip=5&format=webm&name=login-flow
```

| Flag | Default | Meaning |
| --- | --- | --- |
| `shot=1` | — | one still, then stop |
| `clip=SECONDS` | — | a clip of that length, capped at 15 s. A clip that would need more than 180 frames lowers its own frame rate to fit rather than stopping early |
| `name=NAME` | `nowui-shot` / `nowui-clip` | the file name. Letters, digits, `-` and `_`; the extension is chosen by the Editor, never by the page |
| `format=` | `png` for a shot, `webp` for a clip | `png`, `webp` or `webm` |
| `fps=N` | `12` | frames per second, 1–30 |
| `scale=F` | `1` for a shot, `0.5` for a clip | fraction of the drawing buffer, then capped at **1600 px wide for a still and 960 px for a clip** whatever this said. **Ignored by `format=webm`**, which records the canvas at its own size |
| `quality=F` | `0.75` | WebP quality, 0.1–1 |
| `delay=SECONDS` | `0` | run the page this long before recording starts, on top of the warm-up |
| `dpr=1` | the display's | pin the device pixel ratio. **Pass it.** On a 2× display the drawing buffer is four times the pixels and every size below quadruples |

The capture waits for 10 drawn frames and 300 ms before it starts. The first
NowUI frame in a browser takes 47–450 ms depending on how much text is on the
page, because every glyph is rasterised into the atlas on first sight; every
frame after it takes about 10 ms. Recording frame one photographs a half-built
page.

Sizes measured on a 1264×704 canvas at `dpr=1`, in Chromium 152 on Windows 11:

| Request | Result |
| --- | --- |
| `shot=1` | 1264×704 PNG, 57,258 B |
| `shot=1&scale=0.75` | 948×528 PNG, 38,066 B |
| `clip=5&fps=12&scale=0.5` | 632×352 WebP, 59 frames, 246,044 B |
| `clip=6&fps=12&scale=0.5` | 632×352 WebP, 70 frames, 292,576 B |
| `clip=3&fps=15&format=webm` | 1264×704 VP9 WebM, 188,419 B |

An animated WebP made this way is several times larger per pixel than the WebM,
because each frame is encoded independently and there is no inter-frame
prediction. That is the price of a format a conversation renders inline; the
WebM is there for anyone who does not need that.

The page caps a WebP clip at 8 MB and pays for it in frame rate rather than
quality — halving the frames still shows the motion, where dropping quality far
enough to matter makes NowUI's gradients band.

## Keep the window visible

**A browser suspends `requestAnimationFrame` entirely in a tab that is hidden,
minimised, behind another window, or on a background virtual desktop.** A NowUI
page there runs zero frames, and neither capture API says so. Measured in such a
tab: zero `requestAnimationFrame` callbacks in three seconds, a `canvas.toBlob`
still that came back as **87 bytes of a single colour**, and a three-second
`MediaRecorder` run that produced **110 bytes in one data chunk**. Neither call
threw, and neither reported an error. A capture written from there would be a
black rectangle presented as a result.

`document.visibilityState` is not a sufficient test either. In a measured run it
read `"visible"` through three seconds that delivered **zero** frames, because
the window was occluded rather than hidden.

So NowUI counts the frames the page actually drew during the capture window. If
too few were drawn it writes **no image at all**; it writes
`<ProjectRoot>/NowUI/captures/NAME.txt` instead, saying how many frames it got,
how many it expected, and to keep the window in front. Whoever went looking for
the picture finds the reason in its place.

Tell the user this before they open the URL. It is the single most likely way a
capture comes back wrong.

## What each browser can record

Measured on **Chromium 152.0.7977.76, Windows 11**:

- `canvas.toBlob('image/webp')` returns a genuine `RIFF`/`WEBP` file. The stills
  are muxed into an animated WebP in the page, and Chrome's own `ImageDecoder`
  reads the result back as an animated track with the expected frame count and
  per-frame duration.
- `MediaRecorder.isTypeSupported` is **true** for `video/webm` and its `vp8`,
  `vp9`, `h264` and `av01` spellings, for `video/mp4` and `video/mp4;codecs=avc1`,
  and for `video/x-matroska;codecs=avc1`.
- It is **false** for `image/webp` and `image/gif`. `MediaRecorder` structurally
  cannot produce a format that renders inline in a conversation. That is why the
  default is the muxed WebP and not the recorder.

Measured on **Firefox 155.0.1, Windows 11**, against the same application and the
same shipped bundle:

- `shot=1` produced a 1280×842 PNG of 40,791 B.
- `clip=5&fps=12&scale=0.5` produced a 640×422 animated WebP, 42 frames, all
  distinct, loop forever, 162,888 B. So `toBlob('image/webp')` is genuine WebP
  here and the muxer works unchanged.
- `clip=4&fps=15&format=webm` produced a 164,886 B WebM through `MediaRecorder`.

All three paths work in Firefox. One difference worth knowing, because it is
what `fps` really means here: Firefox drew fewer frames than the requested rate
over the same window — 42 where 60 were asked for. `?fps` is a ceiling the
browser may miss, not a promise. The clip is still the right *length*: each
frame is stamped with the measured capture window divided by the frames that
actually arrived, so 42 frames of a 5 s recording play back over 5 s at about
8 fps rather than racing through in 3.5 s. A browser that cannot hold the rate
gives a choppier clip, never a faster one. Ask for a lower `fps` if you would
rather have even spacing than detail.

**Safari was not measured** — no install on the machine these numbers come from.
The page feature-detects rather than assuming: if `MediaRecorder` or the
requested MIME type is unavailable it says so and falls back to the animated
WebP, and if `toBlob('image/webp')` hands back something that is not a WebP —
which is the documented Safari behaviour — the animation cannot be assembled and
it reports that instead of writing a broken file. Verify Safari rather than
trusting this paragraph.

## When something is missing

| Condition | What happens | What to do |
| --- | --- | --- |
| no `WebBundle~` in the package | there is no browser path at all | say so; work in Unity |
| the Editor is closed | nothing, for serving — this is the normal case | run `WebBundle~/serve.py`; the Editor is only needed for live reload on save |
| `serve.py` cannot bind a port | it walks 8973-8992, then takes an ephemeral one | read the URL it printed rather than assuming 8973 |
| no Python on the machine | `serve.py` will not run | any static server works IF it sends `Content-Encoding: br` for the `.br` files (see below); the Editor's window is the fallback that needs nothing installed |
| captures have nowhere to be written | the page can record but not save | the capture upload goes to the EDITOR's server only. `serve.py` serves; it does not accept captures. Use the Editor's preview for capture flags, or drive the browser and screenshot it |
| the tab is hidden, minimised or occluded | zero frames drawn; no image is written | a `.txt` of the same name lands instead — read it, ask the user to keep the window in front, retry |
| `MediaRecorder` absent or the MIME type unsupported | detected before recording | falls back to the animated WebP automatically |
| `toBlob('image/webp')` does not produce WebP | detected by the container's own bytes | reports that the browser cannot make an animation; take a still instead |
| the upload is refused (no server, 403, 413) | the banner turns red and grows a **Download NAME.EXT** link holding the bytes | nothing is on disk until the user clicks that link, and then it is in their Downloads folder, not the project — say so rather than reporting a file |
| no browser automation available | everything above still works | the reader clicks the one URL you started a server for |

Captures land in `<ProjectRoot>/NowUI/captures/`, beside `apps/` and outside both
this package and `Assets/` — a package upgrade replaces the package wholesale,
and an image under `Assets/` is an AssetDatabase import with a `.meta` file per
capture.

The Editor accepts a capture only from a page it served itself: the POST carries
a token minted per server start and readable only by same-origin script, the
destination folder is computed rather than received, and the extension is chosen
from the content type through a closed list. A page on another origin cannot
write into the project even though the server is on loopback.

Nothing captures on its own. A preview that wrote files nobody asked for would be
a preview that filled a repository.
