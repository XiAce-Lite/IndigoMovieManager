# AI向け レビュー指示 Claude T9b WatcherRenameBridge安全契約復元 2026-03-22

最終更新日: 2026-03-22

## 1. 目的

- `Watcher RenameBridge` 単独レーンが、安全契約を本当に復元できているかを判定する

## 2. レビュー対象

- `Watcher/MainWindow.WatcherRenameBridge.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRenameBridgePolicyTests.cs`

## 3. 必ず見る観点

- stale watch scope の rename が別 DB / 別 UI 状態へ流れないか
- snapshot / guard / rollback の境界が戻っているか
- hash jpg / `.#ERROR.jpg` / bookmark jpg / bookmark DB rename が他 owner や部分一致へ誤爆しないか
- `Movie_Path` / `Movie_Name` 以外の rename 後 state 契約が崩れていないか
- broad exception swallow へ戻っていないか
- rollback が実施済み段だけを戻すことをテストで固定しているか

## 4. 今回レビューしないもの

- `WatchScanCoordinator`
- `WatchFolderDropRegistrationPolicy`
- `WatcherRegistrationDirectPipelineTests`
- `EventQueue` / `Created direct pipeline`

## 5. 出力形式

- findings を severity 順で列挙
- なければ `findings なし`
- 最後に受け入れ可否を 1 行で明記
