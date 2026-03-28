# AI向け 作業指示 Codex T9e WatcherRenameBridge非表示item回帰修正 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `RenameBridge` が表示中 `MovieRecs` だけへ依存している回帰を止める
- 検索中 / 別タブ / ページ外でも、watch rename が DB / bookmark / サムネ契約を壊さないように戻す

## 2. 対象ファイル

- `Watcher/MainWindow.WatcherRenameBridge.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRenameBridgePolicyTests.cs`

## 3. 必達

- rename 対象解決が「現在見えている行だけ」で完結しない
- 非表示 item の rename でも DB path / bookmark / thumbnail rename 契約が維持される
- stale watch scope guard を崩さない
- visible item と hidden item の両方をテストで固定する

## 4. 禁止

- `Watcher/MainWindow.Watcher.cs` を触らない
- `Watcher/MainWindow.WatchScanCoordinator.cs` を触らない
- `App.xaml.cs` / `Views/Main/MainWindow.DispatcherTimerSafety.cs` を触らない
- docs 更新をこのレーンへ混ぜない

## 5. 受け入れ条件

- `RenameBridge` だけでレビュー専任役が `findings なし`
- 対象外ファイルへ差分が広がらない
- 非表示 item rename の回帰がテストで見える

## 6. 返却時に必要な報告

- 変更ファイル一覧
- 追加 / 修正テスト一覧
- 実行した確認コマンドと結果
- 残リスク
