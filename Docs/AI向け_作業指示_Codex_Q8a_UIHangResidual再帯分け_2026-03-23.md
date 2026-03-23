# AI向け 作業指示 Codex Q8a UIHangResidual再帯分け 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `UI hang` 関連の residual dirty を、回帰帯と続行可能帯へ再分割する。
- 実装修正は行わず、まず commit 単位へ切れる帯を確定する。

## 2. 主対象

- `App.xaml.cs`
- `Views/Main/MainWindow.DispatcherTimerSafety.cs`
- `Views/Main/MainWindow.Startup.cs`
- `Views/Main/MainWindow.Player.cs`
- `Views/Main/MainWindow.UiHangNotification.cs`
- 関連 test
  - `Tests/IndigoMovieManager_fork.Tests/AppDispatcherTimerExceptionPolicyTests.cs`
  - `Tests/IndigoMovieManager_fork.Tests/ManualPlayerResizeHookPolicyTests.cs`
  - `Tests/IndigoMovieManager_fork.Tests/StartupUiHangActivitySourceTests.cs`
  - `Tests/IndigoMovieManager_fork.Tests/UiHangNotificationPolicyTests.cs`

## 3. やること

1. main worktree の dirty を読み、変更意図を帯ごとに分類する
2. 少なくとも
   - dispatcher timer 縮退
   - startup activity
   - manual player resize
   - raw timer stop 回帰
   の 4 帯へ分けられるか確認する
3. 各帯について
   - commit 可能
   - 回帰で凍結
   - 追加調査が必要
   を判定する

## 4. 返却物

- 帯一覧
- 各帯の対象ファイル
- commit 可能 / 凍結 の判定
- 次に投げるべき最小レーン
