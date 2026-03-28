# AI向け 作業指示 Codex Q5a WatcherDirectPipelineTests再整合 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `WatcherRegistrationDirectPipelineTests` が旧 direct helper 契約へ張り付いて compile 不能になっている
- 現行 source の `WatcherEventQueue` / `QueueCheckFolderAsyncRequestedForTesting` 契約を正本として、test を再整合する

## 1. 目的

- test project build を止めている `WatcherRegistrationDirectPipelineTests` の symbol drift を解消する
- source 側へ旧 direct helper 契約を戻さず、現行 queue 契約へ test を寄せる

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/WatcherRegistrationDirectPipelineTests.cs`
- 必要なら参照だけ
  - `Watcher/MainWindow.WatcherEventQueue.cs`
  - `Watcher/MainWindow.WatcherRegistration.cs`
  - `Watcher/MainWindow.Watcher.cs`

## 3. 現状の停止点

- `MainWindow.CreatedWatchEventDirectResult`
- `MainWindow.ProcessCreatedWatchEventDirectAsync`
- `MainWindow.ProcessCreatedWatchEventDirectAsyncOverrideForTesting`
- `MainWindow.ProcessRenamedWatchEventDirect`

これらは現行 source には存在しない。

## 4. 守ること

1. `WatcherEventQueue` の現行契約を正本にする
2. `MainWindow` に旧 direct helper や旧 enum を戻さない
3. test は `QueueCheckFolderAsyncRequestedForTesting` など、現行の seam を使って組み替える
4. `RenameBridge` や `WatchScanCoordinator` へ unrelated change を混ぜない

## 5. 着地イメージ

- `Created` は
  - ready 待機
  - zero-byte 判定
  - `QueueCheckFolderAsync(CheckMode.Watch, "created:...")`
  へ流す現行契約を test で固定する
- `Renamed` は
  - queue 本流で `RenameThumbAsync(new, old)` へ流れる契約を test で固定する
- 旧 direct result enum 前提の期待値は削除する

## 6. 検証

- `MSBuild.exe Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj /t:Build /p:Configuration=Debug /p:Platform=x64`
- 可能なら対象テスト絞り込み
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter WatcherRegistrationDirectPipelineTests`

## 7. 禁止

- `Watcher/MainWindow.WatcherEventQueue.cs` に test 都合の公開面を増やすこと
- 旧 direct pipeline 実装の復活
- `MainWindow.xaml.cs` や `Views/Main` への波及
