# サムネイルキュー専用DB・非同期処理アーキテクチャ テストコード設計プラン

> 対象設計書: `plan_サムネイルキュー専用DB_非同期処理アーキテクチャ最終設計.md`
> 参考実装計画: `Implementation Plan.md`
> 目的: キューの永続化、排他制御（複数プロセス）、非同期処理フローの正確性を担保する自動テストを実装する

---

## 1. テスト戦略の方針

- **フレームワーク**: NUnit / xUnit / MSTest (既存プロジェクトに準拠)
- **モック**: Moq / NSubstitute (依存関係の切り離し)
- **データベース**: 軽量なインメモリSQLite (`Data Source=:memory:`)、または実ファイル(一時ディレクトリ)を用いたファイルベースI/Oテストを併用
- **非同期の検証**: `Task.Delay` や `Channel` などの非同期機能に対する完了待機やキャンセルのアサート

---

## 2. 実装フェーズ（Phase 1〜4）に対応するユニット・結合テスト

### 2.1 【Phase 1】 QueueDBアクセス層のテスト (`QueueDbService` / `QueueDbSchema` / `QueueDbPathResolver`)
- **目的**: データベースの作成、スキーマの適用、および基本CRUDが正確に機能すること。
- **項目**:
  - `QueueDbPathResolver`: 指定されたメインDBパスに対して、正しいハッシュを含んだ `%LOCALAPPDATA%` 配下のパスを生成するか。
  - `QueueDbSchema`: 初回接続時に正しくテーブルとインデックスが作成され、PRAGMA設定（WAL等）が適用されるか。
  - `QueueDbService.Upsert`: 新規レコードおよび重複キーによる `Status = Pending`, `UpdatedAtUtc` 更新の検証（DELETE・INSERT競合の防止確認）。
  - `QueueDbService.GetPendingAndLease`: `Pending` および 期限切れ `Processing` レコードを正しく `Processing` に更新してリースを取得できるか。
  - `QueueDbService.UpdateStatus`: `Done`, `Failed`, `Pending` (再試行時) へのステータス遷移が正しく行われるか。

### 2.2 【Phase 2】 Producer/Persisterのテスト (`Channel` / `ThumbnailQueuePersister`)
- **目的**: UIイベントからの非同期投入と、バッチによるDBへの書き出し処理が機能すること。
- **項目**:
  - `Producer`: イベントリスナー（`MainWindow.ThumbnailQueue.cs` 相当部）から `Channel<QueueRequest>` へ直ちに `TryWrite` され、呼び出しがブロックされないこと。
  - `Producer`: 同一キーの連続投入でデバウンス窓（800ms）内は抑止され、窓外は再投入できること。
  - `ThumbnailQueuePersister`: 
    - `Channel` からリクエストを取り出し、100〜300msのバッチ間隔で `QueueDbService.Upsert` が呼び出されること。
    - 同一バッチ内の同一キー要求が最新1件に圧縮され、重複Upsertが削減されること。
    - キャンセルトークンによる停止時、残りのバッチ処理が安全に中断（または短時間で完了）すること。

### 2.3 【Phase 3】 Consumerのテスト (`ThumbnailQueueProcessor` 移行部)
- **目的**: DBからのリース取得とステータス更新、エラー時の再試行制御の確実性。
- **項目**:
  - **成功時**: リース取得 → サムネイル生成実処理 → `Done` 更新のフロー確認。
  - **再試行エラー時**: 例外送出時に `AttemptCount` インクリメント後 `Pending` に戻ること。
  - **致命的エラー/閾値超過時**: `Failed` に遷移し `LastError` にスタックトレース等が記録されること。
  - **リース延長**: 生成処理に長時間を要すシミュレーション時、定期的に `LeaseUntilUtc` が延長されること。

### 2.4 【Phase 4】 起動・終了・リカバリのテスト
- **目的**: アプリケーションライフサイクル全体の中断・再開耐性を確認する。
- **項目**:
  - **起動復元**: モックDBに `Pending` と期限切れ `Processing` を用意し、起動時にそれらが自動で拾われて再処理に回ること。
  - **即終了 (Instant Exit)**: `CancellationTokenSource.Cancel()` 発行時、同期 `Flush` が呼び出されず、`ThumbnailQueuePersister` および `ThumbnailQueueProcessor` の処理ループが短時間(最大500ms等)で安全離脱すること。

### 2.5 【Phase 5】 ログ・運用テスト
- **目的**: 運用監視と手動再試行が実用的に機能すること。
- **項目**:
  - `enqueue_total / upsert_submitted_total / db_affected_total / db_inserted_total / db_updated_total / lease_total / failed_total` が処理進行に応じて増加すること。
  - `upsert_submitted_total` と `db_affected_total` の差分が、`db_skipped_processing_total` と整合すること。
  - `ResetFailedThumbnailJobsForCurrentDb` 実行で `Failed -> Pending` 件数が期待通り戻ること。
  - Persister/Consumer例外時に上位監視ループで再起動し、アプリ全体が停止しないこと。
  - サムネイル作成中ダイアログがバッチごとに開閉せず、キュー存続中は単一表示を維持すること。
  - QueueDB未完了件数が0になったタイミングでのみダイアログが閉じること。

---

## 3. インテグレーション・並行処理テスト (Integration & Concurrency)

SQLiteを介した複数層・複数プロセスの連携を検証します。

### 3.1 3層貫通テスト (End-to-End キューシミュレーション)
- リクエストを `Channel` (Producer) に入れ、`Persister` がDBへ書き込み、さらに並行稼働する `Consumer` がDBからリース取得して `Done` にするまでの一連のフローが正しく完了するか。

### 3.2 複数プロセス排他制御（リース競合テスト）
- **目的**: 同一のメインDBに対して複数のアプリケーションインスタンス（テスト上は異なる `OwnerInstanceId` を持つ複数タスク）が競合した場合の安全性を確認。
- **シミュレーション**:
  - 複数の `Consumer` が同時に `QueueDbService.GetPendingAndLease`（ `BEGIN IMMEDIATE TRANSACTION` ）を実行。
  - 同一のジョブ(`QueueId`)が重複して2つのインスタンスにリースされないこと(排他ロックの確認)。
  - `PRAGMA busy_timeout` によってロックエラー（SqliteException: db is locked）にならずに待機・取得成功するか。
  - クラッシュ時に他の `InstanceId` の `LeaseUntilUtc` が超過指定時刻を過ぎた際、別インスタンスがリースを奪えること。

---

## 4. 手動テストケースとのマッピング
`Implementation Plan.md` 内「6. 手動テストタスク」をコードで自動検証可能にするマッピング方針。

| 手動テスト項目 | 自動化テスト対象スコープ |
|---|---|
| 1. 単一プロセスで10件投入・全件Done確認 | 【3.1】3層貫通テストでシミュレート可能 |
| 2. 2プロセス同時起動・重複生成なし確認 | 【3.2】複数プロセス排他制御（異なるInstanceIdによる並列Consumerテスト） |
| 3. 強制終了後の再起動・未完了ジョブの再開 | 【2.4】起動復元のテストでカバー可能 |
| 4. 1000件投入のUI操作ブロックなし | 【2.2】Producer部の `TryWrite` 応答時間ベンチマークテスト |
| 5. 失敗ジョブの手動Pending戻し・再実行 | 【2.1】QueueDbServiceメソッドによるステータス変更後のConsumer取得テスト |
| 6. 同一イベント連打時の膨張抑止 | 【2.2】Producerデバウンス + Persister重複圧縮の結合テスト |
| 7. 進捗ダイアログのチラつき防止 | 【2.5】セッション単位シングルトン表示の検証 |

---

## 5. テスト実装ステップ

1. **Phase 1**: `QueueDbService` と `QueueDbSchema` 周りの単体CRUDテスト（SQLiteファイルベース）
2. **Phase 2**: `ThumbnailQueuePersister` と `Channel` の連携テスト（モックDB使用）
3. **Phase 3**: `ThumbnailQueueProcessor` の Consumer非同期ワーカーループと状態遷移テスト
4. **Phase 4**: 複数タスクを用いたリース競合トランザクションテストの記述と、起動・終了パイプラインの結合テスト
