from __future__ import annotations

import copy
import hashlib
import http.server
import importlib.util
import json
import subprocess
import sys
import tempfile
import threading
import unittest
import zipfile
from pathlib import Path
from unittest import mock


SCRIPT = Path(__file__).resolve().parents[1] / "ffmpeg-assets.py"
SPEC = importlib.util.spec_from_file_location("ffmpeg_assets", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
ffmpeg_assets = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = ffmpeg_assets
SPEC.loader.exec_module(ffmpeg_assets)


def digest(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def valid_manifest(version: str = "btbn-autobuild-2026-08-12-13-15") -> dict:
    assets = {}
    for rid in ffmpeg_assets.EXPECTED_RIDS:
        archive_name = f"ffmpeg-{rid}-{digest(rid)[:16]}.zip"
        entry = {
            "url": f"https://github.com/crazysmile-PhD/downkyi-runtime-assets/releases/download/ffmpeg-{version}/{archive_name}",
            "sha256": digest(rid),
            "fileName": archive_name,
            "provenance": {
                "upstreamRepository": "https://github.com/example/source",
                "upstreamRelease": "fixed-release",
                "originalAssetName": f"upstream-{rid}.zip",
                "upstreamUrl": f"https://example.invalid/upstream-{rid}.zip",
                "mirroredAt": "2026-08-13T00:00:00Z",
                "ffmpegVersion": "N-126086",
            },
        }
        if rid.startswith("osx-"):
            probe_name = f"ffprobe-{rid}-{digest(rid + '-probe')[:16]}.zip"
            entry.update({
                "ffprobeUrl": f"https://github.com/crazysmile-PhD/downkyi-runtime-assets/releases/download/ffmpeg-{version}/{probe_name}",
                "ffprobeSha256": digest(rid + "-probe"),
                "ffprobeFileName": probe_name,
            })
        assets[rid] = entry
    return {
        "ffmpeg": {
            "version": version,
            "requiredRids": list(ffmpeg_assets.EXPECTED_RIDS),
            "mirror": {
                "repository": "crazysmile-PhD/downkyi-runtime-assets",
                "retention": "never-delete",
                "tagPrefix": "ffmpeg-",
            },
            "assets": assets,
        }
    }


def candidate_for(rid: str = "win-x64") -> dict:
    source_name = f"ffmpeg-N-126086-{rid}.zip"
    return {
        "version": "btbn-autobuild-2026-08-12-13-15",
        "assets": {
            rid: {
                "upstreamRepository": "https://github.com/BtbN/FFmpeg-Builds",
                "upstreamRelease": "autobuild-2026-08-12-13-15",
                "ffmpegVersion": "N-126086",
                "files": [{
                    "role": "ffmpeg",
                    "sourceUrl": f"https://example.invalid/{source_name}",
                    "sha256": digest(source_name),
                    "originalAssetName": source_name,
                    "mirroredFileName": f"ffmpeg-{rid}-autobuild-2026-08-12-13-15-{digest(source_name)[:16]}.zip",
                }],
            }
        },
    }


class AssetRequestHandler(http.server.BaseHTTPRequestHandler):
    def do_HEAD(self) -> None:  # noqa: N802
        self.send_response(404 if self.path.endswith("missing.zip") else 200)
        self.send_header("Content-Length", "1")
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        self.do_HEAD()
        if not self.path.endswith("missing.zip"):
            self.wfile.write(b"x")

    def log_message(self, _format: str, *_args: object) -> None:
        return


class FfmpegAssetsTests(unittest.TestCase):
    def test_manifest_requires_every_rid_url_and_checksum(self) -> None:
        manifest = valid_manifest()
        ffmpeg_assets.validate_manifest(manifest)
        del manifest["ffmpeg"]["assets"]["linux-x64"]["sha256"]
        with self.assertRaisesRegex(ffmpeg_assets.AssetError, "linux-x64.*SHA-256"):
            ffmpeg_assets.validate_manifest(manifest)

    def test_manifest_rejects_missing_url(self) -> None:
        manifest = valid_manifest()
        del manifest["ffmpeg"]["assets"]["linux-x64"]["url"]
        with self.assertRaisesRegex(ffmpeg_assets.AssetError, "linux-x64.*url"):
            ffmpeg_assets.validate_manifest(manifest)

    def test_manifest_rejects_mutable_latest_url(self) -> None:
        manifest = valid_manifest()
        manifest["ffmpeg"]["assets"]["win-x64"]["url"] = (
            "https://github.com/crazysmile-PhD/downkyi-runtime-assets/releases/download/latest/ffmpeg.zip"
        )
        with self.assertRaisesRegex(ffmpeg_assets.AssetError, "fixed project-owned"):
            ffmpeg_assets.validate_manifest(manifest)

    def test_manifest_rejects_non_project_owned_url(self) -> None:
        manifest = valid_manifest()
        manifest["ffmpeg"]["assets"]["win-x64"]["url"] = (
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-1/ffmpeg.zip"
        )
        with self.assertRaisesRegex(ffmpeg_assets.AssetError, "project-owned"):
            ffmpeg_assets.validate_manifest(manifest)

    def test_manifest_rejects_duplicate_or_missing_rids(self) -> None:
        manifest = valid_manifest()
        manifest["ffmpeg"]["requiredRids"].append("win-x64")
        with self.assertRaisesRegex(ffmpeg_assets.AssetError, "duplicate"):
            ffmpeg_assets.validate_manifest(manifest)
        manifest = valid_manifest()
        manifest["ffmpeg"]["requiredRids"].remove("linux-arm64")
        with self.assertRaisesRegex(ffmpeg_assets.AssetError, "complete supported"):
            ffmpeg_assets.validate_manifest(manifest)

    def test_current_candidate_is_a_no_op(self) -> None:
        manifest = valid_manifest()
        candidate = candidate_for()
        entry = manifest["ffmpeg"]["assets"]["win-x64"]
        entry["sha256"] = candidate["assets"]["win-x64"]["files"][0]["sha256"]
        entry["provenance"]["upstreamRelease"] = candidate["assets"]["win-x64"]["upstreamRelease"]
        self.assertTrue(ffmpeg_assets.candidate_is_current(manifest, candidate))

    def test_discovery_rejects_incomplete_release(self) -> None:
        incomplete = [{
            "tag_name": "autobuild-2026-08-12-13-15",
            "draft": False,
            "prerelease": False,
            "assets": [{
                "name": "ffmpeg-N-126086-win64-gpl.zip",
                "digest": f"sha256:{digest('win')}",
                "browser_download_url": "https://example.invalid/win.zip",
            }],
        }]
        with mock.patch.object(ffmpeg_assets, "release_assets", return_value=incomplete):
            with self.assertRaisesRegex(ffmpeg_assets.AssetError, "No complete fixed upstream release"):
                ffmpeg_assets.find_release("example/source", {
                    "win-x64": __import__("re").compile(r"ffmpeg-.+-win64-gpl\\.zip"),
                    "linux-x64": __import__("re").compile(r"ffmpeg-.+-linux64-gpl\\.tar\\.xz"),
                })

    def test_package_downloader_verifier_rejects_checksum_mismatch(self) -> None:
        self.assertIn("ffmpeg-assets.py", (SCRIPT.parent / "ffmpeg.ps1").read_text(encoding="utf-8"))
        self.assertIn("ffmpeg-assets.py", (SCRIPT.parent / "ffmpeg.sh").read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as temporary:
            asset = Path(temporary) / "asset.bin"
            asset.write_bytes(b"known bytes")
            result = subprocess.run(
                [sys.executable, str(SCRIPT), "verify-file", "--path", str(asset), "--sha256", digest("wrong")],
                capture_output=True,
                text=True,
                check=False,
            )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Checksum mismatch", result.stderr)

    def test_candidate_validation_rejects_missing_ffprobe(self) -> None:
        candidate = candidate_for("linux-x64")
        file = candidate["assets"]["linux-x64"]["files"][0]
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            archive = directory / file["mirroredFileName"]
            with zipfile.ZipFile(archive, "w") as source:
                source.writestr("bin/ffmpeg", b"not an executable")
            with self.assertRaisesRegex(ffmpeg_assets.AssetError, "ffprobe"):
                ffmpeg_assets.validate_candidate_archive(candidate, "linux-x64", directory, False, None)

    def test_mirror_failure_leaves_manifest_unchanged(self) -> None:
        manifest = valid_manifest()
        before = copy.deepcopy(manifest)
        with self.assertRaisesRegex(ffmpeg_assets.AssetError, "Mirror upload evidence is missing"):
            ffmpeg_assets.apply_update(manifest, candidate_for(), {
                "repository": "crazysmile-PhD/downkyi-runtime-assets",
                "assets": {},
            })
        self.assertEqual(before, manifest)

    def test_successful_mirror_evidence_updates_only_the_selected_rid(self) -> None:
        manifest = valid_manifest()
        candidate = candidate_for()
        mirror = ffmpeg_assets.record_mirror(
            candidate,
            "crazysmile-PhD/downkyi-runtime-assets",
            "ffmpeg-btbn-autobuild-2026-08-12-13-15",
            "2026-08-13T00:00:00Z",
        )
        updated = ffmpeg_assets.apply_update(manifest, candidate, mirror)
        self.assertEqual(mirror["assets"]["win-x64"], updated["ffmpeg"]["assets"]["win-x64"])
        self.assertEqual(manifest["ffmpeg"]["assets"]["linux-x64"], updated["ffmpeg"]["assets"]["linux-x64"])

    def test_historical_mirror_manifest_is_valid(self) -> None:
        manifest = valid_manifest("btbn-autobuild-2025-01-01-00-00")
        ffmpeg_assets.validate_manifest(manifest)

    def test_preflight_reports_the_broken_rid(self) -> None:
        manifest = valid_manifest()
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), AssetRequestHandler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            for asset in manifest["ffmpeg"]["assets"].values():
                asset["url"] = f"http://127.0.0.1:{server.server_port}/available.zip"
                if "ffprobeUrl" in asset:
                    asset["ffprobeUrl"] = f"http://127.0.0.1:{server.server_port}/available.zip"
            manifest["ffmpeg"]["assets"]["linux-x64"]["url"] = f"http://127.0.0.1:{server.server_port}/missing.zip"
            with mock.patch.object(ffmpeg_assets, "validate_manifest"):
                with self.assertRaisesRegex(ffmpeg_assets.AssetError, "tool=ffmpeg\\nrid=linux-x64"):
                    ffmpeg_assets.preflight(manifest, 5)
        finally:
            server.shutdown()
            thread.join()
            server.server_close()


if __name__ == "__main__":
    unittest.main()
