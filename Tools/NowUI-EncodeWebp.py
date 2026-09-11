"""Compatibility entry point for the packaged opaque, looping WebP encoder.

Encoding behavior and its quality/alpha rationale live in
Assets/NowUI/Native~/encode-webp.py.
"""

from pathlib import Path
import runpy


if __name__ == "__main__":
    runpy.run_path(
        str(Path(__file__).resolve().parents[1] / "Assets/NowUI/Native~/encode-webp.py"),
        run_name="__main__",
    )
