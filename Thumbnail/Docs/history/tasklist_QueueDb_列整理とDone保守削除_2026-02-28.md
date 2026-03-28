# QueueDB タスクリスト（列整理 + Done保守削除）

## 目的
- QueueDBは未使用列を持たない状態を維持する。
- `Done` 履歴は当日分のみ保持し、前日以前を定期削除して肥大化を防ぐ。

## タスク
- [ ] 1. 列使用実態の最終監査
  - `QueueDbService` / `ThumbnailQueueProcessor` / `QueuePipeline` / テストを横断し、列参照漏れがないことを確認する。
  - 監査結果を本ファイル末尾へ記録する。

- [ ] 2. QueueDBスキーマ更新
  - `IX_ThumbnailQueue_DoneRetention (MainDbPathHash, Status, UpdatedAtUtc)` を追加する。
  - 既存DBでも `EnsureCreated` で追従作成されることを確認する。

- [ ] 3. 保守削除APIの追加
  - `QueueDbService` に `DeleteDoneOlderThan(DateTime cutoffLocalDateStart)` を追加する。
  - 削除条件:
    - `MainDbPathHash = @MainDbPathHash`
    - `Status = Done`
    - `UpdatedAtUtc < @CutoffUtc`

- [ ] 4. 実行タイミングの組み込み
  - 起動時に1回実行する。
  - 日付切替後の最初のキュー処理開始時に1回実行する（1日1回）。

- [ ] 5. ログ出力追加
  - 削除件数を `queue-ops` へ記録する。
  - 0件でも実行ログは残す（運用確認用）。

- [ ] 6. テスト追加
  - `Done` のみ削除されること。
  - `Pending/Processing/Failed/Skipped` が削除されないこと。
  - 境界時刻（当日00:00）で誤削除がないこと。

- [ ] 7. 回帰確認
  - サムネイル作成キューの通常動作（投入/取得/Done遷移/再試行）が維持されること。
  - ビルド成功（MSBuild）。

## 完了条件
- スキーマ更新・API追加・実行組み込み・テストが揃っている。
- 仕様書と実装が一致している。
- `debug-runtime.log` に保守削除実行ログが出る。
