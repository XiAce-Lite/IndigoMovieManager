# AI向け レビュー結果 Watcher残差分帯分け 2026-03-22

最終更新日: 2026-03-22

変更概要:
- `Watcher` 残差分 8 ファイルをレビュー専任役へ回し、単一帯で扱えるかを判定した
- `RenameBridge` 安全契約後退、`Created` 経路の別設計依存、`drop policy` 正規化後退が混在していることを確認した
- 次に切る最小レーンを `Watcher RenameBridge 安全契約復元` に固定した

## 1. 判定

- 単一コミット帯: 不可
- 推奨: 再分割
- 次レーン候補名: `Watcher RenameBridge 安全契約復元`

## 2. 主 finding

- `High`
  - `Watcher/MainWindow.WatcherRenameBridge.cs` が snapshot / guard / rollback 前提から外れ、`MainVM` 現在値へ直接書き戻す形へ後退している
- `High`
  - 共有資産 rename の安全契約が崩れ、hash jpg / ERROR marker / bookmark 部分一致 rename の誤爆が起こり得る
- `High`
  - `WatcherRegistrationDirectPipelineTests.cs` 側の差分は、現 worktree に存在しない direct pipeline 実装へ依存しており、この帯だけでは閉じない
- `Medium`
  - `WatchScanCoordinator.cs` 側で deferred path の case-insensitive 重複潰しが落ちている
- `Medium`
  - `WatchFolderDropRegistrationPolicy.cs` 側で末尾セパレータ正規化と `CanAccept` 早期 return 契約が落ちている

## 3. 次レーンに含めるファイル

- `Watcher/MainWindow.WatcherRenameBridge.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRenameBridgePolicyTests.cs`

## 4. 今回含めないもの

- `Watcher/MainWindow.WatchScanCoordinator.cs`
- `Watcher/WatchFolderDropRegistrationPolicy.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatchScanCoordinatorPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatchFolderDropRegistrationPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRegistrationDirectPipelineTests.cs`
- `Watcher/Docs/Flowchart_メインDB登録非同期化現状_2026-03-05.md`

## 5. 補足

- 対象テスト実行は `Views/Main/MainWindow.Startup.cs:71` の別件コンパイルエラーで未完走
- 次は `RenameBridge` の安全契約だけを復元し、その後に `EventQueue` や `drop policy` を別レーンで切る
