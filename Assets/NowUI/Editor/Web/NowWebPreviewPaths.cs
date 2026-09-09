using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NowUI.Editor.Web
{
    /// <summary>
    /// Where the three things the Web Preview needs live on disk: the precompiled browser bundle that ships
    /// inside the package, the user's own JavaScript applications, and the identity of this Unity project.
    /// </summary>
    /// <remarks>
    /// Everything here is <see cref="System.IO"/> and nothing here is <see cref="AssetDatabase"/>, and that is
    /// forced rather than stylistic: the bundle lives in a folder whose name ends in <c>~</c>, which is Unity's
    /// own convention for "ships with the package, is on disk, and is NOT imported". Unity writes no .meta for
    /// it, compiles nothing in it and has no asset path for it, so <c>AssetDatabase.LoadAssetAtPath</c> would
    /// return null for every file in the bundle no matter how correct the path was.
    /// </remarks>
    internal static class NowWebPreviewPaths
    {
        /// <summary>The folder name inside the package that holds the published browser build.</summary>
        internal const string BundleFolderName = "WebBundle~";

        /// <summary>The one file whose presence proves a candidate folder really is a published bundle.</summary>
        internal const string BundleMarker = "_framework/blazor.boot.json";

        /// <summary>
        /// The project root: the folder that holds Assets/, Packages/ and Library/. Also the identity this
        /// project's server answers with on <c>/__nowui/id</c>, so a second Unity project's server squatting on
        /// the port is skipped rather than adopted.
        /// </summary>
        internal static string ProjectRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
        }

        /// <summary>
        /// <c>&lt;ProjectRoot&gt;/NowUI/apps</c> - the user's own JavaScript, OUTSIDE the package and OUTSIDE
        /// Assets/.
        /// </summary>
        /// <remarks>
        /// Outside the package because a package manager replaces the package folder wholesale on upgrade, and
        /// an application stored there would be deleted by an update. Outside <c>Assets/</c> because Unity
        /// imports .js under Assets/ as a TextAsset, so every save from an external editor would trigger an
        /// AssetDatabase reimport - the authoring loop would be fighting the Editor for no benefit. A sibling
        /// folder at the project root is still inside the user's own version control, which is where their
        /// source belongs.
        /// </remarks>
        internal static string AppsRoot
        {
            get { return Path.Combine(Path.Combine(ProjectRoot, "NowUI"), "apps"); }
        }

        /// <summary>The folder above <see cref="AppsRoot"/>, which also holds the README seeded beside it.</summary>
        internal static string UserRoot
        {
            get { return Path.Combine(ProjectRoot, "NowUI"); }
        }

        /// <summary>
        /// The published browser bundle, or null. First hit wins, and every candidate must contain
        /// <see cref="BundleMarker"/> to count - an empty or half-copied folder is not a bundle.
        /// </summary>
        internal static string FindBundleRoot()
        {
            foreach (string candidate in BundleCandidates())
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                string marker = Path.Combine(candidate, BundleMarker.Replace('/', Path.DirectorySeparatorChar));

                // Or its compressed sibling. The shipped bundle stores most files as NAME.br and drops the raw
                // original; this marker is deliberately kept raw, but a folder is still a bundle either way, and
                // a detector that only knew one spelling is what turned the first compressed bundle into
                // "no bundle found".
                if (File.Exists(marker) || File.Exists(marker + ".br"))
                    return Path.GetFullPath(candidate);
            }

            return null;
        }

        /// <summary>
        /// Every place a bundle could legitimately be, in the order they are tried. Public so the error message
        /// in the window can list exactly what was searched: "it did not work" plus three absolute paths is a
        /// bug report, "it did not work" alone is not.
        /// </summary>
        internal static string[] BundleCandidates()
        {
            var candidates = new System.Collections.Generic.List<string>(3);

            // 1. The package, however it was installed: Packages/ (embedded), Library/PackageCache/ (registry,
            //    OpenUPM or a git URL). resolvedPath is an absolute disk path in all three cases.
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(NowWebPreviewPaths).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                    candidates.Add(Path.Combine(info.resolvedPath, BundleFolderName));
            }
            catch (Exception)
            {
                // FindForAssembly throws for an assembly that is not in a package at all (an Assets/ import).
                // That is candidate 3's case, not an error.
            }

            // 2. The .unitypackage route. A tilde folder cannot appear in a .unitypackage at all - its entries
            //    are keyed by the asset GUID in a .meta file and Unity writes none under `~` - so users on that
            //    install route unzip NowUI-Web-<version>.zip here by hand.
            candidates.Add(Path.Combine(UserRoot, "WebBundle"));

            // 3. A plain Assets/NowUI/ import: find this assembly's own asmdef and look beside it.
            try
            {
                string[] guids = AssetDatabase.FindAssets("NowUI.Editor t:AssemblyDefinitionAsset");
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    if (!assetPath.EndsWith("/NowUI.Editor.asmdef", StringComparison.Ordinal)) continue;

                    // <project>/Assets/NowUI/Editor/NowUI.Editor.asmdef -> <project>/Assets/NowUI/WebBundle~
                    string editorDir = Path.GetDirectoryName(Path.Combine(ProjectRoot, assetPath));
                    if (string.IsNullOrEmpty(editorDir)) continue;
                    string packageDir = Path.GetDirectoryName(editorDir);
                    if (string.IsNullOrEmpty(packageDir)) continue;
                    candidates.Add(Path.Combine(packageDir, BundleFolderName));
                }
            }
            catch (Exception)
            {
                // AssetDatabase is unavailable during some very early domain states. Two candidates is enough.
            }

            return candidates.ToArray();
        }

        /// <summary>
        /// Creates <c>&lt;ProjectRoot&gt;/NowUI/apps</c> and seeds it, ONCE. Called on the first open of the
        /// window and never again: the package writes into the user's folder exactly one time, so an application
        /// they have edited can never be overwritten by an upgrade or by clicking the menu item twice.
        /// </summary>
        /// <returns>True if the folder was created by this call.</returns>
        internal static bool SeedApps(string bundleRoot)
        {
            if (Directory.Exists(AppsRoot)) return false;

            Directory.CreateDirectory(AppsRoot);

            if (!string.IsNullOrEmpty(bundleRoot))
            {
                // The shipped sample, copied out so the user has something that already runs to edit. Both
                // layouts are accepted: today the bundle serves its samples from the site root, and the design
                // note in the README describes an apps/ subfolder, so whichever one a future bundle uses, this
                // finds it.
                foreach (string sample in new[] { "app.js", "popups.js" })
                {
                    string source = Path.Combine(Path.Combine(bundleRoot, "apps"), sample);
                    if (!File.Exists(source)) source = Path.Combine(bundleRoot, sample);
                    if (!File.Exists(source)) continue;

                    try { File.Copy(source, Path.Combine(AppsRoot, sample), false); }
                    catch (IOException) { }
                }
            }

            string readme = Path.Combine(UserRoot, "README.md");
            if (!File.Exists(readme))
            {
                try { File.WriteAllText(readme, ReadmeText); }
                catch (IOException) { }
            }

            return true;
        }

        /// <summary>Every *.js directly inside <see cref="AppsRoot"/>, without the extension.</summary>
        internal static string[] ListUserApps()
        {
            if (!Directory.Exists(AppsRoot)) return new string[0];

            string[] files = Directory.GetFiles(AppsRoot, "*.js", SearchOption.TopDirectoryOnly);
            var names = new string[files.Length];
            for (int i = 0; i < files.Length; i++) names[i] = Path.GetFileNameWithoutExtension(files[i]);
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private const string ReadmeText =
@"# NowUI - your web applications

This folder is **yours**. NowUI created it once, the first time you opened
`Tools > NowUI > Web Preview`, and the package never writes here again. Upgrading NowUI will not touch it.

## The loop

1. Edit `apps/app.js` in any editor you like (or point an AI at it).
2. Save.
3. Press F5 in the browser tab NowUI opened.

Unity is not involved after the first launch and reimports nothing - that is why this folder is outside
`Assets/`. A `.js` file under `Assets/` would be imported as a TextAsset and every save would cost you an
AssetDatabase reimport.

Add `&watch=1` to the URL and the page reloads itself when the file changes, so step 3 goes away.

## Which file is served

`?app=NAME` serves `apps/NAME.js` from THIS folder if it exists, and falls back to the sample of the same name
inside the package's bundle if it does not. So:

* edit `apps/app.js` -> `?app=app` shows your version;
* delete `apps/app.js` -> `?app=app` shows the shipped sample again;
* add `apps/mine.js` -> `?app=mine` shows it, with no configuration anywhere.

`NAME` may hold letters, digits, `-` and `_`. It is a bare file name, never a path.

## What an application looks like

```js
import { start, ui } from './nowui/nowui.js';

let clicks = 0;

start(() => {
    ui.text('Hello from my own file');
    if (ui.button('Click me')) clicks++;
    ui.text('clicks: ' + clicks);
});
```

`./nowui/nowui.js` is served by the same local server, out of the package's bundle. Do not copy it here - both
halves of the bridge have to reach the same module instance, and they only do that if the URL is the shared one.

## Showing it to someone

Add `&shot=1` for a still, or `&clip=5` for a five-second animation, and the page records ITSELF and hands the
bytes back to the Editor, which writes them to `captures/` beside this file. `&name=NAME` chooses the file name.

**Keep the browser window visible and in front while it records.** A browser stops drawing entirely in a tab that
is hidden, minimised or behind another window, so a capture taken there would be blank. NowUI counts the frames
and writes a `.txt` saying what happened rather than an empty picture.

A clip is an animated WebP by default, which is the format that renders inline in a chat or a pull request.
`&format=webm` records with the browser's own video encoder instead: a much smaller file that has to be opened.

## No network

Everything is served from `http://127.0.0.1` by the Unity Editor, out of files already on your disk. Nothing is
fetched, uploaded or phoned home, and the preview works with the network cable out. A capture goes from the page
to the Editor on that same loopback socket and no further.
";
    }
}
