# AI向け 作業指示 Codex ThumbnailCore handoff整理 2026-03-20

最終更新日: 2026-03-20

## 1. あなたの役割

- あなたは `Lane C: Thumbnail.Core 集約` の実装役である
- 今回は queue / rescue handoff 判定の host 依存を薄くする

## 2. 目的

- App と `RescueWorker` の両方で散っている handoff 判断を、共通 core へ寄せやすい形へ一段整理する
- ただし big bang で再編しない

## 3. 主に見る場所

- `Thumbnail\MainWindow.ThumbnailQueue.cs`
- `Thumbnail\MainWindow.ThumbnailFailureSync.cs`
- `Thumbnail\MainWindow.ThumbnailRescueWorkerLauncher.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\`
- `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`
- `Thumbnail\AI向け_引き継ぎ_Thumbnail基盤整理と次着手_2026-03-20.md`

## 4. 今回やってよいこと

- handoff 判定を helper / coordinator / policy へ寄せる
- host 依存の薄化
- 最小ログ補強
- 最小テスト追加

## 5. 今回やってはいけないこと

- `Factory + Interface + Args` を壊すこと
- `ThumbnailCreationService` に orchestration を戻すこと
- `RescueWorkerApplication` を大規模に作り直すこと
- UI から見える挙動を広く変えること

## 6. 完了条件

1. timeout handoff と failure handoff の判断場所が今より見やすい
2. App 専用ロジックと worker 専用ロジックが少しでも減っている
3. 既存の rescue 実動画検証観点を壊していない
4. 追加した変更に対するテストまたは既存テストの補強がある

## 7. 最低限の確認

- 関連ユニットテスト
- 追加した policy / helper のテスト
- 既存の `ThumbnailCreationService` 境界テストに影響がないこと

## 8. レビュー時に見てほしい点

- 責務逆流がないか
- host 依存が本当に薄くなっているか
- rescue の説明可能性が上がっているか

## 9. 次へ渡す相手

- Claude / Opus レビュー専任
