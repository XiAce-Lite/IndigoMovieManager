# AI向け 作業指示 Codex Q8c TabsResidual再帯分け 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `UpperTabs` / `BottomTabs` / `UserControls` / `Views/Main/MainWindow.xaml` に散っている UI dirty を、機能単位の commit 帯へ戻す。

## 2. 主対象

- `UpperTabs/DuplicateVideos/*`
- `UpperTabs/Rescue/*`
- `BottomTabs/*`
- `UserControls/*`
- `Views/Main/MainWindow.xaml`

## 3. やること

1. 少なくとも
   - DuplicateVideos
   - RescueTab
   - BottomTabs
   - UserControls / Xaml
   の 4 帯へ分ける
2. 既に commit 済みの rescue tab relay と重複しないか確認する
3. commit 可能帯と凍結帯を分ける

## 4. 返却物

- 帯一覧
- 各帯の対象ファイル
- 既存 commit との重複有無
- 次に切るべき最小レーン
