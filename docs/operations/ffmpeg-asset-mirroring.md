# FFmpeg Asset Mirroring

## Ownership boundary

`script/assets/external-assets.json` is the sole production owner of every
FFmpeg/ffprobe URL and SHA-256. Package scripts only consume that manifest;
they do not query an upstream release API, resolve `latest`, select a version,
or fall back to an unverified executable.

The updater is the only component allowed to query upstream releases. Its
normal source policy is deliberately platform-specific:

| RID | Discovery source | Required flavor |
| --- | --- | --- |
| `win-x64`, `linux-x64`, `linux-arm64` | `BtbN/FFmpeg-Builds` GitHub Releases API | fixed `autobuild-*`, static GPL archive, `ffmpeg` and `ffprobe` |
| `win-x86` | `yt-dlp/FFmpeg-Builds` GitHub Releases API during the first mirror import | fixed `autobuild-*`, GPL archive with `ffprobe` |
| `osx-x64`, `osx-arm64` | existing reviewed martin-riedl pinned source during the first mirror import | separate FFmpeg/ffprobe archives, preserved without HTML scraping |

The scheduled updater changes only the BtbN RIDs. martin-riedl is not scraped:
it does not publish a suitable release API, so a macOS refresh is a deliberate
operator-reviewed bootstrap instead of a fragile HTML parser.

## Immutable mirror policy

The mirror repository is `crazysmile-PhD/downkyi-runtime-assets`. It must be a
dedicated, project-owned repository before the first bootstrap. Each update
creates one new GitHub Release named `ffmpeg-<fixed-upstream-version>` and
uploads filenames that contain the RID, fixed upstream tag, and SHA-256 prefix.
The workflow refuses to reuse a release tag. It never overwrites an asset,
never uses `latest`, and the repository retention policy is `never-delete`.

Every production asset entry must use a fixed URL below that repository's
`releases/download/<tag>/` path, include an archive SHA-256, and retain
provenance: upstream repository/release/file/URL, mirror timestamp, target RID,
and FFmpeg build identifier. Historical tags and assets are release inputs for
old DownKyi commits and must not be pruned.

## Updater flow

`.github/workflows/update-ffmpeg-assets.yml` runs weekly or on manual dispatch:

1. Discover the newest complete, non-`latest` GitHub release using the
   publisher API and its per-asset SHA-256 digest.
2. Download the selected archives, verify size and SHA-256, extract them, and
   require non-empty `ffmpeg` and `ffprobe` files.
3. On native runners run `ffmpeg -version`, `ffprobe -version`, and where
   applicable verify the required `h264_nvenc` encoder is compiled in.
4. Only after every matrix validation job succeeds, create a new mirror release
   and upload the exact verified archives.
5. Record the resulting fixed mirror URLs and provenance, validate them with
   the preflight, then create a manifest PR. The workflow cannot push `main`.

A validation, download, checksum, extraction, capability, upload, or preflight
failure stops before manifest mutation. A failed upload may leave an unreferenced
incomplete release for an operator to inspect, but it cannot update the
production manifest or replace a historical asset.

## Bootstrap and recovery

Before enabling normal scheduled updates, create the dedicated repository and
run **Update mirrored FFmpeg assets** with `bootstrap=true`. This imports all
six current pinned sources, including the distinct win-x86/macOS sources, into
one immutable mirror release and opens a PR. Do not manually edit the manifest
to another BtbN daily URL.

If the bootstrap fails:

1. Keep the current manifest unchanged.
2. Inspect the failed RID and source URL in the workflow output.
3. Correct an upstream policy issue or create a fresh bootstrap candidate; do
   not overwrite the partial mirror release/tag.
4. Re-run the dispatch with a new fixed candidate. Review the generated PR and
   require the release/package workflow before merging.

To force a normal BtbN refresh, run the same workflow with `bootstrap=false`.
If the selected fixed release is already represented in the manifest, it exits
successfully without creating a PR.

## Required permissions and secrets

The workflow uses two narrowly scoped credentials:

- `RUNTIME_ASSETS_TOKEN`: fine-grained token or GitHub App installation token
  with **Contents: read/write** only on
  `crazysmile-PhD/downkyi-runtime-assets`. It creates releases and uploads
  assets, but has no DownKyi source-repository permission.
- `DOWNKYI_AUTOMATION_TOKEN`: fine-grained token or GitHub App installation
  token with **Contents: read/write** and **Pull requests: read/write** only on
  `crazysmile-PhD/downkyicore`. It creates the manifest PR. A separate token is
  required because a PR opened by `GITHUB_TOKEN` does not reliably trigger the
  repository's package CI.

No token is needed by normal builds or package downloaders.

## Local checks

Run these from the repository root with Python 3.12 or later. Python is also a
package-script prerequisite: the Windows and Unix FFmpeg downloaders invoke the
same fail-closed checksum verifier.

```powershell
python -m unittest script/tests/test_ffmpeg_assets.py -v
python script/ffmpeg-assets.py validate-manifest --manifest script/assets/external-assets.json
python script/ffmpeg-assets.py preflight --manifest script/assets/external-assets.json --timeout 30
```

`preflight` is intentionally only an availability and schema gate. The package
downloaders still rehash the downloaded archive before extraction, so a later
content replacement fails closed.
