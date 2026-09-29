# FFmpeg Asset Mirroring

## Ownership

`script/assets/external-assets.json` 是 production FFmpeg／ffprobe version、URL、SHA-256、RID 與 provenance 的唯一 owner。Package scripts 只能消費 manifest；不得 query upstream、resolve `latest`、自行選版或 fallback 到未驗證 executable。

Updater workflow 是唯一可查詢 upstream release 的 owner：

| RID | Discovery source | Required flavor |
| --- | --- | --- |
| `win-x64`、`linux-x64`、`linux-arm64` | `BtbN/FFmpeg-Builds` Releases API | fixed `autobuild-*`、static GPL、FFmpeg + ffprobe |
| `win-x86` | reviewed `yt-dlp/FFmpeg-Builds` source | fixed GPL archive with ffprobe |
| `osx-x64`、`osx-arm64` | reviewed martin-riedl pinned source | separate FFmpeg／ffprobe archives；禁止 HTML scraping |

Scheduled updater 只自動更新 BtbN RIDs；win-x86／macOS source 變更需要 operator-reviewed bootstrap。

## Immutable mirror invariant

- Mirror repository 固定為 `crazysmile-PhD/downkyi-runtime-assets`，retention 是 `never-delete`，且必須啟用 GitHub Immutable Releases。
- 每次 candidate 使用新的 `ffmpeg-<fixed-upstream-version>` tag。Filename 必須含 RID、fixed upstream tag 與 digest prefix；禁止 `latest`、覆寫或刪除歷史 asset。
- Workflow 先驗 publisher API 的 immutable tag／asset name／size／digest，再 download、rehash、extract 並確認 FFmpeg／ffprobe 可執行；native runner 另驗 version 和適用的 required encoder。
- 只有全部 RID 通過後才 publish mirror。Manifest mutation 前必須由 release API read back `immutable=true`，並再次比對每個 asset 的 name、size、digest。
- Manifest PR 只能修改 `script/assets/external-assets.json`，base 必須和提供 manifest 的 checkout branch 相同；workflow 不可直接 push `main`。
- 每個 manifest entry 保留 upstream repository／release／file／URL、mirror time、target RID 與 build identity。舊 tag／asset 是舊 DownKyi commit 的 release input，不得 prune。

Validation、download、checksum、extraction、capability、upload 或 preflight failure 都必須在 manifest mutation 前 fail closed。Publish 後 PR 失敗可以留下 unreferenced immutable release 供檢查，但 retry 只有在完整 read-back 相同時才能重用。

## Permissions

- `RUNTIME_ASSETS_TOKEN`：只對 runtime-assets repository 有 Contents read/write，用於建立 release／upload asset；不得有 DownKyi source permission。
- `DOWNKYI_AUTOMATION_TOKEN`：只對 downkyicore 有 Contents 與 Pull requests read/write，用於 manifest PR。不能用它寫 runtime-assets。
- Normal build／package downloader 不需要這兩個 token。

## Recovery

1. 保持 current manifest 不變。
2. 從 workflow artifact 定位失敗 RID、source、digest 或 capability gate；不手改 manifest 指向 daily URL。
3. 不覆寫 partial／published release。若 release 已 publish，只有所有 expected assets 完整 read-back 相同才可重用；否則建立新 candidate。
4. 修正 source-policy 問題後重新 dispatch，review manifest-only PR，並要求 normal release／package gates。

相同 fixed release 已存在於 manifest 時，normal updater 應 idempotently 成功且不建立 PR。

## Local checks

Repository root 需 Python 3.12+：

```powershell
python -m unittest script/tests/test_ffmpeg_assets.py -v
python script/ffmpeg-assets.py validate-manifest `
  --manifest script/assets/external-assets.json
python script/ffmpeg-assets.py preflight `
  --manifest script/assets/external-assets.json --timeout 30
```

`preflight` 只驗 availability 與 schema；package downloader 仍須在 extraction 前重新計算 archive SHA-256。
