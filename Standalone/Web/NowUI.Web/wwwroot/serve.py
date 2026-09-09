#!/usr/bin/env python3
"""Serve the NowUI browser bundle, with no Unity and nothing to install.

    python serve.py                 # from inside the bundle folder
    python serve.py --port 8080
    python serve.py --project /path/to/UnityProject

WHY THIS SHIPS. The Editor's Web Preview window is for AUTHORING: it reloads when you save, and it is the right
tool when a person is iterating on an app. It is the wrong tool for handing someone a running page, because it
needs the Editor open and a menu item clicked. Anyone who can run a script - a CI job, a person with the project
checked out but Unity closed, or an assistant working on the project - can start this instead and produce a URL
that just works. Serving the bundle is static file I/O; none of it needs the editor that built it.

WHY IT IS NOT `python -m http.server`. THE BUNDLE IS BROTLI-ONLY. Every large file is stored as NAME.br and the
original is deleted, which is what keeps the committed artifact around a third of its raw size. A plain static
server answers 404 for every one of them, and a server that finds NAME.br and sends it without
`Content-Encoding: br` hands the browser compressed bytes it will try to parse as JavaScript. Both failures look
like "the bundle is broken" rather than "the server is wrong", so this file exists to get that one detail right.

Standard library only, deliberately: an assistant that has to `pip install` something before it can show anyone
anything has already lost. That is also why a client which does not accept brotli is REFUSED with an explanation
rather than served inflated - decompressing would need a package that is not in the standard library, and every
browser sends `Accept-Encoding: br`.
"""

import argparse
import json
import os
import posixpath
import socket
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MIME = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript",
    ".mjs": "text/javascript",
    ".json": "application/json",
    ".wasm": "application/wasm",
    ".css": "text/css",
    ".txt": "text/plain; charset=utf-8",
    ".frag": "text/plain; charset=utf-8",
    ".vert": "text/plain; charset=utf-8",
    ".glsl": "text/plain; charset=utf-8",
    ".dat": "application/octet-stream",
    ".blat": "application/octet-stream",
    ".pdb": "application/octet-stream",
    ".ttf": "font/ttf",
    ".otf": "font/otf",
    ".woff": "font/woff",
    ".woff2": "font/woff2",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".gif": "image/gif",
    ".webp": "image/webp",
    ".svg": "image/svg+xml",
    ".ico": "image/x-icon",
    ".mp4": "video/mp4",
    ".webm": "video/webm",
    # A Lottie document is JSON whatever it is named.
    ".lottie": "application/json",
    ".md": "text/markdown; charset=utf-8",
}

# What /assets/ will hand out of the project's Assets folder. The same allowlist the Editor's server uses, and
# for the same reason: this route exists so ui.image and ui.lottie can name art the project already has, and a
# loopback socket that will serve a source tree to earn that is a trade nobody asked for. Extension only - the
# question is not "is this really a PNG" but "did the author mean to publish this file over a socket", and a
# .cs or a .meta never means that.
SERVABLE_ASSETS = {
    ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico",
    ".json", ".lottie", ".ttf", ".otf", ".woff", ".woff2",
}

# What the package's own README.md is served as, so it does not collide with Documentation~/README.md.
PACKAGE_README_ALIAS = "package.md"

# Root-level modules the bundle owns. A user application named main.js must never replace the page's own boot
# script - that produces a black canvas and no explanation.
RESERVED = {"main.js", "nowui-fetch.js", "nowui-gl.js", "nowui-input.js", "serve.py"}


class Handler(BaseHTTPRequestHandler):
    bundle_root = ""
    apps_root = ""
    assets_root = ""
    docs_root = ""
    package_root = ""

    # ------------------------------------------------------------------------------------------- documentation

    def docs_index(self):
        """Every document this server can serve, as JSON.

        An index rather than a hardcoded list in the application, because the set of documents is a property of
        the package and changes with it. A viewer that listed them itself would go stale silently the first time
        a document was added.
        """
        entries = []

        overview = os.path.join(self.package_root, "README.md")
        if os.path.isfile(overview):
            entries.append({"name": "Overview", "path": "/docs/" + PACKAGE_README_ALIAS,
                            "bytes": os.path.getsize(overview)})

        if os.path.isdir(self.docs_root):
            for name in sorted(os.listdir(self.docs_root)):
                if not name.lower().endswith(".md"):
                    continue
                entries.append({"name": "Documentation index" if name == "README.md" else name[:-3],
                                "path": "/docs/" + name,
                                "bytes": os.path.getsize(os.path.join(self.docs_root, name))})

        return json.dumps(entries).encode("utf-8")

    def docs_file(self, tail):
        """One document, by name. Only .md, and only out of the package's own two documentation locations.

        THE ALIAS IS NOT DECORATION. Both the package root and Documentation~ contain a README.md, and they are
        different documents - the package's is the project overview, the folder's is the index of the other
        guides. Mapping both onto /docs/README.md silently served the first and made the second unreachable, so
        the folder keeps the natural mapping and the package's gets a name of its own.
        """
        if not tail.lower().endswith(".md") or "/" in tail:
            return None

        if tail == PACKAGE_README_ALIAS:
            candidate = os.path.join(self.package_root, "README.md")
            return candidate if os.path.isfile(candidate) else None

        candidate = os.path.join(self.docs_root, tail)
        return candidate if self.inside(self.docs_root, candidate) and os.path.isfile(candidate) else None

    # ------------------------------------------------------------------------------------------------ routing

    def resolve(self, path):
        """Where a request comes from, as (absolute path, is_user_file) or (None, False)."""
        relative = path.lstrip("/")
        if not relative:
            relative = "index.html"

        if ".." in relative.split("/"):
            return None, False

        # /docs/... - the package's own documentation, so NowUI can render the manual that describes it.
        #
        # A SEPARATE ROUTE FROM /assets/, and not simply ".md added to the allowlist", because the documentation
        # is not in the project. It ships inside the package, and for anyone who installed NowUI rather than
        # cloning it the package lives under Library/PackageCache where nothing beneath Assets/ can reach it.
        # WebBundle~ and Documentation~ are siblings under the package root, so resolving relative to the bundle
        # works identically in a clone and in an install.
        if relative.lower().startswith("docs/") or relative.lower() == "docs":
            tail = relative[5:] if len(relative) > 4 else ""
            if tail == "":
                return "\x00index", True
            return (self.docs_file(tail), True)

        # /assets/... - the project's own Assets folder, media only.
        if relative.lower().startswith("assets/"):
            tail = relative[len("assets/"):]
            if not tail or os.path.splitext(tail)[1].lower() not in SERVABLE_ASSETS:
                return None, False
            candidate = os.path.join(self.assets_root, *tail.split("/"))
            return (candidate, True) if self.inside(self.assets_root, candidate) else (None, False)

        # The author's own applications shadow the bundle, which is what makes ?app=NAME serve THEIR file.
        app = None
        if relative.endswith(".js"):
            if "/" not in relative and relative not in RESERVED:
                app = relative
            elif relative.lower().startswith("apps/") and "/" not in relative[5:]:
                app = relative[5:]

        if app and self.apps_root:
            candidate = os.path.join(self.apps_root, app)
            if self.inside(self.apps_root, candidate) and os.path.isfile(candidate):
                return candidate, True

        candidate = os.path.join(self.bundle_root, *relative.split("/"))
        if not self.inside(self.bundle_root, candidate):
            return None, False

        if os.path.isfile(candidate) or os.path.isfile(candidate + ".br"):
            return candidate, False

        # A bundle that still keeps its samples at the root, asked for as /apps/NAME.js.
        if app:
            flat = os.path.join(self.bundle_root, app)
            if self.inside(self.bundle_root, flat) and (os.path.isfile(flat) or os.path.isfile(flat + ".br")):
                return flat, False

        return None, False

    @staticmethod
    def inside(root, candidate):
        root = os.path.abspath(root)
        full = os.path.abspath(candidate)
        return full == root or full.startswith(root + os.sep)

    # -------------------------------------------------------------------------------------------------- verbs

    def do_GET(self):
        self.serve(body=True)

    def do_HEAD(self):
        self.serve(body=False)

    def serve(self, body):
        path = posixpath.normpath(self.path.split("?", 1)[0].split("#", 1)[0])
        resolved, from_user = self.resolve(path)

        if resolved == "\x00index":
            payload = self.docs_index()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            if body:
                self.wfile.write(payload)
            return

        if resolved is None:
            self.fail(404, "NowUI: '%s' was not found.\n\nLooked in:\n  %s\n  %s\n"
                           % (path, self.apps_root or "(no apps folder)", self.bundle_root))
            return

        # The LOGICAL name decides the content type: a .wasm stored as .wasm.br is still application/wasm, and
        # calling it application/brotli would stop the loader's streaming instantiation.
        on_disk, compressed = resolved, False
        if not os.path.isfile(on_disk) and os.path.isfile(resolved + ".br"):
            on_disk, compressed = resolved + ".br", True

        if not os.path.isfile(on_disk):
            self.fail(404, "NowUI: '%s' was not found." % path)
            return

        if compressed and "br" not in (self.headers.get("Accept-Encoding") or ""):
            self.fail(415,
                      "NowUI: this bundle stores '%s' brotli-compressed and the client did not send\n"
                      "Accept-Encoding: br, so it cannot be served. Every browser does send it; a command line\n"
                      "client usually needs to be asked (curl --compressed).\n" % path)
            return

        with open(on_disk, "rb") as f:
            payload = f.read()

        self.send_response(200)
        self.send_header("Content-Type", MIME.get(os.path.splitext(resolved)[1].lower(),
                                                  "application/octet-stream"))
        if compressed:
            self.send_header("Content-Encoding", "br")
        self.send_header("Content-Length", str(len(payload)))
        # The author's own files must never be cached: "I saved and nothing changed" is the worst authoring bug
        # there is. The bundle may be revalidated instead, which is free over loopback.
        self.send_header("Cache-Control", "no-store" if from_user else "no-cache")
        self.end_headers()

        if body:
            self.wfile.write(payload)

    def fail(self, code, message):
        payload = message.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, *args):
        pass


def free_port(preferred):
    """The preferred port, or the first free one after it, or an ephemeral one."""
    for candidate in [preferred] + list(range(8973, 8993)):
        with socket.socket() as probe:
            try:
                probe.bind(("127.0.0.1", candidate))
                return candidate
            except OSError:
                continue
    return 0


def main():
    here = os.path.dirname(os.path.abspath(__file__))

    parser = argparse.ArgumentParser(description="Serve the NowUI browser bundle.")
    parser.add_argument("--port", type=int, default=8973)
    parser.add_argument("--bundle", default=here, help="the bundle folder (default: beside this script)")
    parser.add_argument("--project", default=None,
                        help="the Unity project root (default: inferred from the bundle's location)")
    parser.add_argument("--app", default=None, help="print a ready-made URL for this application")
    args = parser.parse_args()

    bundle = os.path.abspath(args.bundle)
    if not os.path.isfile(os.path.join(bundle, "index.html")):
        sys.exit("No index.html in %s - point --bundle at the folder holding the NowUI web bundle." % bundle)

    # The bundle normally lives at <ProjectRoot>/Assets/NowUI/WebBundle~, so three levels up is the project.
    # Wrong guesses are harmless: the two project-derived routes simply find nothing.
    project = os.path.abspath(args.project) if args.project \
        else os.path.abspath(os.path.join(bundle, "..", "..", ".."))

    # The package root is the bundle's parent: <package>/WebBundle~ and <package>/Documentation~ are siblings
    # in a clone and in an installed copy alike, which is what lets /docs/ work without knowing which it is.
    package = os.path.abspath(os.path.join(bundle, ".."))

    Handler.bundle_root = bundle
    Handler.apps_root = os.path.join(project, "NowUI", "apps")
    Handler.assets_root = os.path.join(project, "Assets")
    Handler.package_root = package
    Handler.docs_root = os.path.join(package, "Documentation~")

    port = free_port(args.port)
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    port = server.server_address[1]

    apps = []
    if os.path.isdir(Handler.apps_root):
        apps = sorted(f[:-3] for f in os.listdir(Handler.apps_root) if f.endswith(".js"))

    print("NowUI is serving on http://127.0.0.1:%d/" % port)
    print("  bundle   %s" % bundle)
    print("  apps     %s%s" % (Handler.apps_root, "" if os.path.isdir(Handler.apps_root) else "   (not present)"))
    print("  assets   /assets/... -> %s" % Handler.assets_root)

    doc_count = len([f for f in os.listdir(Handler.docs_root) if f.endswith(".md")]) \
        if os.path.isdir(Handler.docs_root) else 0
    print("  docs     /docs/    -> %s   (%d documents)" % (Handler.docs_root, doc_count))

    if args.app:
        print("\n  http://127.0.0.1:%d/?app=%s" % (port, args.app))
    elif apps:
        print("\nApplications found:")
        for name in apps:
            print("  http://127.0.0.1:%d/?app=%s" % (port, name))

    print("\nCtrl+C to stop.", flush=True)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("stopped")


if __name__ == "__main__":
    main()
