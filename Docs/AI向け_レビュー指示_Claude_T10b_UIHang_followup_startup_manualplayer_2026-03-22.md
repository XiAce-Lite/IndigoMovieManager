# AI向け レビュー指示 Claude T10b UIHang follow-up startup manualplayer 2026-03-22

最終更新日: 2026-03-22

## 1. 役割

- あなたはコードレビュー専任
- findings first
- severity 順で返す

## 2. レビュー対象

- `Views/Main/MainWindow.Startup.cs`
- `Views/Main/MainWindow.Player.cs`
- 関連テスト

## 3. レビュー観点

1. `TrackUiHangActivity(UiHangActivityKind.Startup)` の追加が startup の処理順を壊していないか
2. manual player resize hook が二重登録されないか
3. visible でない時に不要な viewport 更新をしないか
4. `dispatcher timer` fault 縮退帯の accepted 契約へ触れていないか
5. unrelated change が混ざっていないか

## 4. 期待する出力

- findings を severity 順で列挙
- なければ `findings なし`
- 最後に受け入れ可否を 1 行で明記
