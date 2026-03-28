# Implementation Plan_救済worker_CI自動生成_2026-03-17

## 1. 目的

- rescue worker artifact をローカルだけでなく CI でも同じ手順で生成できるようにする
- tag 時に app 配布物とは別に worker artifact も release asset として残せるようにする
- 将来の別 repo 化で、そのまま流用できる publish/package 手順を先に固める

## 2. 今回の反映

- `scripts/create_rescue_worker_artifact_package.ps1`
  - `Publish-RescueWorkerArtifact.ps1` を呼び、`artifacts/rescue-worker/publish/*` を生成
  - publish 出力を package folder へ複製し、`README-artifact.txt` と `SHA256SUMS.txt` を追加
  - `IndigoMovieManager.Thumbnail.RescueWorker-<label>-win-x64-compat-<CompatibilityVersion>.zip` を生成
- `.github/workflows/rescue-worker-artifact.yml`
  - `workflow_dispatch` と `v*` tag push で実行
  - worker artifact zip を Actions Artifact へ保存
  - tag 時は GitHub Release asset としても添付

## 3. 今の意味

- app 本体 release workflow と独立に、worker artifact だけを追えるようになった
- ローカル確認と CI が同じ script を呼ぶので、差分事故が減る
- release asset に worker zip を残せるため、repo 分離前でも artifact 起点の検証がしやすい

## 4. ローカル手順

PowerShell 7 で次を実行する。

```powershell
./scripts/create_rescue_worker_artifact_package.ps1 `
  -Configuration Release `
  -Runtime win-x64 `
  -VersionLabel v0.0.0-local
```

生成物:

- `artifacts/rescue-worker/publish/Release-win-x64/*`
- `artifacts/rescue-worker/package/IndigoMovieManager.Thumbnail.RescueWorker-v0.0.0-local-win-x64/*`
- `artifacts/rescue-worker/IndigoMovieManager.Thumbnail.RescueWorker-v0.0.0-local-win-x64.zip`

## 5. 残件

- app release package 側から worker artifact version を参照する連携はまだ無い
- CI で artifact compatibilityVersion を検査する dedicated step はまだ無い
- 別 repo 化の段では、この workflow を新 repo 側へ移すかを決める
