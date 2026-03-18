# UpperTabs Rescue

このフォルダは、上側 `救済` タブの UI と一覧組み立てコードを置く場所です。

## 役割

- 対象タブ (`Grid / Small / Big / List / 5x2`) を選び、そのタブで失敗している動画だけを上側タブで見せる
- 集計元は既存の `ThumbnailErrorRecordViewModel` / `FailureDb` 集計を再利用し、重いロジックを二重実装しない
- 一覧は `List` タブより薄い DataGrid にして、画像・基本情報・救済状態だけを見せる
- タブがアクティブでも自動更新はせず、`更新` ボタンを押した時だけ一覧を組み直す

## 主要ファイル

- `RescueTabView.xaml`
- `RescueTabView.xaml.cs`
- `MainWindow.UpperTabs.RescueTab.cs`
- `UpperTabRescueTargetOption.cs`
- `UpperTabRescueListItemViewModel.cs`
