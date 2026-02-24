# Implementation Plan（サムネイルキュー専用DB + 非同期3層）

## 1. 目的
- `plan_サムネイルキュー専用DB_非同期処理アーキテクチャ最終設計.md` を実装可能な粒度へ分解する
- 既存の in-memory キュー中心実装を、`Producer -> Persister -> Consumer` のDB中心実装へ移行する
- 実装中の手戻りを避けるため、事前確認・変更順・完了条件を固定する

## 2. 事前準備チェックリスト
- [ ] `Thumbnail/MainWindow.ThumbnailQueue.cs` の重複キー管理（`queuedThumbnailKeys`）の利用箇所を洗い出す
- [ ] `Thumbnail/ThumbnailQueueProcessor.cs` の `ConcurrentQueue<QueueObj>` 依存箇所を洗い出す
- [ ] 起動時処理と終了時処理で、サムネイルキュー開始/停止の呼び出し点を特定する
- [ ] `%LOCALAPPDATA%\\IndigoMovieManager\\QueueDb\\` の作成責務を決める（起動時に必ず作成）
- [ ] `System.Data.SQLite` の既存利用箇所を確認し、接続文字列・トランザクション方針を統一する

## 3. 実装タスクリスト（フェーズ別）

## Phase 1: QueueDBアクセス層を追加
- [ ] `Thumbnail/QueueDb/QueueDbPathResolver.cs` を追加する
- [ ] `Thumbnail/QueueDb/QueueDbService.cs` を追加する
- [ ] `Thumbnail/QueueDb/QueueDbSchema.cs` を追加し、テーブル・インデックス・PRAGMA適用を実装する
- [ ] `Pending` 取得、`Lease` 取得、`Status` 更新、`Upsert` のSQLを `QueueDbService` に集約する
- [ ] `Status` 列挙体（`Pending/Processing/Done/Failed/Skipped`）を追加する

## Phase 2: Producer/Persisterを導入
- [ ] `Thumbnail/QueuePipeline/QueueRequest.cs` を追加する
- [ ] `Thumbnail/QueuePipeline/ThumbnailQueuePersister.cs` を追加する
- [ ] `Channel<QueueRequest>` を導入し、Watcher/D&D側は `TryWrite` のみ行うように変更する
- [ ] 既存 `TryEnqueueThumbnailJob` は直接 `queueThumb.Enqueue` せず、Producer経由に置換する
- [ ] Persisterで100〜300msバッチのUpsertを実装する

## Phase 3: ConsumerをDBリース中心へ移行
- [ ] `Thumbnail/ThumbnailQueueProcessor.cs` を改修し、DBからのリース取得ループを実装する
- [ ] 処理開始時に `Processing + OwnerInstanceId + LeaseUntilUtc` を必ず設定する
- [ ] 成功時 `Done`、再試行時 `Pending + AttemptCount++`、致命時 `Failed/Skipped` を実装する
- [ ] 長時間処理中ジョブの `LeaseUntilUtc` 延長処理を実装する
- [ ] 既存 `ConcurrentQueue<QueueObj>` 依存を段階的に削除する

## Phase 4: 起動復元・終了処理の確定
- [ ] 起動時に `Pending` と期限切れ `Processing` を再処理対象として扱う
- [ ] 終了時は入力停止 -> `CancellationToken` 通知 -> 最大500ms待機へ統一する
- [ ] 同期 `Flush` を呼ばない実装に統一する
- [ ] 旧 in-memory 専用の重複管理コードを縮退または削除する

## Phase 5: 仕上げ（ログ・運用）
- [ ] キュー投入数、DB反映数、リース取得数、失敗数をログ出力する
- [ ] 手動再試行（`Failed -> Pending`）の運用手順をドキュメント化する
- [ ] 例外時にアプリ全体を止めない境界（Persister/Consumer）を確認する

## 4. 変更対象ファイル（着手順）
1. `Thumbnail/QueueDb/QueueDbPathResolver.cs`（新規）
2. `Thumbnail/QueueDb/QueueDbSchema.cs`（新規）
3. `Thumbnail/QueueDb/QueueDbService.cs`（新規）
4. `Thumbnail/QueuePipeline/QueueRequest.cs`（新規）
5. `Thumbnail/QueuePipeline/ThumbnailQueuePersister.cs`（新規）
6. `Thumbnail/MainWindow.ThumbnailQueue.cs`（Producer接続）
7. `Thumbnail/ThumbnailQueueProcessor.cs`（Consumer移行）
8. 起動/終了を管理する `MainWindow` 側ファイル群（復元・停止）

## 5. 受け入れ条件（DoD）
- [ ] 監視イベント経路でDB書き込みや重い処理を直接実行していない
- [ ] キューDBが `%LOCALAPPDATA%\\IndigoMovieManager\\QueueDb\\` に生成される
- [ ] 複数プロセス同時起動で同一ジョブが二重実行されない
- [ ] 強制終了後の再起動で `Pending` ジョブが再開される
- [ ] 1000件投入時にUI操作の体感停止が発生しない
- [ ] 終了操作後に長時間待ちなくウィンドウが閉じる

## 6. 手動テストタスク
1. 単一プロセスで10件投入し、全件 `Done` になることを確認する
2. 2プロセス同時起動で同一データを投入し、重複生成がないことを確認する
3. 処理途中で強制終了し、再起動後に未完了ジョブが再開されることを確認する
4. 1000件投入し、操作中のUI応答と処理継続を確認する
5. 失敗ジョブを `Pending` に戻し、再実行できることを確認する

## 7. 実装順の推奨
1. まずQueueDB層を完成させ、SQLと状態遷移の正しさを固定する
2. 次にProducer/Persisterを接続し、イベント経路を軽量化する
3. 最後にConsumerを置換し、起動復元と終了処理を統合する
