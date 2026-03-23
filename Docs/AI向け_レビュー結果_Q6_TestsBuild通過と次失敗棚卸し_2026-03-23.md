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

## 7. Q6a 完了結果

- `Q6a` は `AutogenRegressionTests.cs` 1 ファイル修正で受け入れた
- レビュー専任役は `findings なし`
- clean worktree の対象テストは `20` 件合格
- 本線 commit
  - `8064e7ff43149321991f97f62dfcb06719e5b411`
  - `超巨大動画router回帰テストを現仕様へ寄せる`
- 次は `Q6b rescue window reservation reflection drift` へ進む

## 8. Q6b 完了結果

- `Q6b` は clean worktree で受け入れ済み
- `MissingThumbnailRescuePolicyTests` の旧 reflection 依存を外し、現行 `TryReserveMissingThumbnailRescueWindow(...)` 契約へ寄せた
- 対象テスト `62` 件成功
- レビュー専任役
  - `findings なし`
- ただし main 側 `Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs` に別テーマ dirty が混在しているため、本線取り込みは保留

## 9. Q6c 配布判断

- `Q6c` は source 調査を完了した
- 調整役判断では、現仕様は「manual metadata 欠落を precheck 即失敗にせず、`ThumbnailJobContextBuilder` fallback で作り直し context へ進める」が正しい
- 根拠
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateWorkflowCoordinator.cs`
    - precheck 即返しは `HasImmediateResult` の時だけ
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailPrecheckCoordinator.cs`
    - manual metadata 欠落を即失敗にする分岐は存在しない
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailJobContextBuilder.cs`
    - manual metadata 欠落時は `ThumbnailAutoThumbInfoBuilder.Build(...)` へ fallback する
  - `Tests/IndigoMovieManager_fork.Tests/ThumbnailJobContextBuilderTests.cs`
    - `Build_manual生成でWB互換メタが無ければ現在位置を使って作り直し用contextを返す`
      が既に現仕様を固定している
- したがって `Q6c` は source 修正ではなく、workflow test の期待値と test 名の再整合を主線として配布する

## 10. Q6c 完了結果

- `Q6c` は clean worktree で受け入れ済み
- `ThumbnailCreateWorkflowCoordinatorTests` の manual metadata ケースを現行 fallback 契約へ寄せた
- 対象周辺テスト
  - `ThumbnailCreateWorkflowCoordinatorTests`
  - `ThumbnailJobContextBuilderTests`
  - `ThumbnailPrecheckCoordinatorTests`
  が通過
- レビュー専任役
  - `findings なし`
- 本線 commit
  - `bb70b966b9f4153cc21dda80dddef676c4ef4044`
  - `manual metadata workflowテストを現仕様へ寄せる`

## 11. Q6d 完了結果

- `Q6d` は clean worktree で受け入れ済み
- `WhiteBrowserThumbInfoSerializer` の `CaptureSeconds` が旧メタ列を読んでいた不整合を修正した
- 対象周辺テスト
  - `ThumbnailJpegMetadataWriterTests`
  - `ThumbInfoCompatibilityTests`
  が通過
- レビュー専任役
  - `findings なし`
- 本線 commit
  - `1304dfe9c617e7e073f0009bd32ab4d2ebe69fc4`
  - `jpegメタ修復で最新追記メタを優先する`

## 12. Q6e 配布判断

- `Q6e` は source 調査を完了した
- 調整役判断では、`NullReferenceException` の原因は `WatchScanCoordinatorPolicyTests` の harness 初期化不足であり、runtime source を緩める必要はない
- 根拠
  - `Watcher/MainWindow.WatchScanCoordinator.cs`
    - `FlushPendingNewMoviesAsync(...)` は DB insert 後に `TryAdjustRegisteredMovieCount(context.SnapshotDbFullPath, insertedCount)` を呼ぶ
  - `Views/Main/MainWindow.xaml.cs`
    - `TryAdjustRegisteredMovieCount(...)` は `MainVM.DbInfo` と `Dispatcher.InvokeAsync(...)` 前提で動く
  - `Tests/IndigoMovieManager_fork.Tests/WatchScanCoordinatorPolicyTests.cs`
    - `CreateMainWindowForCoordinatorTests()` は未初期化 `MainWindow` を返していた
    - `CreatePendingFlushContext()` の `SnapshotDbFullPath` 既定値も空だった
- したがって `Q6e` は source 修正ではなく、test harness 補強を主線として配布する

## 13. Q6e 完了結果

- `Q6e` は clean worktree で受け入れ済み
- `WatchScanCoordinatorPolicyTests` の harness に最小 `MainVM` / `DbInfo` / `Dispatcher` 契約を補強した
- 対象テスト
  - failing test 単体
  - `WatchScanCoordinatorPolicyTests` 全体 `13` 件
  が通過
- レビュー専任役
  - `findings なし`
- clean accepted commit
  - `f78936ae072b95bfc7e4b2139d397c7ca5ccdba9`
  - `watch coordinator harness nullテストを補強する`
- 本線 commit
  - `65a8d5bf32787e4db2e2820f1dea63dbc5995cdf`
  - `watch coordinator harness nullテストを補強する`
- 次は `Q6b` の main 取り込み帯分けか、残 failing test の次レーンへ進む
