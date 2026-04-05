# Implementation Plan rescue-worker.lock.json と同梱worker整合チェック 2026-04-03

最終更新日: 2026-04-04

変更概要:
- `rescue-worker.lock.json` の実 schema に合わせて内容を更新
- `verify_app_package_worker_lock.ps1` の実処理に合わせて整合チェック手順を更新
- live 成功済みの Public / Private release 運用を踏まえ、`sourceType` と `assetFileName` の意味を具体化

## 1. 目的

app package 配下の `rescue-worker.lock.json` で、同梱 worker が main repo が pin した正しい実行物かを起動前に fail-fast する。

この資料は次を正本にする。

1. lock file の実 schema
2. app package 生成時の事前検証
3. launcher 起動時の再検証
4. `sourceType / version / assetFileName / compatibilityVersion / sha256` の意味

## 2. いまの結論

現在の運用は次である。

1. `scripts/create_github_release_package.ps1` が app package 直下へ
   - `rescue-worker.lock.json`
   - `rescue-worker-expected.json`
   - `rescue-worker-lock-summary.txt`
   を生成する
2. 同じ script が `scripts/verify_app_package_worker_lock.ps1` を呼び、ZIP 化前に整合チェックを行う
3. app 起動後は `ThumbnailRescueWorkerArtifactLockFile` と `ThumbnailRescueWorkerLaunchSettingsFactory` が lock を読み、worker 実行前に再検証する
4. lock の正本は `workerArtifact` セクションであり、main repo はこの内容で worker を pin する

## 3. 実 schema

現在の `rescue-worker.lock.json` は次の形である。

```json
{
  "schemaVersion": 1,
  "workerArtifact": {
    "artifactType": "IndigoMovieManager.Thumbnail.RescueWorker",
    "sourceType": "github-release-asset",
    "version": "v1.0.3.5",
    "assetFileName": "IndigoMovieManager.Thumbnail.RescueWorker-v1.0.3.5-win-x64-compat-2026-03-17.1.zip",
    "sourceArtifactName": "",
    "compatibilityVersion": "2026-03-17.1",
    "workerExecutableSha256": "..."
  }
}
```

### 3.1 必須項目

- `schemaVersion`
  - lock file schema の版
  - 現在は `1`
- `workerArtifact.artifactType`
  - 固定値
  - `IndigoMovieManager.Thumbnail.RescueWorker`
- `workerArtifact.sourceType`
  - worker 取得元の種別
- `workerArtifact.version`
  - worker 側の version / tag / run 由来ラベル
- `workerArtifact.assetFileName`
  - Public 側が pin した worker package 名
- `workerArtifact.compatibilityVersion`
  - host と worker の契約一致判定
- `workerArtifact.workerExecutableSha256`
  - 同梱 `IndigoMovieManager.Thumbnail.RescueWorker.exe` の hash

### 3.2 任意項目

- `workerArtifact.sourceArtifactName`
  - GitHub Actions artifact 名を持ちたい時だけ入る
  - `github-release-asset` では空でもよい
  - `github-actions-artifact` では `rescue-worker-publish` のような値を持つ

## 4. sourceType の実値

現在の実装で想定している値は次である。

- `github-release-asset`
  - Private repo の GitHub Release asset を同期した時
- `github-actions-artifact`
  - preview 用に Private repo の Actions artifact を同期した時
- `prepared-publish-dir`
  - metadata が無い prepared publish dir をそのまま使った時
- `bundled-app-package`
  - Public repo が local worker source build から package を作った時

補足:
- Public repo の既定運用は `github-release-asset` または `github-actions-artifact`
- `bundled-app-package` は local 開発用の明示 opt-in 例外である

## 5. 関連ファイルの役割

- `rescue-worker.lock.json`
  - app package の pin 正本
  - launcher が読む
- `rescue-worker-expected.json`
  - package 側の期待値
  - `bundledRescueWorkerRelativePath`
  - `expectedRescueWorkerCompatibilityVersion`
  を持つ
- `rescue-worker-artifact.json`
  - worker publish dir / bundled worker 配下の marker
  - `artifactType`
  - `compatibilityVersion`
  - `supportedEntryModes`
  を持つ
- `rescue-worker-sync-source.json`
  - Private artifact 同期元の provenance
  - `sourceType / version / assetFileName / sourceArtifactName / compatibilityVersion`
  を持つ

## 6. 整合チェックの流れ

### 6.1 package 生成時

`scripts/create_github_release_package.ps1` は次を行う。

1. worker publish dir または prepared publish dir を決める
2. `rescue-worker-artifact.json` を読む
3. `compatibilityVersion` が app 側期待値と一致するか確認する
4. worker を `rescue-worker\` 配下へ同梱する
5. `rescue-worker.lock.json` を生成する
6. `scripts/verify_app_package_worker_lock.ps1` を呼ぶ
7. 通った時だけ ZIP 化する

### 6.2 verify script

`scripts/verify_app_package_worker_lock.ps1` は次を確認する。

1. `rescue-worker.lock.json` を読む
2. `rescue-worker-expected.json` を読む
3. `schemaVersion >= 1` を確認する
4. `workerArtifact` の必須項目が揃っているか確認する
5. `rescue-worker\IndigoMovieManager.Thumbnail.RescueWorker.exe` の存在を確認する
6. 同じ directory の `rescue-worker-artifact.json` を読む
7. `artifactType` が固定値と一致するか確認する
8. `compatibilityVersion` が
   - lock
   - expected
   - marker
   で一致するか確認する
9. required files と native sqlite の存在を確認する
10. exe の `sha256` を計算し、lock と一致するか確認する

### 6.3 launcher 起動時

`ThumbnailRescueWorkerArtifactLockFile` と `ThumbnailRescueWorkerLaunchSettingsFactory` は次を行う。

1. host base 直下の `rescue-worker.lock.json` を読む
2. worker exe 近傍の marker を読む
3. `artifactType / compatibilityVersion / sha256` を照合する
4. 不一致なら worker を採用しない
5. 診断理由を `workerExecutablePathDiagnostic` と log へ残す

## 7. fail-fast 条件

現在の fail-fast 条件は次である。

- `rescue-worker.lock.json` が壊れている
- `schemaVersion` が無い、または不正
- `workerArtifact` が無い
- `artifactType` が不正
- `compatibilityVersion` が
  - lock と expected
  - lock と marker
  のどちらかで一致しない
- `workerExecutableSha256` が実 exe と一致しない
- `rescue-worker-artifact.json` が無い
- bundled worker が無い
- required files が不足
- native sqlite が不足

## 8. 実務判断

いまの実務判断は次である。

1. lock file は app package の pin 正本にする
2. launcher は lock file がある時だけ厳しく fail-fast する
3. `sourceType` は provenance の説明値であり、採用判定の本体は
   - `artifactType`
   - `compatibilityVersion`
   - `sha256`
   で行う
4. `assetFileName` は GitHub Release 本文や summary にも出す正本名にする
5. `sourceArtifactName` は preview 調査で必要な時だけ持てばよい
6. Public repo の既定は private artifact / release asset を正本にし、local worker source build は例外運用に留める

## 9. live で確認できたこと

2026-04-04 時点で次を確認済みである。

1. preview run で Private publish artifact pin が通る
2. preview run で Private release asset pin が通る
3. Public release `v1.0.3.5` で `github-release-asset` 正本ルートが通る
4. package 生成時に `worker lock verification ok` が出る
5. Release 本文 summary に
   - `Source`
   - `Version`
   - `Artifact`
   - `CompatibilityVersion`
   - `WorkerExe SHA256`
   が出る

## 10. 次に残るもの

この資料の次段でやるべきものは次である。

1. lock file schema を必要なら別資料へ独立させる
2. rollback 時にどの `version / assetFileName / sha256` を戻すかの運用を書く
3. `sourceType` ごとの監視 / 障害切り分けを人向け手順へ落とす

