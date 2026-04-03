# TASK-009 worker lock file schemaとlauncher読取骨格 2026-04-03

最終更新日: 2026-04-03

## 1. 目的

main repo が external worker を消費する時に、
「どの worker artifact を使うか」を 1 つの lock file で固定できるようにする。

今回の狙いは次の 2 つである。

- schema を先に固定して、release helper / launcher / live 確認で同じ JSON を見られるようにする
- launcher 側に読取骨格を入れ、lock file がある時だけ fail-fast を強める

## 2. lock file の置き場所

- ファイル名: `rescue-worker.lock.json`
- 置き場所: app host base directory 直下
- app package では `IndigoMovieManager.exe` と同じ階層に置く

補足:
- `rescue-worker-expected.json` は package 内期待値の manifest
- `rescue-worker.lock.json` は main repo が pin した external worker の消費 manifest

役割が違うので、両方を持ってよい。

## 3. 最小 schema

```json
{
  "schemaVersion": 1,
  "workerArtifact": {
    "artifactType": "IndigoMovieManager.Thumbnail.RescueWorker",
    "sourceType": "github-release",
    "version": "v1.2.3",
    "assetFileName": "IndigoMovieManager.Thumbnail.RescueWorker-v1.2.3-win-x64-compat-2026-03-17.1.zip",
    "compatibilityVersion": "2026-03-17.1",
    "workerExecutableSha256": "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF"
  },
  "packages": {
    "contracts": "1.2.3-preview.1",
    "engine": "1.2.3-preview.1",
    "failureDb": "1.2.3-preview.1"
  }
}
```

## 4. 必須 fields

- `schemaVersion`
  - lock file 自体の schema version
- `workerArtifact.sourceType`
  - 取得元の種別
  - v1 は `github-release` 前提
- `workerArtifact.artifactType`
  - worker artifact 用 lock であることを示す種別
- `workerArtifact.version`
  - main repo が pin した worker version
- `workerArtifact.assetFileName`
  - 期待する worker zip 名
- `workerArtifact.compatibilityVersion`
  - launcher が見る互換 version
- `workerArtifact.workerExecutableSha256`
  - 展開済み `IndigoMovieManager.Thumbnail.RescueWorker.exe` の hash
- `packages`
  - 将来の package pin 用の予約領域
  - 今回の launcher 骨格ではまだ読まない

## 5. launcher 側の最小読み取り

2026-04-03 の段階で、main repo 側には次の骨格を入れた。

- `ThumbnailRescueWorkerArtifactLockFile`
  - `rescue-worker.lock.json` を読む
- `ThumbnailRescueWorkerLaunchSettingsFactory`
  - lock file がある時だけ候補 exe を追加検証する

検証内容は最小で次の 2 本である。

1. `compatibilityVersion` 一致
2. `workerExecutableSha256` 一致

## 6. fail-fast 条件

lock file がある時は、次で worker 候補を不採用にする。

- lock file 自体が壊れている
- `schemaVersion` が無い、または不正
- `workerArtifact` section が無い
- `compatibilityVersion` が一致しない
- `workerExecutableSha256` が一致しない
- artifact marker が無い

この時は silent fallback せず、launcher log に理由を残す。

例:

- `worker artifact lock invalid: schemaVersion is missing or invalid.`
- `worker artifact lock mismatch: compatibilityVersion expected='x' actual='y'.`
- `worker artifact lock mismatch: sha256 expected='...' actual='...'.`

## 7. 今回やらないこと

- lock file から package restore version まで実際に解決する
- release helper で lock file を自動生成する
- GitHub Release asset の存在確認まで行う

ここは次段に回す。

## 8. 次の実務手順

1. app packaging が `rescue-worker.lock.json` を package へ同梱できるようにする
2. app packaging が lock file 経由でしか worker を同梱しない形へ寄せる
3. live 確認で `lock file 読取 -> manifest 一致 -> launcher 起動` を smoke 化する

2026-04-03 時点で、`scripts/create_github_release_package.ps1` は
同梱 worker exe の `sha256` を計算し、
`rescue-worker.lock.json` を package 直下へ生成するところまで入った。

さらに `scripts/verify_app_package_worker_lock.ps1` を追加し、
package 生成時に `lock / expected / marker / bundled worker` の整合を smoke 確認するようにした。

また marker がある worker 候補には、lock の前に
`必須ファイル / native sqlite / compatibilityVersion` の完成度検証を通すようにしたため、
lock が一致していても不完全な bundled artifact は採用しない。

## 9. 結論

`TASK-009` では、worker lock file を「文書だけ」で終わらせず、
launcher が読める最小骨格まで main repo へ入れた。

これで次は、release helper と packaging を
`lock file 正本` 前提へ寄せる実装へ進める。
