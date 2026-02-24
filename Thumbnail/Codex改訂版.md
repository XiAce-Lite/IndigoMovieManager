# サムネイルキュー専用DBおよび非同期処理アーキテクチャ設計（改訂版）

## 1. 前提と決定事項
- アプリの同時起動（複数プロセス）は許容する。
- 監視イベントの永続キーは `MovieId` 維持を必須としない。
- 終了ポリシーは「即終了優先」とする（終了時の同期 Flush は行わない）。
- 既存メインDB（`*.bw`）のスキーマは変更しない。

## 2. 目的
- サムネイル生成キューを永続化し、再起動後に再開できるようにする。
- 監視イベント処理をUIスレッドから分離し、非同期で処理する。
- 複数プロセス同時稼働でも、同一ジョブの重複実行を防ぐ。

## 3. キューDB配置
書き込み権限と衝突回避のため、キューDBは `exe` 直下ではなくユーザー領域へ作成する。

- ルート: `%LOCALAPPDATA%\IndigoMovieManager\QueueDb\`
- ファイル名: `{MainDbName}.{MainDbPathHash8}.queue.db`
  - `MainDbPathHash8` はメインDBフルパス正規化文字列の SHA-256 先頭8文字

例:
- メインDB: `D:\Movies\Anime2026.bw`
- キューDB: `%LOCALAPPDATA%\IndigoMovieManager\QueueDb\Anime2026.A1B2C3D4.queue.db`

## 4. DBスキーマ（SQLite）
`MovieId` に依存せず、`MainDbPathHash + MoviePathKey + TabIndex` を永続キーにする。

```sql
CREATE TABLE IF NOT EXISTS ThumbnailQueue (
    QueueId INTEGER PRIMARY KEY AUTOINCREMENT,
    MainDbPathHash TEXT NOT NULL,
    MoviePath TEXT NOT NULL,
    MoviePathKey TEXT NOT NULL,           -- 正規化+小文字化した比較用キー
    TabIndex INTEGER NOT NULL,
    ThumbPanelPos INTEGER,
    ThumbTimePos INTEGER,
    Status INTEGER NOT NULL DEFAULT 0,    -- 0:Pending 1:Processing 2:Done 3:Failed 4:Skipped
    AttemptCount INTEGER NOT NULL DEFAULT 0,
    LastError TEXT NOT NULL DEFAULT '',
    OwnerInstanceId TEXT NOT NULL DEFAULT '',
    LeaseUntilUtc TEXT NOT NULL DEFAULT '',
    CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UpdatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UNIQUE (MainDbPathHash, MoviePathKey, TabIndex)
);

CREATE INDEX IF NOT EXISTS IX_ThumbnailQueue_Status_Lease
ON ThumbnailQueue (Status, LeaseUntilUtc, CreatedAtUtc);

CREATE INDEX IF NOT EXISTS IX_ThumbnailQueue_MainDb
ON ThumbnailQueue (MainDbPathHash, Status, CreatedAtUtc);
```

### 4.1 SQLite動作設定（推奨）
- `PRAGMA journal_mode=WAL;`
- `PRAGMA synchronous=NORMAL;`
- `PRAGMA busy_timeout=5000;`

## 5. 全体フロー（完全非同期）

### 5.1 Producer（Watcher / D&D）
- `FileSystemWatcher` ハンドラでは重い処理を行わず、`Channel<QueueRequest>` へ `TryWrite` するだけにする。
- UIスレッドで DB 書き込み・画像処理を実行しない。

### 5.2 Persister（単一ライター）
- 専用バックグラウンドタスクで `Channel` から要求を受け取り、短周期バッチで `INSERT ... ON CONFLICT DO UPDATE` 相当を実行する。
- 追加要求は `Status=Pending` へ統一し、完了済みジョブの再投入時だけ `UpdatedAtUtc` を更新する。

### 5.3 Consumer（ThumbWorker）
- DBから `Pending`（またはリース期限切れ `Processing`）を取得し、リース獲得後に生成処理を行う。
- 処理結果で `Status` を更新する:
  - 成功: `Done`
  - 再試行可能エラー: `Pending`（`AttemptCount++`）
  - 復旧不能: `Failed` or `Skipped`

## 6. 複数プロセス対応（リース方式）
全プロセスは固有 `InstanceId`（GUID）を持ち、リースで排他する。

1. `BEGIN IMMEDIATE TRANSACTION`
2. 取得対象を選択（`Pending` / 期限切れ `Processing`）
3. 対象行を `Processing + OwnerInstanceId + LeaseUntilUtc` に更新
4. `COMMIT`

補足:
- 生成が長引くジョブは定期的に `LeaseUntilUtc` を延長する。
- プロセスクラッシュ時はリース期限切れで他プロセスが再取得できる。

## 7. シャットダウン方針（即終了優先）
- 終了時は新規入力受付を停止し、`CancellationToken` を即時キャンセルする。
- 同期 `Flush()` は実施しない。
- Persister/Worker は `Task.WhenAny(task, Task.Delay(短時間))` で待機上限を設け、上限超過時は待たずに終了する。
- 未反映のインメモリ要求は失う可能性があるため、Persister のバッチ間隔は短く保つ（例: 100〜300ms）。

## 8. エラー処理と再試行
- `AttemptCount` が閾値（例: 5）を超えたら `Failed` へ遷移。
- `LastError` には最後の例外要約を保存する。
- `Failed` は手動再試行（状態を `Pending` に戻す）可能にする。

## 9. 移行ステップ
1. キューDBアクセス層（`QueueDbService`）を追加
2. Producer を `Channel` 化（Watcherから直接DB呼び出しを除去）
3. Persisterタスクを導入し、追加要求の永続化を一本化
4. Consumer を「`queueThumb` 中心」から「DBリース取得中心」へ移行
5. 旧 in-memory 重複キー管理は段階的に縮退
6. 起動時リカバリを DB の `Pending/Processing(期限切れ)` 読み込みへ置換

## 10. 差分提案（旧案 → 改訂案）
1. 保存先
- 旧: `exe` 直下
- 新: `%LOCALAPPDATA%\IndigoMovieManager\QueueDb\`
- 理由: 書き込み権限問題を回避し、実運用で失敗しにくくする

2. 永続キー
- 旧: `PRIMARY KEY (MovieId, TabIndex)`
- 新: `UNIQUE (MainDbPathHash, MoviePathKey, TabIndex)`
- 理由: `MovieId` 未確定イベントでも登録可能にし、要件に一致させる

3. 複数プロセス制御
- 旧: 明示的な排他なし
- 新: `OwnerInstanceId + LeaseUntilUtc` のリース方式
- 理由: 同時起動時の重複処理と取りこぼしを防ぐ

4. 終了処理
- 旧: 終了時に同期 `Flush()` 実行
- 新: 即終了優先（短時間待機のみ、同期Flushなし）
- 理由: UI終了の体感遅延を避ける

5. 処理順序
- 旧: in-memory キュー中心、DBは追従
- 新: DBを正として Producer/Persister/Consumer を非同期分離
- 理由: 再起動復元と複数プロセス整合性を優先

## 11. 検証項目
1. 複数プロセス同時起動で同一ジョブが二重生成されない
2. 強制終了後に `Pending` が再実行される
3. 1000件投入時にUI操作が詰まらない
4. 終了操作で即時にウィンドウが閉じる（長時間待ちしない）
