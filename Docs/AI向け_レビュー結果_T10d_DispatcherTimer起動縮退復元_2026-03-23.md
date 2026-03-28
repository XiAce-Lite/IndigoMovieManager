# AI向け レビュー結果 T10d DispatcherTimer起動縮退復元 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `T10d` の実装返却、fix1、最終レビュー結果を記録
- `StartupUri` 前提の handler 登録順と `DispatcherTimer` start / cleanup fault 縮退契約の受け入れを記録
- 対象テストが差分外 `Watcher` compile error で完走しないことを検証ギャップとして記録
- clean commit と本線取り込み commit を追記

## 1. 対象

- `App.xaml.cs`
- `Views/Main/MainWindow.DispatcherTimerSafety.cs`
- `Tests/IndigoMovieManager_fork.Tests/AppDispatcherTimerExceptionPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/UiHangNotificationPolicyTests.cs`

## 2. 着地

- `App.xaml.cs`
  - `RegisterDispatcherUnhandledExceptionHandler();` は現行で `base.OnStartup(e);` より前にあり、runtime 変更は不要と確認
- `Views/Main/MainWindow.DispatcherTimerSafety.cs`
  - known な start fault を `HandleDispatcherTimerInfrastructureFault(...)` へ流す経路を維持
  - cleanup 中の stop 例外は `TryHandleDispatcherTimerFaultCleanupStopWin32ExceptionCore(...)` で継続する経路を維持
- テスト
  - `App.xaml` の `StartupUri="Views/Main/MainWindow.xaml"` と `App.xaml.cs` の handler 登録順を同時に固定
  - `TryStartDispatcherTimer(...)` と cleanup 側 catch が helper 経由で正しい配線になっていることを source ベースで固定

## 3. レビュー結果

- 初回レビュー
  - `start / cleanup` の新規テストが helper 直叩きで、actual wiring 固定が弱い
  - `StartupUri` 前提がテストで固定されていない
- fix1 後レビュー
  - `findings なし`
  - 受け入れ可

## 4. 検証

- `git diff --check`
  - 通過
- 対象テスト
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AppDispatcherTimerExceptionPolicyTests|FullyQualifiedName~UiHangNotificationPolicyTests"`
  - 差分外 blocker により完走せず
- blocker
  - `Watcher/MainWindow.WatcherRegistration.cs`
  - `QueueWatchEventAsync`
  - `WatchEventRequest`
  - `WatchEventKind`

## 5. 調整役判断

- `T10d` は受け入れ
- 実行検証ギャップは `Watcher` 側既存ビルド不整合として別管理する
- clean commit
  - `d194e5e1100450c3d2d52843f241faeebe9166cb`
  - `DispatcherTimer起動縮退の契約を固定する`
- 本線 commit
  - `1c5af728e1fd9cf49c1dc86f08e41f6c4ac38aaf`
  - `DispatcherTimer起動縮退の契約を取り込む`
- accepted patch
  - `C:\Users\na6ce\source\repos\_imm_agents\out\t10d-dispatchertimer-accepted.patch`
- 補足
  - main repo への index-only 取り込み時は、元 patch が current index にそのまま当たらなかったため、accepted commit の同一 3 ファイルだけを current HEAD 基準の一時 patch に落として適用した
  - 取り込み後も同じ 3 ファイルには worktree dirty が残る
