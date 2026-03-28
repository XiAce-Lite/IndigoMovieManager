# AI向け 作業指示 Codex Q4 SelectedIndexRepairRequestedコンパイル不整合解消 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `Views/Main/MainWindow.xaml` の `SelectedIndexRepairRequested` 未解決を、`RescueTabView` 公開契約に限定して解消する
- `Q3` で解消した `WatcherEventQueue` blocker と混ぜずに、次の XAML compile blocker だけを潰す

## 2. 現在の blocker

- clean worktree build
  - `dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64`
- 停止箇所
  - `Views/Main/MainWindow.xaml(1028,23)`
- 症状
  - `SelectedIndexRepairRequested` は XML 名前空間 `clr-namespace:IndigoMovieManager.UpperTabs.Rescue` に存在しない

## 3. 作業場所

- `Q3` 受け入れ土台の上で新しい clean worktree を切る

## 4. 変更対象

- 触ってよいファイル
  - `UpperTabs/Rescue/RescueTabView.xaml.cs`
  - 必要なら `UpperTabs/Rescue/RescueTabView.xaml`
  - 必要なら対応テストのみ
- 参照してよいファイル
  - `Views/Main/MainWindow.xaml`
  - `UpperTabs/Rescue/MainWindow.UpperTabs.RescueTab.cs`
  - `Docs/AI向け_レビュー結果_Q3_WatcherEventQueueコンパイル不整合解消_2026-03-23.md`

## 5. 必達

- `SelectedIndexRepairRequested` が `RescueTabView` から見える
- `UpperTabRescueSelectedIndexRepairButton_Click` との event 契約が一致する
- 変更は rescue tab 公開契約の compile blocker 解消に閉じる
- watch / UI hang / thumbnail / docs を混ぜない

## 6. 禁止

- `MainWindow.xaml` 側の event 名を安易に変えて逃げない
- `RescueTabView` へ unrelated event や control 公開をまとめて足さない
- `UpperTabs/Rescue/MainWindow.UpperTabs.RescueTab.cs` の処理本体へ広げない
- `Q3` の watcher 差分を触らない

## 7. 受け入れ条件

- clean worktree の build で `SelectedIndexRepairRequested` blocker が消える
- review 専任役が `findings なし`
- 差分が rescue tab 公開契約に閉じる

## 8. 返却時に必要な報告

- 変更ファイル一覧
- 追加 / 修正テスト一覧
- 実行した build / test コマンドと結果
- 残リスク
