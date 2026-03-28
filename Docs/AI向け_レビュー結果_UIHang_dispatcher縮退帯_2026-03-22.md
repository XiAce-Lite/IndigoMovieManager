# AI向け レビュー結果 UIHang dispatcher縮退帯 2026-03-22

最終更新日: 2026-03-22

変更概要:
- `UI hang` 系の follow-up として、`dispatcher timer` fault 縮退帯の受け入れ結果を記録
- fix8 後レビュー `findings なし` と、本線取り込み commit `1106d2b` を記録
- 取り込み後に同一ファイル群へ残っていた dirty 差分が回帰であることを記録

## 1. 対象

- `App.xaml.cs`
- `Views/Main/MainWindow.DispatcherTimerSafety.cs`
- `Views/Main/MainWindow.UiHangNotification.cs`
- `Views/Main/MainWindow.Player.cs`
- `Views/Main/MainWindow.Startup.cs`
- `BottomTabs/DebugTab/MainWindow.BottomTab.Debug.cs`
- `BottomTabs/Bookmark/MainWindow.BottomTab.Bookmark.cs`
- `Tests/IndigoMovieManager_fork.Tests/AppDispatcherTimerExceptionPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/UiHangNotificationPolicyTests.cs`

## 2. 受け入れ結果

- 状態
  - 完了、受け入れ、本線取り込み済み
- clean commit
  - `a0724d9`
- 本線 commit
  - `1106d2b36a71fc8ed2f7090e28bb2b90634af9d3`
- commit message
  - `UI hang の dispatcher timer 縮退を安全化する`

## 3. fix8 で閉じた点

- `dispatcher timer` の fault 記録を `App` 側 global flag と `MainWindow` 側 local flag の両方で保持する形へ戻した
- `TryStartDispatcherTimer(...)` は known fault を記録して縮退へ倒し、未知 `Win32Exception` は握り広げない形へ固定した
- `StopDispatcherTimerSafely(...)` は fault cleanup 中でも無条件 suppress せず、cleanup 経路を示す stack marker がある時だけ狭く許可する形へ固定した
- fault 記録後に `StartUiHangNotificationSupportCore(...)` が `false` へ倒れる契約をテストで固定した

## 4. レビュー結果

- fix8 後のレビュー専任役判定
  - `findings なし`
- 判定理由
  - `App.xaml.cs` の suppress 条件は `Win32Exception`、`NativeErrorCode == 8`、既知 render timer 経路に限定された
  - `MainWindow.UiHangNotification.cs` の再起動抑止は `HasDispatcherTimerInfrastructureFault` と整合した
  - 今回の差分帯に unrelated change は認められなかった

## 5. 検証状況

- `git diff --cached --check`
  - 通過
- 対象テスト
  - 差分外の既存 compile error により完走していない
- 主な blocker
  - `Watcher/MainWindow.WatcherRegistration.cs`
  - `Watcher/MainWindow.WatcherRenameBridge.cs`
  - `Tests/IndigoMovieManager_fork.Tests/ThumbnailLayoutProfileTests.cs`
  - `Tests/IndigoMovieManager_fork.Tests/RescueWorkerApplicationTests.cs`

## 6. 本線取り込み後の residual dirty 判定

- 判定
  - 回帰なので保留 / 破棄推奨
- 対象
  - `1106d2b` 取り込み後、同一ファイル群に残っていた dirty 差分
- 主 finding
  - `dispatcher timer fault` 縮退契約を壊していた
  - `App` 側 global fault 記録と `MainWindow` 側 start 抑止の連携を切っていた
  - suppress 判定を accepted より広げていた
  - raw `DispatcherTimer.Stop()` を再侵入させていた

## 7. 調整役判断

- `1106d2b` の accepted 帯は確定として維持する
- 取り込み後 residual dirty 9 ファイルは次帯へ進めない
- `TrackUiHangActivity(UiHangActivityKind.Startup)` と manual player resize hook だけを別帯へ再抽出する
