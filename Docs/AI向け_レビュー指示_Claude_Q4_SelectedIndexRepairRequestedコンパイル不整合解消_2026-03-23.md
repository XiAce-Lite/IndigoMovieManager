# AI向け レビュー指示 Claude Q4 SelectedIndexRepairRequestedコンパイル不整合解消 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `SelectedIndexRepairRequested` の XAML / code-behind 契約不整合だけを見て、受け入れ可能か判定する

## 2. レビュー対象

- `UpperTabs/Rescue/RescueTabView.xaml.cs`
- 必要なら `UpperTabs/Rescue/RescueTabView.xaml`
- 必要なら対応テスト

## 3. 必ず見る観点

- `SelectedIndexRepairRequested` が `Views/Main/MainWindow.xaml` の要求と一致しているか
- event 公開面が rescue tab の compile blocker 解消に閉じているか
- `UpperTabs/Rescue/MainWindow.UpperTabs.RescueTab.cs` の実処理責務へ広げていないか
- unrelated event / control 公開を混ぜていないか

## 4. 今回レビューしないもの

- `Watcher`
- `UI hang`
- `Thumbnail`
- docs

## 5. 出力形式

- findings を severity 順で列挙
- なければ `findings なし`
- 最後に受け入れ可否を 1 行で明記
