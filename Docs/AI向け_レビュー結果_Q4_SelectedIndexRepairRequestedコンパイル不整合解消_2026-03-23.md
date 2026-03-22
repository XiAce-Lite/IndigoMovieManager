# AI向け レビュー結果 Q4 SelectedIndexRepairRequestedコンパイル不整合解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `RescueTabView` の公開イベント契約を最小追加し、`SelectedIndexRepairRequested` の XAML compile blocker を解消した
- clean worktree では build 成功まで確認した
- main worktree の同名ファイルにはより広い dirty があるため、本線取り込みは deferred とした

## 1. 対象

- `UpperTabs/Rescue/RescueTabView.xaml.cs`

## 2. 実行場所

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q4-selectedindexrepair`

## 3. 着地

- `SelectedIndexRepairRequested`
- `SelectedBlackConfirmRequested`
- `SelectedBlackLiteRetryRequested`
- `SelectedBlackDeepRetryRequested`
  を `RescueTabView` の公開イベントとして追加

## 4. 検証

- `dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64`
  - 成功
- `git diff --check`
  - 通過
- `git diff --cached --check`
  - 通過

## 5. 残留注意

- 新規公開イベント 4 件は compile blocker 解消に必要な公開面だけを足しており、この clean worktree では UI から発火していない
- `CS0067` 警告は残る
- main worktree の `UpperTabs/Rescue/RescueTabView.xaml.cs` は、同じ event 宣言に加えて button click relay まで含む別テーマ dirty を持つ
- そのため、本線へは即取り込みせず、後で同ファイルの帯分けを行う

## 6. 判定

- 実装判定
  - 受け入れ
- レビュー判定
  - 調整役の手動レビューで受け入れ
- clean commit
  - `ffa061eb5f81ed356eedc5b3c6dc2189f909dbd3`
  - `RescueTabViewに選択救済操作イベントを追加`
- 本線取り込み
  - 未実施
