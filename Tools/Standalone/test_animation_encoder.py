"""Validate the packaged animation encoder with Python/Pillow and PowerShell 7.

Run: python Tools/Standalone/test_animation_encoder.py
Every packaged invocation runs from a fresh package-only directory outside the checkout.
"""

import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / "Assets/NowUI/Native~"


class AnimationEncoderTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="nowui encoder ")
        self.addCleanup(self.temporary.cleanup)
        self.work = Path(self.temporary.name)
        self.package = self.work / "Package with spaces/Native~"
        self.package.mkdir(parents=True)
        for name in ["encode-animation.ps1", "encode-webp.py"]:
            shutil.copy2(PACKAGE / name, self.package / name)
        self.frames = self.work / "Recorded frames"
        self.frames.mkdir()
        self.output = self.work / "Output with spaces/animation.webp"

    def record(self, count=7, fps=29.97, digits=6):
        for index in range(count):
            # Distinct colors prevent WebP from merging identical adjacent frames.
            image = Image.new("RGBA", (16, 12), ((37 * index) % 256, (71 * index) % 256, (113 * index) % 256, 128))
            image.save(self.frames / f"frame-{index:0{digits}d}.png")
            image.close()
        (self.frames / "animation.json").write_text(json.dumps({
            "frames": count, "fps": fps, "pattern": f"frame-%0{digits}d.png"
        }))

    def run_encoder(self, script=None, output=None, *extra):
        return subprocess.run([
            "pwsh", "-NoProfile", "-File", str(script or self.package / "encode-animation.ps1"),
            "-Frames", str(self.frames), "-Output", str(output or self.output),
            "-Python", sys.executable, *extra
        ], cwd=self.work, capture_output=True, text=True, timeout=60)

    def assert_encoded(self, output, count, fps):
        expected = [round((index + 1) * 1000 / fps) - round(index * 1000 / fps) for index in range(count)]
        with Image.open(output) as image:
            self.assertEqual(image.n_frames, count)
            self.assertEqual(image.info["loop"], 0)
            durations = []
            for index in range(count):
                image.seek(index)
                image.load()
                self.assertEqual(image.size, (16, 12))
                self.assertEqual(image.convert("RGBA").getchannel("A").getextrema(), (255, 255))
                durations.append(image.info["duration"])
            self.assertEqual(durations, expected)
            self.assertEqual(sum(durations), round(count * 1000 / fps))

    def assert_success(self, result):
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_package_only_encoding_preserves_cumulative_timing_and_opacity(self):
        self.record(count=96, fps=24)
        result = self.run_encoder()
        self.assert_success(result)
        self.assertEqual(Path(result.stdout.strip()), self.output)
        self.assert_encoded(self.output, 96, 24)

    def test_fractional_fps_and_powershell_compatibility_wrapper(self):
        self.record()
        for options in [(), ("-Quality", "37", "-Method", "0")]:
            with self.subTest(options=options):
                self.assert_success(self.run_encoder(None, None, *options))
                wrapped = self.output.with_name("wrapped.webp")
                self.assert_success(self.run_encoder(ROOT / "Tools/Encode-NowUINativeAnimation.ps1", wrapped, *options))
                self.assertEqual(self.output.read_bytes(), wrapped.read_bytes())
                self.assert_encoded(wrapped, 7, 29.97)

    def test_python_compatibility_wrapper_preserves_harness_defaults(self):
        self.record(count=7, fps=24, digits=4)
        outputs = []
        for name, script in [("packaged", self.package / "encode-webp.py"), ("wrapper", ROOT / "Tools/NowUI-EncodeWebp.py")]:
            output = self.work / (name + ".webp")
            result = subprocess.run([
                sys.executable, str(script), "--frames", str(self.frames), "--count", "7",
                "--fps", "24", "--output", str(output)
            ], cwd=self.work, capture_output=True, text=True, timeout=60)
            self.assert_success(result)
            self.assert_encoded(output, 7, 24)
            outputs.append(output.read_bytes())
        self.assertEqual(*outputs)

    def test_missing_frame_preserves_existing_output(self):
        self.record()
        self.assert_success(self.run_encoder())
        original = self.output.read_bytes()
        (self.frames / "frame-000003.png").unlink()
        result = self.run_encoder()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Missing frame", result.stderr)
        self.assertEqual(self.output.read_bytes(), original)
        self.assertEqual(list(self.output.parent.glob("*.tmp")), [])

    def test_partial_encoder_failure_cleans_temporary_and_preserves_existing_output(self):
        self.record()
        self.assert_success(self.run_encoder())
        original = self.output.read_bytes()
        # The copied helper simulates a dependency failure after it has opened its destination.
        (self.package / "encode-webp.py").write_text(
            "from pathlib import Path\nimport sys\n"
            "Path(sys.argv[sys.argv.index('--output') + 1]).write_bytes(b'partial encoding')\n"
            "sys.exit(9)\n"
        )
        result = self.run_encoder()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.output.read_bytes(), original)
        self.assertEqual(list(self.output.parent.glob("*.tmp")), [])

    def test_invalid_manifest_preserves_existing_output(self):
        self.record()
        self.assert_success(self.run_encoder())
        original = self.output.read_bytes()
        for field, value in [("frames", 0), ("fps", 0), ("pattern", "frame-%04d.png")]:
            with self.subTest(field=field):
                manifest = {"frames": 7, "fps": 29.97, "pattern": "frame-%06d.png", field: value}
                (self.frames / "animation.json").write_text(json.dumps(manifest))
                result = self.run_encoder()
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(self.output.read_bytes(), original)
                self.assertEqual(list(self.output.parent.glob("*.tmp")), [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
