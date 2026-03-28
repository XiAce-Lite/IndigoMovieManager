# AI向け 作業指示 Codex T10b UIHang follow-up startup manualplayer 2026-03-22

最終更新日: 2026-03-22

## 1. 目的

- `UI hang` follow-up の residual dirty から、回帰を含む塊を切り離し、安全な 2 点だけを別帯として取り出す
- 対象は次の 2 点に限定する
  - startup 冒頭の `TrackUiHangActivity(UiHangActivityKind.Startup)` 追加
  - manual player の resize hook 追加

## 2. 作業場所

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\uihang-followup-startup-manualplayer`

## 3. 変更対象

- 触ってよいファイル
  - `Views/Main/MainWindow.Startup.cs`
  - `Views/Main/MainWindow.Player.cs`
  - 必要なら対象テストを 1 から 2 ファイルまで追加してよい
- 触ってはいけないファイル
  - `App.xaml.cs`
  - `Views/Main/MainWindow.DispatcherTimerSafety.cs`
  - `Views/Main/MainWindow.UiHangNotification.cs`
  - `BottomTabs/DebugTab/MainWindow.BottomTab.Debug.cs`
  - `BottomTabs/Bookmark/MainWindow.BottomTab.Bookmark.cs`
  - `Tests/IndigoMovieManager_fork.Tests/AppDispatcherTimerExceptionPolicyTests.cs`
  - `Tests/IndigoMovieManager_fork.Tests/UiHangNotificationPolicyTests.cs`

## 4. 必須要件

### 4.1 startup activity

- `Views/Main/MainWindow.Startup.cs`
  - startup の処理本体入口に
    - `using IDisposable uiHangScope = TrackUiHangActivity(UiHangActivityKind.Startup);`
    を追加する
- 既存処理順を壊さない
- `uiHangScope` 導入以外の unrelated change は入れない

### 4.2 manual player resize

- `Views/Main/MainWindow.Player.cs`
  - manual player 表示時だけ `SizeChanged` による viewport 再計算を有効化する
  - 二重 hook を避けるための guard を入れる
  - handler は `PlayerArea.Visibility == Visible` の時だけ `UpdateManualPlayerViewport()` を呼ぶ
  - hook 追加箇所は manual player 表示経路に限定する
- 既存の timer / dispatcher / ui hang fault 縮退契約には触れない

## 5. テスト

- 最小でよいので 2 点を固定する
  - startup 側
    - `TrackUiHangActivity(UiHangActivityKind.Startup)` が `MainWindow.Startup.cs` に入っていること
  - player 側
    - resize hook が二重登録されないこと、または visible 時だけ viewport 更新へ流すこと
- source テストでもよいが、runtime 契約へ少しでも寄せられるなら優先する

## 6. 禁止

- `dispatcher timer` fault 縮退帯の accepted 契約を崩す変更
- suppress 条件、global fault 記録、safe stop/start への変更
- `Bookmark` / `DebugTab` / `UiHangNotification` への波及
- docs の更新

## 7. 検証

- `git diff --check`
- 可能なら対象テスト
- 全体 build / test が別件で止まる場合は blocker を明記する

## 8. 返却形式

- 変更ファイル一覧
- startup activity で何を足したか
- manual player resize で何を足したか
- 追加 / 修正テスト
- 実行した確認コマンドと結果
- 残リスク
