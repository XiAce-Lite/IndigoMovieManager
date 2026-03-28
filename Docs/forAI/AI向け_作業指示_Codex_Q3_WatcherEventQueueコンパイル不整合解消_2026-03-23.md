# AI向け 作業指示 Codex Q3 WatcherEventQueueコンパイル不整合解消 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- clean worktree で `Watcher/MainWindow.WatcherRegistration.cs` を止めている compile blocker を、`WatcherEventQueue` partial に限定して解消する
- `QueueWatchEventAsync` / `WatchEventRequest` / `WatchEventKind` の定義を、受け入れ済み watcher 契約を壊さずに repo 管理下へ戻す

## 2. 現在の blocker

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q2-startupfeed-synth`
- 停止箇所
  - `Watcher/MainWindow.WatcherRegistration.cs:27`
  - `Watcher/MainWindow.WatcherRegistration.cs:28`
  - `Watcher/MainWindow.WatcherRegistration.cs:62`
  - `Watcher/MainWindow.WatcherRegistration.cs:63`
- 症状
  - `QueueWatchEventAsync` が存在しない
  - `WatchEventRequest` が見つからない
  - `WatchEventKind` が見つからない
- 根因候補
  - main worktree では `Watcher/MainWindow.WatcherEventQueue.cs` が untracked で存在する
  - clean worktree では同ファイルが存在しない

## 3. 作業場所

- 新しい clean worktree を切って作業する
- main worktree の dirty は触らない

## 4. 変更対象

- 触ってよいファイル
  - `Watcher/MainWindow.WatcherEventQueue.cs`
  - `Watcher/MainWindow.WatcherRegistration.cs`
  - 必要なら watcher 系の対応テストのみ
    - `Tests/IndigoMovieManager_fork.Tests/WatcherRegistrationDirectPipelineTests.cs`
- 参照してよいファイル
  - `Watcher/MainWindow.Watcher.cs`
  - `Watcher/MainWindow.WatcherRenameBridge.cs`
  - `Docs/AI向け_レビュー結果_Q2_StartupFeedRequestコンパイル不整合解消_2026-03-23.md`

## 5. 必達

- `WatcherRegistration` から `QueueWatchEventAsync` / `WatchEventRequest` / `WatchEventKind` が見える
- `WatcherEventQueue` は partial として compile に参加する
- 変更は event queue compile blocker に閉じる
- 既に受け入れ済みの `RenameBridge` / `Created snapshot` / `UI hang` 契約を戻さない

## 6. 禁止

- main worktree の untracked ファイルをそのまま信用して大帯を混ぜない
- `Watcher/MainWindow.Watcher.cs` へ event queue 定義を戻して責務を逆流させない
- `RenameBridge` / `WatchScanCoordinator` / `UI hang` / docs を混ぜない
- compile を通すためだけに access modifier や nested type 契約を広げない

## 7. 受け入れ条件

- clean worktree の `dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64` で、この blocker が消える
- review 専任役が `findings なし`
- 差分が `WatcherEventQueue` partial 周辺に閉じる

## 8. 返却時に必要な報告

- 変更ファイル一覧
- 追加 / 修正テスト一覧
- 実行した build / test コマンドと結果
- 残リスク
