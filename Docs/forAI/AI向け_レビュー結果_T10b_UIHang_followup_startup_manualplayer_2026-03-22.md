# AI向け レビュー結果 T10b UIHang follow-up startup manualplayer 2026-03-22

最終更新日: 2026-03-22

変更概要:
- `UI hang` residual dirty から、安全な 2 点だけを再抽出した別帯 `T10b` の受け入れ結果を記録
- startup activity の source-aware 化と manual player resize hook の isolated 受け入れを記録
- 対象テストが差分外 `Watcher` compile error で完走しないことを blocker として記録

## 1. 対象

- `Startup/StartupLoadCoordinator.cs`
- `Views/Main/MainWindow.Startup.cs`
- `Views/Main/MainWindow.Player.cs`
- `Tests/IndigoMovieManager_fork.Tests/StartupUiHangActivitySourceTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/ManualPlayerResizeHookPolicyTests.cs`

## 2. 着地

- startup 側
  - `StartupFeedRequest` に `UiHangActivityKind ActivityKind` を追加
  - `BeginStartupDbOpen()` は `UiHangActivityKind.Startup` を request に積む
  - `RunStartupDbOpenAsync(...)` は `TrackUiHangActivity(request.ActivityKind)` を使う
- player 側
  - manual player 表示経路にだけ resize hook を追加
  - 二重登録防止 guard を追加
  - visible でない時は viewport 更新しない policy を追加
  - overlay close 時に hook を解除する
- テスト
  - startup source test を repo root 探索 helper へ修正
  - startup request 積み込みと request.ActivityKind 利用を source 固定
  - manual player resize hook の policy テストを追加

## 3. レビュー結果

- 初回レビュー
  - `TrackUiHangActivity(UiHangActivityKind.Startup)` が source-aware でない
  - source test の相対パスが壊れている
- fix1 後レビュー
  - `findings なし`
  - 受け入れ可

## 4. 検証

- `git diff --check`
  - 通過
- 対象テスト
  - 差分外 blocker により完走せず
- blocker
  - `Watcher/MainWindow.WatcherRegistration.cs`
  - `Watcher/MainWindow.WatcherRenameBridge.cs`

## 5. 調整役判断

- `T10b` は受け入れ
- `dispatcher timer` fault 縮退帯とは別帯として扱う
- 本線取り込み時は `Startup` / `Player` / 対応テストだけに閉じる
