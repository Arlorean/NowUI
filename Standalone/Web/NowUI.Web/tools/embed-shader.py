#!/usr/bin/env python3
"""Turn a wwwroot/shaders/*.vert|frag entry file into the JavaScript template
literal nowui-gl.js embeds.

WHY THIS EXISTS. nowui-gl.js carries the shader sources inline, as template
literals, because it is a plain ES module that compiles at init with no fetch
step. wwwroot/shaders/*.vert|frag carry the SAME sources, annotated line by
line against the HLSL they came from. Every port before this one kept the two
in step by hand, and a hand copy of 700 lines of GLSL is a divergence waiting
to happen -- the kind that renders a plausible picture from the file a reviewer
reads while the browser executes something else.

So the SDF program's embedded copy is GENERATED from its annotated file:

    python tools/embed-shader.py wwwroot/shaders/nowui-sdf.vert
    python tools/embed-shader.py wwwroot/shaders/nowui-sdf.frag

Each prints the constant to stdout; paste it into nowui-gl.js. Re-run it after
ANY edit to the .vert/.frag and paste again -- the file is the source, the
constant is a build product.

WHAT IT DOES, EXACTLY. Three things and nothing else, so that "generated" means
"the same program", not "something like it":

  1. `//#include "name.glsl"` becomes the ${...} interpolation of the constant
     nowui-gl.js already holds for that include, so the mask and colour-space
     code is not duplicated a third time.
  2. Full-line `//` comments are dropped and trailing `//` comments are cut.
     GLSL has no string literals, so there is no `//` that is not a comment,
     which is what makes this safe to do textually.
  3. Runs of blank lines collapse to one.

It does NOT reformat, reorder, rename or otherwise touch a line of code. Diff
the output against the file with the comments stripped and it is identical.
"""

import re
import sys
from pathlib import Path

INCLUDE = re.compile(r'^[ \t]*//#include[ \t]+"([^"]+)"[ \t]*$')

# Include file -> the nowui-gl.js constant that already holds it.
CONSTANTS = {
    'nowui-mask.glsl': 'GLSL_MASK',
    'nowui-colorspace.glsl': 'GLSL_COLOR_SPACE',
    'nowui-sdf-image.glsl': 'GLSL_SDF_IMAGE_COMMON',
    'nowui-text-gradient.glsl': 'GLSL_TEXT_GRADIENT',
}

# Entry file -> the constant name to emit.
NAMES = {
    'nowui-sdf.vert': 'GLSL_VERTEX_SDF',
    'nowui-sdf.frag': 'GLSL_FRAGMENT_SDF',
    # Hidden/NowUI/SDF Image Field: one vertex stage (vert_img), five fragment
    # stages, and a shared CGINCLUDE block that is BOTH an entry file (so the
    # tool can generate its constant) and an include (so the five passes splice
    # it rather than each carrying a copy).
    'nowui-sdf-image.glsl': 'GLSL_SDF_IMAGE_COMMON',
    'nowui-sdf-image.vert': 'GLSL_VERTEX_SDF_IMAGE',
    'nowui-sdf-image-seed.frag': 'GLSL_FRAGMENT_SDF_IMAGE_SEED',
    'nowui-sdf-image-flood.frag': 'GLSL_FRAGMENT_SDF_IMAGE_FLOOD',
    'nowui-sdf-image-resolve.frag': 'GLSL_FRAGMENT_SDF_IMAGE_RESOLVE',
    'nowui-sdf-image-stamp.frag': 'GLSL_FRAGMENT_SDF_IMAGE_STAMP',
    'nowui-sdf-image-dilate.frag': 'GLSL_FRAGMENT_SDF_IMAGE_DILATE',
    # NowUITextGradient.cginc, the text shader's gradient fill. It is an
    # include rather than an entry file -- nowui-text.frag splices it -- and it
    # is registered in BOTH tables for that reason: NAMES so this tool can
    # generate its constant, CONSTANTS so nowui-text.frag's marker resolves to
    # that constant if the .frag itself is ever generated too.
    #
    # nowui-text.frag is NOT in NAMES, and that is a known gap rather than an
    # oversight: the slice-1 pair (nowui-rectangle and nowui-text) name the
    # outline varying `vOutlineColor` in the annotated files and `vOutline` in
    # nowui-gl.js, and they share one varyings block there that has no include
    # file here. Generating either would mean renaming a varying across four
    # files. Until that is done, the hand-maintained part of the text program
    # is its ~150 lines of body; the gradient maths -- the part with four
    # HLSL->GLSL traps in it -- is generated.
    'nowui-text-gradient.glsl': 'GLSL_TEXT_GRADIENT',
}


def strip(line: str) -> str:
    """Remove a comment, whole-line or trailing. Returns None for a dropped line."""
    stripped = line.lstrip()
    if stripped.startswith('//'):
        return None
    index = line.find('//')
    if index >= 0:
        return line[:index].rstrip()
    return line.rstrip()


def convert(path: Path) -> str:
    lines = path.read_text(encoding='utf-8').split('\n')
    out = []

    for raw in lines:
        match = INCLUDE.match(raw)
        if match:
            constant = CONSTANTS.get(match.group(1))
            if constant is None:
                raise SystemExit(f'no nowui-gl.js constant is registered for "{match.group(1)}"')
            out.append('${' + constant + '}')
            continue

        text = strip(raw)
        if text is None:
            continue
        if text == '' and out and out[-1] == '':
            continue
        out.append(text)

    while out and out[-1] == '':
        out.pop()

    body = '\n'.join(out)

    if '`' in body or '\\' in body:
        raise SystemExit('the source contains a backtick or a backslash; the template literal would break')

    return body


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)

    path = Path(sys.argv[1])
    name = NAMES.get(path.name)

    if name is None:
        raise SystemExit(f'no constant name is registered for "{path.name}"; add one to NAMES')

    print(f'// GENERATED from wwwroot/shaders/{path.name} by tools/embed-shader.py -- edit that file, not this.')
    print(f'const {name} = `{convert(path)}\n`;')


if __name__ == '__main__':
    main()
