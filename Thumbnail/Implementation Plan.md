# Implementation Plan（サムネイルキュー専用DB + 非同期3層）

## 1. 目的
- `plan_サムネイルキュー専用DB_非同期処理アーキテクチャ最終設計.md` を実装可能な粒度へ分解する
- 既存の in-memory キュー中心実装を、`Producer -> Persister -> Consumer` のDB中心実装へ移行する
- 実装中の手戻りを避けるため、事前確認・変更順・完了条件を固定する

## 2. 事前準備チェックリスト
- [x] `Thumbnail/MainWindow.ThumbnailQueue.cs` の重複キー管理（`queuedThumbnailKeys`）の利用箇所を洗い出す
- [x] `Thumbnail/ThumbnailQueueProcessor.cs` の `ConcurrentQueue<QueueObj>` 依存箇所を洗い出す
- [x] 起動時処理と終了時処理で、サムネイルキュー開始/停止の呼び出し点を特定する
- [x] `%LOCALAPPDATA%\\IndigoMovieManager\\QueueDb\\` の作成責務を決める（起動時に必ず作成）
- [x] `System.Data.SQLite` の既存利用箇所を確認し、接続文字列・トランザクション方針を統一する

### 2.1 調査結果（実装前固定）
- `queuedThumbnailKeys` の実利用は実装前時点で「予約: `TryEnqueueThumbnailJob`」「解放: `ReleaseThumbnailJob`」「全消去: `ClearThumbnailQueue`」の3点だった（Phase 4 で廃止済み）。
- `TryEnqueueThumbnailJob` 呼び出し点は `MainWindow.Selection.cs:32`, `MainWindow.MenuActions.cs:365`, `MainWindow.MenuActions.cs:589`, `MainWindow.Watcher.cs:74`, `MainWindow.Watcher.cs:216`, `MainWindow.xaml.cs:1275`, `MainWindow.xaml.cs:1291`, `Thumbnail/MainWindow.ThumbnailCreation.cs:173`。
- 旧 `queueThumb.Enqueue` 直呼びは `MainWindow.xaml.cs:1187`, `MainWindow.xaml.cs:1203` に存在するが、コメントアウト済みブロック内であり現行経路では未使用。
- `ThumbnailQueueProcessor` の `ConcurrentQueue<QueueObj>` 依存は、引数定義 `Thumbnail/ThumbnailQueueProcessor.cs:18`、空判定 `Thumbnail/ThumbnailQueueProcessor.cs:36`, `Thumbnail/ThumbnailQueueProcessor.cs:100`、取り出し `Thumbnail/ThumbnailQueueProcessor.cs:48`。
- キュー開始点は `MainWindow.xaml.cs:249`（`MainWindow_ContentRendered` で `CheckThumbAsync` 起動）。停止点は `MainWindow.xaml.cs:305`（`MainWindow_Closing` で `_thumbCheckCts.Cancel()`）。再起動経路は `MainWindow.Search.cs:69` -> `Thumbnail/MainWindow.ThumbnailCreation.cs:10`。
- `%LOCALAPPDATA%\\IndigoMovieManager\\QueueDb\\` の作成責務は「最終責務を `QueueDbService.EnsureInitialized` に集約（`Thumbnail/QueueDb/QueueDbService.cs:64`）」に決定。`QueueDbPathResolver.ResolveQueueDbPath` 側の `Directory.CreateDirectory`（`Thumbnail/QueueDb/QueueDbPathResolver.cs:29`）は防御的フォールバックとして維持。
- SQLite方針は「接続文字列は `Data Source={path}` 統一」「通常書き込みは `BeginTransaction` 必須（既存 `DB/SQLite.cs` と `QueueDbService.Upsert`）」「リース取得は `BEGIN IMMEDIATE TRANSACTION`（`Thumbnail/QueueDb/QueueDbService.cs:372`）」「QueueDB接続時はPRAGMA統一適用（`Thumbnail/QueueDb/QueueDbSchema.cs`）」に統一。

## 3. 実装タスクリスト（フェーズ別）

## Phase 1: QueueDBアクセス層を追加
- [x] `Thumbnail/QueueDb/QueueDbPathResolver.cs` を追加する
- [x] `Thumbnail/QueueDb/QueueDbService.cs` を追加する
- [x] `Thumbnail/QueueDb/QueueDbSchema.cs` を追加し、テーブル・インデックス・PRAGMA適用を実装する
- [x] `Pending` 取得、`Lease` 取得、`Status` 更新、`Upsert` のSQLを `QueueDbService` に集約する
- [x] `Status` 列挙体（`Pending/Processing/Done/Failed/Skipped`）を追加する

## Phase 2: Producer/Persisterを導入
- [x] `Thumbnail/QueuePipeline/QueueRequest.cs` を追加する
- [x] `Thumbnail/QueuePipeline/ThumbnailQueuePersister.cs` を追加する
- [x] `Channel<QueueRequest>` を導入し、Watcher/D&D側は `TryWrite` のみ行うように変更する
- [x] 既存 `TryEnqueueThumbnailJob` は直接 `queueThumb.Enqueue` せず、Producer経由に置換する
- [x] Persisterで100〜300msバッチのUpsertを実装する
- [x] Producerに短時間デバウンスを追加し、同一イベント連打の投入膨張を抑止する
- [x] Persisterで同一キー要求をバッチ内で圧縮し、重複Upsertを削減する

## Phase 3: ConsumerをDBリース中心へ移行
- [x] `Thumbnail/ThumbnailQueueProcessor.cs` を改修し、DBからのリース取得ループを実装する
- [x] 処理開始時に `Processing + OwnerInstanceId + LeaseUntilUtc` を必ず設定する
- [x] 成功時 `Done`、再試行時 `Pending + AttemptCount++`、致命時 `Failed/Skipped` を実装する
- [x] 長時間処理中ジョブの `LeaseUntilUtc` 延長処理を実装する
- [x] 既存 `ConcurrentQueue<QueueObj>` 依存を段階的に削除する
- [x] `Upsert` で `Status=Processing` 行を上書きせず、処理中リースを破壊しない
- [x] `preferredTabIndex` による選択タブ優先リース取得を実装する
- [x] Consumer例外時に上位監視で再起動できる構成へ変更する

## Phase 4: 起動復元・終了処理の確定
- [x] 起動時に `Pending` と期限切れ `Processing` を再処理対象として扱う
- [x] 終了時は入力停止 -> `CancellationToken` 通知 -> 最大500ms待機へ統一する
- [x] 同期 `Flush` を呼ばない実装に統一する
- [x] 旧 in-memory 専用の重複管理コードを縮退または削除する
- [x] DB切替時は旧DBキューを保持し、Consumerは現在開いているDBのみ処理する
- [x] タブ切替時は既存DBキューを保持し、選択タブ優先で消化する

## Phase 5: 仕上げ（ログ・運用）
- [x] キュー投入数、Upsert投入数、DB実反映数（新規/更新/Processing保護スキップ）、リース取得数、失敗数をログ出力する
- [x] サムネイル作成中ダイアログはシングルトン表示とし、キューがある間は開いたまま維持する
- [x] 手動再試行（`Failed -> Pending`）の運用手順をドキュメント化する
- [x] 例外時にアプリ全体を止めない境界（Persister/Consumer）を確認する

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
- [ ] ユーザー選択中タブのジョブが他タブより優先して消化される
- [ ] 同一イベント連打時に `Channel` 滞留と重複Upsertが増え続けない
- [ ] `upsert_submitted` と `db_affected / db_inserted / db_updated` の差分から、実DB反映状況を判別できる
- [ ] サムネイル作成中ダイアログがバッチ境界で開閉せず、キュー枯渇時のみ閉じる

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

## 8. 実施記録（2026-02-24）

### 8.1 Phase 1 実装済み
- `Thumbnail/QueueDb/QueueDbPathResolver.cs` を追加
  - `%LOCALAPPDATA%\IndigoMovieManager\QueueDb\` への保存先解決
  - `MainDbPathHash8` / `MoviePathKey` 生成ロジックを実装
- `Thumbnail/QueueDb/QueueDbSchema.cs` を追加
  - `ThumbnailQueue` テーブル、インデックス、PRAGMA適用を実装
- `Thumbnail/QueueDb/QueueDbService.cs` を追加
  - `Upsert` / `GetPendingAndLease` / `UpdateStatus` / `ExtendLease` / `ResetFailedToPending` を実装
  - `ThumbnailQueueStatus` 列挙体を追加

### 8.2 Phase 2 実装済み
- `Thumbnail/QueuePipeline/QueueRequest.cs` を追加
  - `QueueObj` から永続化要求へ変換する `FromQueueObj` を実装
- `Thumbnail/QueuePipeline/ThumbnailQueuePersister.cs` を追加
  - `Channel<QueueRequest>` を100〜300ms窓でバッチ処理し、QueueDBへ `Upsert` する単一ライターを実装
- `MainWindow.xaml.cs` を更新
  - `Channel<QueueRequest>` を導入
  - Persister起動（`MainWindow_ContentRendered`）と停止通知（`MainWindow_Closing`）を追加
- `Thumbnail/MainWindow.ThumbnailQueue.cs` を更新
  - `TryEnqueueThumbnailJob` を Producer 経由に変更
  - `TryWriteQueueRequest` を追加し、Watcher/D&Dからの経路を `TryWrite` 中心へ移行

### 8.3 レビュー反映済み（Phase 1/2補強）
- `QueueDbService.UpdateStatus` を「所有者一致時のみ更新」へ変更（更新件数を返却）
- `QueueDbService.ResetFailedToPending` に `AttemptCount = 0` リセットを追加
- Persister異常時の監視ループ再起動を追加（`RunThumbnailQueuePersisterSupervisorAsync`）
- 終了時に Persister 停止を最大500ms待機する処理を追加
- `DBFullPath` 未設定時の投入は失敗扱いへ変更（永続化不能時の投入を拒否）

### 8.4 補助ドキュメント
- `Thumbnail/Test/修正リスト.md` を追加
  - テストコード追従（旧API -> 新API）とビルド構成整理のタスクを記録

### 8.5 ビルド確認
- `MSBuild.exe`（Debug / x64）でビルド成功を確認

### 8.6 テスト追従進捗（`Thumbnail/Test/修正リスト.md` 同期）
- 同期元: `Thumbnail/Test/修正リスト.md`
- 集計: 未着手 9件 / 対応中 0件 / 完了 0件

| ID | 状態 | 対象 | 修正内容 |
|---|---|---|---|
| T-001 | 未着手 | `QueueDbPathResolverTests.cs` | `QueueDbPathResolver.ResolvePath` を `ResolveQueueDbPath` へ置換し、期待値を現行実装の命名規則に合わせる。 |
| T-002 | 未着手 | `QueueDbSchemaTests.cs` | `QueueDbSchema.EnsureSchemaCreated(connectionString)` 依存を廃止し、`SQLiteConnection` を開いて `QueueDbSchema.EnsureCreated(connection)` 呼び出しへ変更する。 |
| T-003 | 未着手 | `QueueDbServiceTests.cs` | 旧API `UpsertBatch` を現行API `Upsert(IEnumerable<QueueDbUpsertItem>, DateTime utcNow)` に置換する。 |
| T-004 | 未着手 | `QueueDbServiceTests.cs`, `ThumbnailQueueProcessorTests.cs`, `TestMockServices.cs` | `UpdateStatus(long queueId, int status, string errorMsg)` を現行署名 `UpdateStatus(long queueId, string ownerInstanceId, ThumbnailQueueStatus status, DateTime utcNow, ...)` に追従する。 |
| T-005 | 未着手 | `QueueDbServiceTests.cs` | `GetPendingAndLease(instanceId, count, leaseMinutes)` を `GetPendingAndLease(instanceId, takeCount, TimeSpan leaseDuration, DateTime utcNow)` に置換する。 |
| T-006 | 未着手 | `QueueDbServiceTests.cs` | `ResetFailedToPending` の `AttemptCount = 0` リセット仕様を検証するテストケースを追加する。 |
| T-007 | 未着手 | `ThumbnailQueuePersisterTests.cs`, `ThumbnailQueueProcessorTests.cs` | テスト内のダミー `QueueDbService` / `QueueDbSchema` 依存を整理し、本番 `IndigoMovieManager.Thumbnail.QueueDb` 名前空間の型を直接参照する。 |
| T-008 | 未着手 | `Thumbnail/Test` 全体 | 旧設計前提の仮実装（`TestMockServices.cs`）を残すか廃止するか方針決定し、重複定義を解消する。 |
| T-009 | 未着手 | ソリューション構成 | `NUnit` / `Moq` 未参照で本体ビルドが失敗するため、テストを別プロジェクト化するか、本体 `.csproj` から `Thumbnail/Test/**/*.cs` を除外する。 |

### 8.7 テスト関連の追加メモ（`修正リスト.md` 反映）
- ビルドエラー対応（未着手）
  - `Thumbnail/QueueDb/QueueDbService.cs` の `Path` / `Directory` 解決エラー
  - `Thumbnail/QueueDb/QueueDbPathResolver.cs` の `Path` / `Directory` 解決エラー
- コンパイラ警告対応（未着手）
  - `Thumbnail/QueueDb/QueueDbService.cs` の `CS8632` 対応（Nullable文脈の整合）
- テスト統合タスク（未着手）
  - `NUnitMocks.cs` / `TestMockServices.cs` の仮実装整理と本番クラス参照への置換

### 8.8 Phase 3 実装済み（Consumer DBリース化）
- `Thumbnail/ThumbnailQueueProcessor.cs` を更新
  - `GetPendingAndLease` ベースのDBリース取得ループへ移行
  - 成功時 `Done`、失敗時 `Pending/Failed` の状態遷移を `UpdateStatus` で統一
  - 長時間処理向けに `ExtendLease` ハートビート（30秒間隔）を追加
- `Thumbnail/MainWindow.ThumbnailCreation.cs` を更新
  - `CheckThumbAsync` を新 `RunAsync` 署名（`QueueDbService Resolver + ownerInstanceId`）へ追従
  - `CreateThumbAsync` に `MoviePath` からの `MovieId` 補完を追加（QueueDB由来ジョブ対応）
- `Thumbnail/MainWindow.ThumbnailQueue.cs` を更新
  - `ResolveCurrentQueueDbService` を追加し、現在DBに追従する `QueueDbService` を解決
  - Consumer所有者ID（`thumbnailQueueOwnerInstanceId`）を追加
  - `ClearThumbnailQueue` から runtime queue 依存を削除し、DB中心管理へ寄せた

### 8.9 Phase 3 追加反映（優先制御・安定化）
- 仕様整理（2026-02-24）
  - タブ切替時は既存DBキューを保持する（破棄しない）
  - DB切替時も既存DBキューは保持し、Consumerは「現在開いているDB」のみ処理する
  - ユーザーが選択中のタブを優先してキュー消化する
- `Thumbnail/QueueDb/QueueDbService.cs` を更新
  - `Upsert` の `ON CONFLICT` で `Status=Processing` 行を上書きしない条件を追加し、処理中リース破壊を防止
  - `GetPendingAndLease` に `preferredTabIndex` 引数を追加
  - リース取得順を `CASE WHEN TabIndex=@PreferredTab THEN 0 ELSE 1 END, CreatedAtUtc` へ変更
- `Thumbnail/ThumbnailQueueProcessor.cs` を更新
  - `RunAsync` に `preferredTabIndexResolver` を追加し、選択タブ優先リースを有効化
  - ハートビート延長処理でキャンセル後スピンしないよう制御を追加
  - `RunAsync` の例外を再送出して上位監視で再起動可能な構成へ変更
- `Thumbnail/MainWindow.ThumbnailCreation.cs` を更新
  - `CheckThumbAsync` をConsumer監視ループ化し、異常終了時に500ms待機で再起動
  - `RunAsync` 呼び出しに `preferredTabIndexResolver` を接続
- `Thumbnail/MainWindow.ThumbnailQueue.cs` を更新
  - `ResolvePreferredThumbnailTabIndex` を追加し、現在タブをConsumerへ供給

### 8.10 Doc同期（2026-02-24）
- Phase 3 チェックリストに以下を追記し、実装済みへ更新
  - `Upsert` の `Processing` 行保護
  - `preferredTabIndex` による選択タブ優先リース
  - Consumer例外時の上位監視再起動
- Phase 4 チェックリストに以下を追記し、実装済みへ更新
  - DB切替時は旧DBキュー保持 + 現在DBのみ処理
  - タブ切替時は既存DBキュー保持 + 選択タブ優先消化
- DoD に「選択中タブ優先消化」の確認項目を追加

### 8.11 Phase 4 実装反映（終了シーケンス統一）
- `MainWindow.xaml.cs` を更新
  - `MainWindow_ContentRendered` 開始時にキュー入力を有効化
  - `MainWindow_Closing` で入力停止（`SetThumbnailQueueInputEnabled(false)`）を先行実行
  - 入力停止後に `CancellationToken` 通知し、`_thumbCheckTask` / `_thumbnailQueuePersisterTask` を最大500ms待機へ統一
  - 500ms待機ロジックを `WaitBackgroundTaskForShutdown` に集約
- `Thumbnail/MainWindow.ThumbnailQueue.cs` を更新
  - `isThumbnailQueueInputEnabled` を追加し、終了中の新規投入を抑止
  - `SetThumbnailQueueInputEnabled` を追加
  - `ProcessDeferredLargeCopyJobsAsync` でも入力停止時は再投入しない
- 起動復元ポリシー
  - `GetPendingAndLease` の対象は `Pending` と期限切れ `Processing` を含むため、起動後Consumerで再処理される
- 同期 Flush 方針
  - 終了時に同期Flushは行わず、入力停止 + キャンセル + 最大500ms待機のみで統一

### 8.12 Phase 4 追加反映（in-memory重複管理の縮退）
- `Thumbnail/MainWindow.ThumbnailQueue.cs` を更新
  - `queuedThumbnailKeys` を削除し、重複抑止をQueueDBの一意制約へ委譲
  - `TryEnqueueThumbnailJob` は入力可否判定 + `Channel<QueueRequest>.TryWrite` のみに簡素化
  - `ClearThumbnailQueue` は後回し大容量コピー管理（`deferredLargeCopyJobs`）のみ初期化する形へ整理
- `Thumbnail/MainWindow.ThumbnailCreation.cs` を更新
  - `CreateThumbAsync` から旧重複キー解放経路（`ReleaseThumbnailJob` / `releaseQueueKey`）を削除
  - Consumer完了時の後処理はUI反映とログ終了に限定

### 8.13 イベント重複膨張対策（Producer + Persister）
- `Thumbnail/MainWindow.ThumbnailQueue.cs` を更新
  - `ThumbnailQueueDebounceWindowMs = 800` を追加し、同一キーの短時間連打を抑止
  - `recentEnqueueByKeyUtc` を導入し、Watcher由来の重複イベントを `TryEnqueueThumbnailJob` で吸収
  - 手動キャプチャ系（`ThumbPanelPos` / `ThumbTimePos` あり）はデバウンス対象外として維持
  - `ClearThumbnailQueue` でデバウンス状態も初期化
- `Thumbnail/QueuePipeline/ThumbnailQueuePersister.cs` を更新
  - `PersistBatch` で同一キー（`MoviePathKey + TabIndex`）を最新1件へ圧縮
  - 重複圧縮の可視化としてログを `batch_count / unique / deduped / upsert_submitted` へ拡張

### 8.14 Phase 5 実装済み（ログ・運用）
- `Thumbnail/QueuePipeline/ThumbnailQueueMetrics.cs` を追加
  - `enqueue / upsert_submitted / db_affected / db_inserted / db_updated / db_skipped_processing / lease / failed` の累計を `Interlocked` で集計
  - `CreateSummary` で監視ログ向けサマリ文字列を提供
- `Thumbnail/MainWindow.ThumbnailQueue.cs` を更新
  - `TryEnqueueThumbnailJob` 成功時に投入累計を記録・ログ出力
  - `ResetFailedThumbnailJobsForCurrentDb` を追加し、手動再試行（`Failed -> Pending`）の実行入口を明確化
- `Thumbnail/QueuePipeline/ThumbnailQueuePersister.cs` を更新
  - `PersistBatch` 成功時にUpsert投入累計（`upsert_submitted_total`）に加え、実反映累計（`db_affected_total / db_inserted_total / db_updated_total / db_skipped_processing_total`）をログ出力
- `Thumbnail/ThumbnailQueueProcessor.cs` を更新
  - `GetPendingAndLease` 後にリース取得累計をログ出力
  - 失敗遷移時に失敗累計を記録し、`failed_total` をログ出力
  - バッチサマリ（`thumb queue summary`）へメトリクス要約を連結
  - 進捗ダイアログをバッチ単位の開閉からセッション単位へ変更し、キュー存続中はシングルトン表示を維持
  - `GetActiveQueueCount(ownerInstanceId)` 判定で、キュー枯渇時のみダイアログを閉じる
- 運用ドキュメントを追加
  - `Thumbnail/手動再試行運用手順.md` に `Failed -> Pending` の実行手順を記載
- 境界確認
  - Persisterは `RunThumbnailQueuePersisterSupervisorAsync` で例外時に再起動継続
  - Consumerは `CheckThumbAsync` で例外時に再起動継続
  - いずれも例外でアプリ全体を停止しない境界として運用可能
