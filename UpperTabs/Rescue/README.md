# UpperTabs Rescue

このフォルダは、上側 `救済` タブの UI と一覧組み立てコードを置く場所です。

## 役割

- 対象タブ (`Grid / Small / Big / List / 5x2`) を選び、そのタブで失敗している動画だけを上側タブで見せる
- 集計元は既存の `ThumbnailErrorRecordViewModel` / `FailureDb` 集計を再利用し、重いロジックを二重実装しない
- 一覧は `List` タブより薄い DataGrid にして、画像・基本情報・救済状態だけを見せる
- 選択行だけ下段の履歴ペインを引き、`FailureDb` の親行 + rescue 試行行から「何を試してどう終わったか」を時系列で見せる
- 下段ボタンの `autogen` / `ffmpeg` / `ffmediatoolkit` / `opencv` は、選択行1件だけを各エンジン固定で単発実行する
- タブがアクティブでも自動更新はせず、`更新` ボタンを押した時だけ一覧を組み直す
- `一括通常再試行` は、現在表示中の行だけを対象タブの通常キューへ優先再投入する
- `一括通常再試行` は、上側 `救済` タブ自身 (`tab=5`) ではなく、各行が持つ元タブ (`0..4`) へ戻す
- そのため通常再試行時だけ、現在タブ一致チェックは `bypassTabGate=true` で明示的に外す
- `インデックス再構築` は `FailureDb` を経由せず、manual slot の rescue worker を direct モードで起動する
- 成功時は `*_repair_*` の新動画パスを worker stdout から受け取り、右下 popup へ返す

## 主要ファイル

- `RescueTabView.xaml`
- `RescueTabView.xaml.cs`
- `UpperTabRescueTabPresenter.cs`
- `UpperTabRescueHistoryPresenter.cs`
- `MainWindow.UpperTabs.RescueTab.cs`
- `MainWindow.UpperTabs.RescueTab.History.cs`
- `UpperTabRescueTargetOption.cs`
- `UpperTabRescueListItemViewModel.cs`
- `UpperTabRescueHistoryItemViewModel.cs`
