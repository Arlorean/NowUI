---
name: nowui
description: Build, preview, publish, review, or debug C# UI with the installed NowUI package. Use when the user names NowUI, project instructions select it, or the affected code uses it. Do not select NowUI merely because it is installed when the task uses another UI framework.
---

# NowUI

Locate the active package, then use its versioned documentation and public
source to carry out the task. This skill is a router, not an API reference.

## Find the active package

- In a Unity project, prefer Unity Package Manager's resolved path for
  `com.blenminer.nowui`. Otherwise inspect `Packages/manifest.json`,
  `Packages/packages-lock.json`, and generated project/source references.
  Follow local `file:` dependencies as well as embedded and cached packages.
- Common locations are `Packages/com.blenminer.nowui`, `Assets/NowUI` in the
  source repository, and `Library/PackageCache/com.blenminer.nowui@*`.
  Embedded folders and local dependencies may have other names or locations;
  a standalone package checkout may itself be the root.
- Validate the candidate's `package.json` name is `com.blenminer.nowui`.
  Cache suffixes can be hashes. If several revisions exist, identify the active
  one from resolved package information or source references, not lexical order
  or the highest version. If still ambiguous, ask for the active package path.
- If the package is absent, report it. Installation or upgrades are separate
  work unless already part of the user's request.

## Use the installed guidance

Read `<package-root>/Documentation~/AI_GUIDE.md` for host/placement choices and
essential contracts, then only the feature guides relevant to the task. Search
the installed public source and XML comments for uncertain signatures; do not
guess from model memory or GitHub `main`. Use a nearby example when helpful.

Unity consumer code belongs under the project's `Assets` directory; native
preview projects belong under `NowUI/apps` as described in the preview guide.
PackageCache is read-only. For package contributions, also read `<package-root>/AGENTS.md`.
An embedded or local dependency does not imply that it should be modified.

## Preview, capture, or publish

Use native C# previews by default. Read
`<package-root>/Documentation~/NativePreview.md`, then use
`<package-root>/Native~/nowui.ps1` or the source checkout's
`Tools/NowUI-Native.ps1`. Reuse supported project assets directly; the user does
not need to export assets or run an Editor menu. Share drawing code with Unity.

Choose the command for the requested result; the guides provide complete arguments:

| Result | Command |
| --- | --- |
| New C# scene project | `init` |
| Interactive mockup or animation app | `preview` |
| Still image | `render` |
| Deterministic animation frames | `animate` |
| Requested website or browser deployment | `publish --target web` |
| Test the scene in a browser | `preview --target web` |
| Open an already published site | `serve` |

The web target is optional and uses the same C# scene. Read
`<package-root>/Documentation~/BrowserDeployment.md` for its additional build
requirements, asset inclusion and browser limits. Native remains the default.

Create and launch the result yourself. Check startup, inspect the rendered output,
and exercise the interaction relevant to the request before sharing it. For stills
or animation captures, inspect the produced images. Report the files, app or local
URL that actually worked, and state any checks you could not complete.

## Verify the change

For code changes, compile against the installed package, address `NOWUI001` and
`NOWUI002`, and run relevant tests or a focused scene/editor check. For
performance work, warm representative state before measuring. Use source-repo
harnesses only when working in that repository. For documentation-only changes,
check links and API claims. Report validation performed and any unavailable
checks; do not claim a compile or runtime check that was not run.
