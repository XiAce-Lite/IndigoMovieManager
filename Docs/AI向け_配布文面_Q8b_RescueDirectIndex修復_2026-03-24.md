# Q8b 配布文面（RescueDirectIndex 修復）

## 実装向け（Codex）
- 対象: `Q8b`
- 参照資料:
  - [AI向け_作業指示_Codex_Q8b_RescueDirectIndex修復_2026-03-24.md](Docs/AI向け_作業指示_Codex_Q8b_RescueDirectIndex修復_2026-03-24.md)
- 作業範囲:
  - `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
  - `Thumbnail/MainWindow.ThumbnailRescueWorkerLauncher.cs`
  - `Thumbnail/MainWindow.ThumbnailRescueManualPopup.cs`
  - `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
  - `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs`
  - `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
- 指示:
  - 上記 6 ファイルのみを clean 再構成し、`requestedFailureId` の渡し先/復元経路を固定する
  - `Q8b L2`/`L3` は触らない
  - 差分は 1 帯で検証しやすい形に収束

## レビュー向け（Claude）
- 対象: `Q8b-R`
- 参照資料:
  - [AI向け_レビュー指示_Claude_Q8b_RescueDirectIndex修復_2026-03-24.md](Docs/AI向け_レビュー指示_Claude_Q8b_RescueDirectIndex修復_2026-03-24.md)
- 審査観点:
  - requestedFailureId の一貫性
  - cleanup と DB 更新順序
  - no-op レーン混入なし
  - 既存挙動の回帰最小化

## 結果受領
- 受け入れ: findings なし
- 再差し戻し: 具体的な行番号と期待挙動を明記して返却
