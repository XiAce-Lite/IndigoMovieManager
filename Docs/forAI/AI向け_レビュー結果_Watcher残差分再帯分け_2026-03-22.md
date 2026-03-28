# AI向け レビュー結果 Watcher残差分再帯分け 2026-03-22

最終更新日: 2026-03-22

変更概要:
- `T9b` / `T9c` 取り込み後に残っている `Watcher` 系 dirty を、レビュー専任サブエージェントで再帯分けした
- `Created` / `RenameBridge` / `UI hang` 回帰が混在しており、単一帯では扱えないと判定した
- 次に切る最小レーンを `Watcher Created snapshot queue契約復元` に固定した

## 1. 判定

- 単一帯での受け入れ: 不可
- 次に切る最小レーン: `T9d Watcher Created snapshot queue契約復元`
- 受け入れ前の帯分け判定: `Created` の DB / tab snapshot 汚染だけを先に切る

## 2. findings

- `High`
  - `App.xaml.cs:146-149`
  - `StartupUri` 継続下で `DispatcherUnhandledException` 登録が `base.OnStartup(e)` 後ろに下がっており、起動直後の `DispatcherTimer` 失敗を縮退前に落とし得る
- `High`
  - `Watcher/MainWindow.WatcherEventQueue.cs:167-170`
  - `Created` queue 要求が `FullPath` しか持たず、ready 待機中の DB 切替 / tab 移動後に別スコープへ `QueueCheckFolderAsync` し得る
- `High`
  - `Watcher/MainWindow.WatcherRenameBridge.cs:259-266`
  - `RenameBridge` が表示中 `MovieRecs` 依存へ戻っており、非表示 item の rename で DB / bookmark / サムネが旧名残りする
- `Medium`
  - `Views/Main/MainWindow.DispatcherTimerSafety.cs:41-44`
  - `DispatcherTimer.Start()` 失敗時に fault 状態が立たず、縮退停止が効かない

## 3. 次レーンを `T9d` に固定する理由

- `Created` 汚染は `WatcherEventQueue` と `WatcherRegistrationDirectPipelineTests` へ閉じやすい
- `RenameBridge` 後続 dirty は、すでに受け入れ済み契約を再度崩しており、別レーンで凍結 / 切り戻し判断が必要
- `UI hang` 側の回帰は `Watcher` 帯へ混ぜると境界が壊れる

## 4. `T9d` の対象候補

- `Watcher/MainWindow.WatcherEventQueue.cs`
- `Watcher/MainWindow.Watcher.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRegistrationDirectPipelineTests.cs`

## 5. `T9d` の受け入れゲート

- `Created` queue 要求が DB / tab / bypass 条件の snapshot を保持する
- ready 後に current `MainVM` へ寄り直さず、要求 snapshot を使って `QueueCheckFolderAsync` へ渡す
- ready 待機中の DB 切替 / tab 移動をテストで再現し、別スコープ汚染を防げている
- `RenameBridge` / `WatchScanCoordinator` / `UI hang` へ差分が広がらない

## 6. 補足

- 今回のレビュー出力は、帯分け用レビューとして実施した
- `RenameBridge` の後続 dirty と `UI hang` 回帰は、次帯へ混ぜず凍結維持とする
