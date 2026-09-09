"""Fail the bundle build if leaning a font renumbered the glyph ids its baked pages were baked against.

WHY THIS EXISTS. Two steps of the bundle build disagree about which font they are talking about, and nothing else
notices:

  * The baked atlas pages in Fixtures/NowUI/*.page<N>.bin are baked IN UNITY, from the FULL TrueType face. Besides one
    record per codepoint, each page carries a record per GLYPH INDEX, keyed as -1 - glyphIndex (NowFont.cs:5365), so a
    shaper can look a glyph up by index without going through the cmap.
  * (d1) in Build-NowUIWebBundle.ps1 then runs fontTools.subset over the staged .ttf to drop the OpenType layout
    tables. A subsetter is entitled to renumber glyph ids.

If it ever does, those index records point at the wrong outlines - "a" would be drawn with the pixels of some other
letter, at the right advance, with no error anywhere. Today the records are inert in the browser (HarfBuzz is not
linked into wasm, so NowTextShaper.supported is false and every draw takes the per-codepoint path) and the check
passes with zero mismatches. It exists so the day a subsetter option changes, the build stops instead of the text
quietly becoming wrong.

IF A MISMATCH IS EVER REPORTED, the fix is not to disable this: drop the negative-keyed records from the exported
pages. The codepoint records alone are still correct and still fast.

Usage: python Check-LeanedGlyphIndices.py <staged-fixtures-dir>/NowUI
Exits 0 with a one-line summary, or 1 naming every mismatch.
"""
import glob
import json
import os
import sys

from fontTools.ttLib import TTFont


def bounds_key(record):
    """A glyph-index record shares its codepoint record's cell, so the geometry identifies the pair."""
    a, p = record["atlasBounds"], record["planeBounds"]
    return (a["left"], a["bottom"], a["right"], a["top"],
            p["left"], p["bottom"], p["right"], p["top"],
            record["advance"])


def check_face(manifest_path):
    with open(manifest_path, encoding="utf-8") as f:
        face = json.load(f)

    pages = face.get("bakedPages")
    if not pages:
        return 0, 0, []

    ttf = os.path.join(os.path.dirname(manifest_path), face["fontBytesFile"])
    font = TTFont(ttf, lazy=True)
    order = font.getGlyphOrder()
    cmap = font.getBestCmap()

    compared, mismatches = 0, []

    for page in pages:
        by_geometry = {}
        for record in page["glyphs"]:
            if record["unicode"] >= 0:
                by_geometry.setdefault(bounds_key(record), []).append(record["unicode"])

        for record in page["glyphs"]:
            if record["unicode"] >= 0:
                continue

            baked_index = -1 - record["unicode"]
            codepoints = by_geometry.get(bounds_key(record), [])

            # Pair only where the geometry is unambiguous. Two codepoints sharing one outline would make the pairing
            # a guess, and a guess is not worth failing a build over.
            if len(codepoints) != 1:
                continue

            codepoint = codepoints[0]
            name = cmap.get(codepoint)
            if name is None:
                mismatches.append(
                    "%s: U+%04X is baked but the leaned face no longer maps it" % (os.path.basename(ttf), codepoint))
                continue

            leaned_index = order.index(name)
            compared += 1
            if leaned_index != baked_index:
                mismatches.append(
                    "%s: U+%04X was baked against glyph id %d but the leaned face numbers it %d"
                    % (os.path.basename(ttf), codepoint, baked_index, leaned_index))

    return 1, compared, mismatches


def main():
    root = sys.argv[1]
    manifests = sorted(glob.glob(os.path.join(root, "*.font.json")))
    if not manifests:
        print("no face manifests under " + root, file=sys.stderr)
        return 1

    faces = compared = 0
    mismatches = []
    for manifest in manifests:
        f, c, m = check_face(manifest)
        faces += f
        compared += c
        mismatches += m

    if mismatches:
        print("Baked glyph indices no longer match the leaned faces:", file=sys.stderr)
        for line in mismatches:
            print("  " + line, file=sys.stderr)
        return 1

    if faces == 0:
        print("  no baked pages in the staged fixtures; nothing to check")
        return 0

    print("  glyph ids stable across leaning: %d comparisons over %d face(s), 0 mismatched" % (compared, faces))
    return 0


sys.exit(main())
