# AI向け 作業指示 Codex Watcher CheckFolderAsync薄化 2026-03-20

最終更新日: 2026-03-20

## 1. あなたの役割

- あなたは `Lane A: App shell 薄化` の実装役である
- 今回は `CheckFolderAsync` に残る orchestration を、壊さずにもう一段薄くする

## 2. 目的

- `Watcher` の責務分離を前進させる
- 将来 `Data` と `Core` へ渡しやすい境界へ寄せる
- UI 詰まり防止を維持する

## 3. 主に見る場所

- `Watcher\MainWindow.Watcher.cs`
- `Watcher\MainWindow.WatchScanCoordinator.cs`
- `Watcher\MainWindow.WatcherEventQueue.cs`
- `Watcher\MainWindow.WatcherUiBridge.cs`
- `Watcher\AI向け_引き継ぎ_Watcher責務分離_UI詰まり防止_2026-03-20.md`

## 4. 今回やってよいこと

- `visible-only gate`
- zero-byte 早期スキップ
- first-hit 通知
- final queue flush
- 上のどれかを coordinator / helper へ寄せる
- watch 系の最小テスト追加

## 5. 今回やってはいけないこと

- `Created` の直書き MainDB 登録復活
- `Renamed` の直呼び復活
- `FilterAndSort(..., true)` の即時全面再評価復活
- 左ドロワー抑制を強制中断へ変えること
- deferred batch の保存規約を壊すこと

## 6. 完了条件

1. `CheckFolderAsync` の責務が今より一段読みやすい
2. `visible-only gate`、deferred batch、UI 抑制の整合が維持されている
3. watch 多発時のテンポ悪化を招く変更がない
4. 追加した変更に対するテストがある

## 7. 最低限の確認

- watch 系テスト
- `WatchUiSuppressionPolicyTests`
- 今回追加した helper / coordinator のテスト

## 8. レビュー時に見てほしい点

- UI 抑制の前提を壊していないか
- coordinator へ寄せた結果、逆に `MainWindow` 依存が増えていないか
- 件数上限 200、strictly newer 境界、左ドロワー抑制と衝突していないか

## 9. 次へ渡す相手

- Claude / Opus レビュー専任
