# AI向け 作業指示 Codex T9d WatcherCreatedSnapshot契約復元 2026-03-22

最終更新日: 2026-03-22

## 1. 目的

- `Created` watch queue 要求に DB / tab / bypass の snapshot 契約を戻す
- ready 待機またぎで別スコープへ `QueueCheckFolderAsync` しないように戻す

## 2. 対象ファイル

- `Watcher/MainWindow.WatcherEventQueue.cs`
- `Watcher/MainWindow.Watcher.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRegistrationDirectPipelineTests.cs`

## 3. 必達

- `Created` queue 要求が `FullPath` 以外に必要な snapshot を保持する
- ready 後の本番経路が current `MainVM` へ寄り直さず、要求 snapshot を使って処理する
- DB 切替 / tab 切替が ready 待機中に起きても、旧 request が新スコープへ流れない
- 通常系と stale 系の両方をテストで固定する

## 4. 禁止

- `Watcher/MainWindow.WatcherRenameBridge.cs` を触らない
- `Watcher/MainWindow.WatchScanCoordinator.cs` を触らない
- `App.xaml.cs` / `Views/Main/MainWindow.DispatcherTimerSafety.cs` を触らない
- docs 更新をこのレーンへ混ぜない

## 5. 受け入れ条件

- `Created` queue 契約だけでレビュー専任役が `findings なし`
- 対象外ファイルへ差分が広がらない
- stale wait 中の DB / tab 汚染防止が runtime 寄りテストで見える

## 6. 返却時に必要な報告

- 変更ファイル一覧
- 追加 / 修正テスト一覧
- 実行した確認コマンドと結果
- 残リスク
