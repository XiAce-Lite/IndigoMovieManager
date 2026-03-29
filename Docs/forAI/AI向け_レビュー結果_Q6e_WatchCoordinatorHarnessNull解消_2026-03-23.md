# AI向け レビュー結果 Q6e WatchCoordinatorHarnessNull解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `watch coordinator harness null` は runtime source ではなく test harness の初期化不足が原因だった
- clean worktree では `WatchScanCoordinatorPolicyTests.cs` 1 ファイルだけを補強し、レビュー専任役 `findings なし` を確認した
- main 側の同一ファイルは dirty だったため、accepted blob だけを index に載せて本線 commit `65a8d5bf32787e4db2e2820f1dea63dbc5995cdf` で取り込んだ

## 1. 対象

- `Tests/IndigoMovieManager_fork.Tests/WatchScanCoordinatorPolicyTests.cs`

## 2. 実行場所

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q6e-watch-coordinator-harness`

## 3. root cause

- `CreateMainWindowForCoordinatorTests()` が `RuntimeHelpers.GetUninitializedObject(typeof(MainWindow))` をそのまま返していた
- 一方で `FlushPendingNewMoviesAsync(...)` は DB insert 後に `TryAdjustRegisteredMovieCount(...)` を呼ぶ
- その結果、test harness 側に `MainVM` / `DbInfo` / `Dispatcher` の最低限契約が無く、`NullReferenceException` で落ちていた

## 4. 着地

- `CoordinatorTestDbFullPath` を定数化した
- `CreateMainWindowForCoordinatorTests()` で最小限の `MainVM` / `DbInfo` を差し込んだ
- `_registeredMovieCountInitialized` を private field 経由で `true` に固定した
- 未初期化 `MainWindow` でも `Dispatcher.InvokeAsync(...)` が通るよう、基底型まで遡る helper で `_dispatcher` を補完した
- `CreatePendingFlushContext()` に `SnapshotDbFullPath = CoordinatorTestDbFullPath` を追加した
- runtime source は変更していない

## 5. 検証

- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~WatchScanCoordinatorPolicyTests.FlushPendingNewMoviesAsync_mid_pass_staleならappendもdeferred戻しもしない"`
  - 成功
  - 対象 failing test は合格
- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~WatchScanCoordinatorPolicyTests"`
  - 成功
  - `13` 件合格
- `git diff --check`
  - 成功
- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64`
  - 既知の別系統 failing test で停止
  - `Q6e` の受け入れ可否には影響しない

## 6. レビュー結果

- レビュー専任役
  - `findings なし`
- 調整役判断
  - 受け入れ

## 7. 残留リスク

- `_dispatcher` 反射補完は WPF 基底 private field 名に依存する
- ただし現行 .NET 8 / この repository の test 実行では再現なく通過している

## 8. 本線取り込み結果

- clean accepted commit
  - `f78936ae072b95bfc7e4b2139d397c7ca5ccdba9`
  - `watch coordinator harness nullテストを補強する`
- 本線 commit
  - `65a8d5bf32787e4db2e2820f1dea63dbc5995cdf`
  - `watch coordinator harness nullテストを補強する`
- 取り込み対象
  - `Tests/IndigoMovieManager_fork.Tests/WatchScanCoordinatorPolicyTests.cs`
