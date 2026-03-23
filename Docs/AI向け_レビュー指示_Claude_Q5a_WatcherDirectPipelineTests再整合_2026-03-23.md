# AI向け レビュー指示 Claude Q5a WatcherDirectPipelineTests再整合 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `WatcherRegistrationDirectPipelineTests` が現行 source 契約を見ているかだけを review する

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/WatcherRegistrationDirectPipelineTests.cs`
- 必要なら参照のみ
  - `Watcher/MainWindow.WatcherEventQueue.cs`
  - `Watcher/MainWindow.WatcherRegistration.cs`
  - `Watcher/MainWindow.Watcher.cs`

## 3. 必須観点

1. test が旧 direct helper 契約へ依存していないか
2. `Created` が queue 契約へ流れる現行挙動を見ているか
3. `Renamed` が queue / rename 本流の現行契約を壊していないか
4. source へ test 専用の公開面逆流が起きていないか

## 4. 受け入れ条件

- findings first
- symbol drift 解消のために source を古い形へ戻していない
- test の観測点が現行 seam に閉じている
