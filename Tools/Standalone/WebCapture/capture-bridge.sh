#!/bin/bash
# capture-bridge.sh <mode> [out-dir] [port] -- one ?bridge=MODE frame, captured in a FRESH browser profile,
# with the page's own report dumped alongside the PNG.
#
# The same two browser facts capture-area.sh records apply here and are not negotiable:
#
#   * the .NET loader caches per URL and survives a cache clear, a rebuild and a query-string bust, so every run
#     starts from an empty profile directory;
#   * headless capture is intermittently blank, roughly one run in three, so a suspiciously small PNG is retried
#     rather than trusted.
#
# What this adds over capture-area.sh is --dump-dom. W2's acceptance has two halves - a word on the canvas and a
# refusal naming a slot offset - and the second one is deliberately NOT on the canvas, because "NowUI is never
# touched" means nothing was drawn. The report <pre> is where it is.
set -u
MODE=${1:?usage: capture-bridge.sh <mode> [out-dir] [port]}
OUT=${2:-artifacts/local/bridge}
PORT=${3:-5103}
CHROME=${CHROME:-"/c/Program Files/Google/Chrome/Application/chrome.exe"}
PROFILE_ROOT=$(mktemp -d)

mkdir -p "$OUT"

# --screenshot needs an ABSOLUTE path or it silently writes nothing, and on Git Bash it needs the WINDOWS form:
# Chrome does not know what /d/... is. `pwd -W` gives D:/..., with a POSIX pwd as the fallback elsewhere.
OUTABS=$(cd "$OUT" && { pwd -W 2>/dev/null || pwd; })

for try in 1 2 3; do
  PROFILE="$PROFILE_ROOT/$MODE-$try"
  rm -rf "$PROFILE"
  rm -f "$OUT/bridge-$MODE.png"

  timeout 120 "$CHROME" --headless=new \
    --use-angle=swiftshader --enable-unsafe-swiftshader --disable-gpu-sandbox \
    --user-data-dir="$PROFILE" --virtual-time-budget=35000 --window-size=900,620 \
    --enable-logging=stderr --v=0 \
    --screenshot="$OUTABS/bridge-$MODE.png" \
    "http://localhost:$PORT/?capture=1&bridge=$MODE&size=900x620" 2> "$PROFILE_ROOT/console-$MODE.txt"

  SIZE=$(stat -c%s "$OUT/bridge-$MODE.png" 2>/dev/null || echo 0)
  [ "$SIZE" -gt 12000 ] && break
  echo "  (blank capture on try $try: $SIZE bytes; retrying)"
done

# The report is IN the PNG: bridge.js paints it into a fixed <pre> over the canvas, and the browser composites
# that into the screenshot. A --dump-dom second load was tried and dropped - the <pre> is created asynchronously
# and appears in roughly half of the dumps, which is a worse oracle than the picture that already has it.
#
# The console goes to a log beside the PNG, because the multi-line report lines are what a grep cannot carry and
# the log can.
cp "$PROFILE_ROOT/console-$MODE.txt" "$OUT/bridge-$MODE.log" 2>/dev/null || true

echo "$MODE: $SIZE bytes -> $OUT/bridge-$MODE.png"
echo "--- console (first line of each message) ---"
grep -o 'CONSOLE:[0-9]*] ".*' "$OUT/bridge-$MODE.log" | grep -v 'dotnet.js' || true
rm -rf "$PROFILE_ROOT"
