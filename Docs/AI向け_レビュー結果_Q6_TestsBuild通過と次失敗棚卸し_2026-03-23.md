# AI向け レビュー結果 Q6 TestsBuild通過と次失敗棚卸し 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `Q5a` / `Q5b` 本線取り込み後、clean worktree で tests project build が通ることを確認した
- 次 blocker は compile ではなく failing test 群へ移った
- failing test は 5 系統に分離できると判断した

## 1. 実行場所

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q6-next-blocker-synth`

## 2. 確認結果

- `MSBuild.exe Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /nologo /verbosity:minimal`
  - 成功
- `dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build`
  - 実行
  - `775` 件中
    - 合格 `765`
    - 失敗 `6`
    - スキップ `4`

## 3. 失敗の内訳

### 3-1. router 期待値 drift

- `AutogenRegressionTests.Router_100枚かつ400Gb超ならFfmpegOnePassを先頭にする`
- 現在値
  - expected: `ffmpeg1pass`
  - actual: `autogen`

### 3-2. rescue window reservation reflection drift

- `MissingThumbnailRescuePolicyTests.ReleaseMissingThumbnailRescueWindowReservation_defer_drop時は同じ予約だけ巻き戻せる`
- `MissingThumbnailRescuePolicyTests.ReleaseMissingThumbnailRescueWindowReservation_新しい予約は巻き戻さない`
- 現在値
  - `ReleaseMissingThumbnailRescueWindowReservation` が見つからず `method == null`

### 3-3. manual metadata precheck drift

- `ThumbnailCreateWorkflowCoordinatorTests.ExecuteAsync_manualでWB互換メタが無ければprecheck失敗を返す`
- 現在値
  - expected: `precheck`
  - actual: `autogen`
  - `autogen.CreateCallCount` も `1`

### 3-4. jpeg metadata repair drift

- `ThumbnailJpegMetadataWriterTests.TryEnsureThumbInfoMetadata_既存メタが不一致でも再追記で修復後はサイズ増加が止まる`
- 現在値
  - metadata repair が `False`
  - stable も `False`
  - capture seconds expected `1`, actual `9`

### 3-5. watch coordinator test harness null

- `WatchScanCoordinatorPolicyTests.FlushPendingNewMoviesAsync_mid_pass_staleならappendもdeferred戻しもしない`
- 現在値
  - `MainWindow.TryAdjustRegisteredMovieCount(...)` で `NullReferenceException`
  - test harness 側の `MainVM` / registered count 周りの初期化不足が疑わしい

## 4. 調整役の判断

- compile blocker は解消済み
- 次は failing test を 1 帯に潰さず、最低でも次の 5 レーンへ分ける
  - `Q6a` router expectation drift
  - `Q6b` rescue reservation reflection drift
  - `Q6c` manual metadata precheck drift
  - `Q6d` jpeg metadata repair drift
  - `Q6e` watch coordinator harness null

## 5. 次アクション

1. `Q6a` を最初に切り、router 正本と test 期待値のどちらが正しいかを棚卸しする
2. `Q6b` は method 名 drift なのか、責務移動なのかを source 調査から入る
3. `Q6c` / `Q6d` は thumbnail engine 側の現在仕様確認を先に入れる
4. `Q6e` は test harness 補強レーンとして独立させる

## 6. Q6a 配布判断

- `Q6a` は source 調査を完了した
- 調整役判断では、現仕様は `400GB` でも `32GB` 超のため `autogen` が正しい
- 根拠
  - `Thumbnail/ThumbnailEnvConfig.cs`
    - `DefaultUltraLargeFileThresholdGb = 32.0d`
  - `Thumbnail/Engines/ThumbnailEngineRouter.cs`
    - `ResolveForThumbnail(...)` は ultra-large 判定を `100 panel + large file` より先に評価する
  - `Thumbnail/Implementation Plan_通常キュー超巨大動画timeout実効化_2026-03-18.md`
    - 超巨大動画も `autogen` を維持すると明記
- したがって `Q6a` は source 修正ではなく、test 期待値と test 名の再整合を主線として配布する
