#!/bin/bash
# capture-area.sh <area> [out-dir] [port] -- one NowUI gallery area, captured in a FRESH browser profile.
#
# The fresh profile is not a nicety. Docs/Standalone/M2-Scouting.md, "The browser cache will lie to you": the .NET
# loader caches per URL, survives Cache API deletion, a clean rebuild and query-string busting, and will happily
# serve a build that no longer exists on disk. Hours were lost to that. Every run here starts from nothing.
#
# Headless capture is also intermittent - roughly one run in three returns a ~4 KB blank PNG with no console output
# at all - so a suspiciously small file is retried rather than trusted. If a real area ever legitimately renders
# under 12 KB, raise the threshold rather than removing the retry.
#
# For anything that has to be DRIVEN rather than looked at, use drive.mjs beside this file: headless --screenshot
# cannot dispatch input.
set -u
AREA=${1:?usage: capture-area.sh <area> [out-dir] [port]}
OUT=${2:-artifacts/local/features}
PORT=${3:-5103}
CHROME=${CHROME:-"/c/Program Files/Google/Chrome/Application/chrome.exe"}
PROFILE_ROOT=$(mktemp -d)

mkdir -p "$OUT"

for try in 1 2 3; do
  PROFILE="$PROFILE_ROOT/$AREA-$try"
  rm -rf "$PROFILE"
  rm -f "$OUT/area-$AREA.png"

  timeout 120 "$CHROME" --headless=new     --use-angle=swiftshader --enable-unsafe-swiftshader --disable-gpu-sandbox     --user-data-dir="$PROFILE" --virtual-time-budget=35000 --window-size=1180,760     --enable-logging=stderr --v=0     --screenshot="$OUT/area-$AREA.png"     "http://localhost:$PORT/?capture=1&area=$AREA" 2> "$PROFILE_ROOT/console-$AREA.txt"

  SIZE=$(stat -c%s "$OUT/area-$AREA.png" 2>/dev/null || echo 0)
  [ "$SIZE" -gt 12000 ] && break
  echo "  (blank capture on try $try: $SIZE bytes; retrying)"
done

echo "$AREA: $SIZE bytes -> $OUT/area-$AREA.png"
grep -o 'CONSOLE:[0-9]*] ".*' "$PROFILE_ROOT/console-$AREA.txt" | grep -v 'dotnet.js' || true
rm -rf "$PROFILE_ROOT"
