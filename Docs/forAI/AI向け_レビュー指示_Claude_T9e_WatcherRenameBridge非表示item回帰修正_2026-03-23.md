# AI向け レビュー指示 Claude T9e WatcherRenameBridge非表示item回帰修正 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `RenameBridge` が visible item 依存へ戻っていないか、hidden item rename でも契約が維持されるかを判定する

## 2. レビュー対象

- `Watcher/MainWindow.WatcherRenameBridge.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRenameBridgePolicyTests.cs`

## 3. 必ず見る観点

- rename 対象解決が表示中 `MovieRecs` だけに閉じていないか
- hidden / filtered / page外 item の rename でも DB / bookmark / thumbnail rename が通るか
- stale watch scope guard が弱くなっていないか
- テストが visible / hidden の両方を固定しているか

## 4. 今回レビューしないもの

- `WatchScanCoordinator`
- `Created` queue 契約
- `UI hang`

## 5. 出力形式

- findings を severity 順で列挙
- なければ `findings なし`
- 最後に受け入れ可否を 1 行で明記
