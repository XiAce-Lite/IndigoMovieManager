# サムネイルキュー専用DBおよび非同期処理アーキテクチャ設計（GAMENI改訂版）

## 1. 目的と前提要件
- **最大目的（大量追加時の機能不全解消）**: 監視フォルダやD&Dによって数千件規模の動画が一括で追加された際、従来発生していた「UIのスレッドブロック（フリーズ）」「同期処理の待ち時間によるOSからのイベント取りこぼし」「キュー溢れによるサムネイル生成の停止」等の機能不全を根絶する。
- **サムネイル生成キューの保護**: アプリの強制終了や再起動時に、未処理のサムネイルキューを失わず自動再開できるようにする。
- **既存の保護**: メインデータベース（`*.bw`）のスキーマ・構造は一切変更しない。
- **アプリの同時起動を許容**: 複数プロセスが同じメインDBに向けたキューを発行・処理しても競合や二重実行が起きないように排他制御を行う。
- **即終了優先（Instant Exit）**: アプリ終了時に長時間のI/O（同期的なFlush処理など）を行わず、即座にウィンドウを閉じるための仕組みとする。
- **キー構造の見直し**: `MovieId` は監視イベント検知（Producer）の時点では未確定なため、ファイルのフルパス等を永続キーとする。

## 2. ディレクトリとファイル構成
キューDBは、一般ユーザー権限でも確実に書き込みが保証され、かつ同じファイル名の本DBでも混線しない場所へ保存する。

- **保存ディレクトリ**: `%LOCALAPPDATA%\IndigoMovieManager\QueueDb\`
- **ファイル命名規則**: `{MainDbName}.{MainDbPathHash8}.queue.db`
  - ※ `MainDbName` は拡張子を除いたファイル名。 
  - ※ `MainDbPathHash8` はメインDBフルパスを小文字化・正規化した文字列の SHA-256 ハッシュ先頭8文字。

**例:**
- メインDB: `D:\Movies\Anime2026.bw`
- キューDB: `%LOCALAPPDATA%\IndigoMovieManager\QueueDb\Anime2026.A1B2C3D4.queue.db`

## 3. データベース設計 (SQLite)
`MovieId` に依存しない、ファイルパスベースのユニーク制約と、複数プロセス制御のためのリース情報と状態管理を持つテーブルを使用。

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

### 3.1 SQLite動作設定（推奨）
高頻度な非同期書き込みに耐えるための事前設定:
- `PRAGMA journal_mode=WAL;`
- `PRAGMA synchronous=NORMAL;`
- `PRAGMA busy_timeout=5000;`

## 4. 全体アーキテクチャ（完全非同期フロー）

プロセスは「① Watcher」「② QueueDB」「③ ThumbWorker」の3ループで構成され、すべて独立稼働する。

### ① 生産者 (Producer): `Watcher`
- **役割**: `FileSystemWatcher` による検知や、ユーザーのD&D操作からの入力を受け取る。
- **動作**: 
  - **No SQL/No DB**: ハンドラ内で重いDBアクセスは行なわず、インメモリの `Channel<QueueRequest>` へ対象を `TryWrite` するのみで即リターンする（＝イベントの取りこぼしとUIフリーズの完全除去）。

### ② 永続化 (Persister): `QueueDB` (DBライタータスク)
- **役割**: Producerが `Channel` に貯めたリクエストを、極めて短い間隔（例: 100〜300ms毎）のバックグラウンドループでSQLiteに流し込む。
- **動作**:
  - `INSERT INTO ... ON CONFLICT (MainDbPathHash, MoviePathKey, TabIndex) DO UPDATE SET Status = 0, UpdatedAtUtc = ...` のような Upsert 処理により新規追加（および再試行等による保留状態への復帰）を行う。
  - キュー完了ジョブも `DELETE` を行わず `Status = 2 (Done)` に更新するのみとする。これにより、従来懸念されていた「メモリへの書き戻し時の DELETE → INSERT 順序の競合により完了済みジョブが復活する問題」を原理的に解決する。

### ③ 消費者 (Consumer): `ThumbWorker` (サムネイル生成処理)
- **役割**: QueueDBから「処理待ち」状態のキューを継続的にポーリングし、サムネイルを生成する。
- **動作**: 
  - インメモリ構造ではなく、「DBからのリース（貸出）獲得」をベースに処理を進行。

## 5. 複数プロセス対応（リース方式の排他制御）
同一キューDBに対し、複数プロセスから生成が走った場合の重複実行を防ぐため、GUIDなどで定義した『このプロセスのID（`OwnerInstanceId`）』を用いてレコードをロックする。

**取得アルゴリズム（ポーリング時）:**
1. `BEGIN IMMEDIATE TRANSACTION;` を実行
2. `Status = 0 (Pending)` または `Status = 1 (Processing)` でかつ `LeaseUntilUtc < SQLiteの現在時刻` の行を検索。
3. 古い順にN件抽出し、対象行の `Status = 1`, `OwnerInstanceId = 自身のGUID`, `LeaseUntilUtc = 現在時刻 + N分` に UPDATE。
4. `COMMIT;`
5. 成功した行をご自身のプロセスで生成実行する。（※完了まで時間がかかる大容量動画などは、定期的にDB上の `LeaseUntilUtc` を延長する）

## 6. エラーハンドリングと再試行
- サムネイル生成中にエラーが発生した場合、`AttemptCount` を +1 し、限界値（例: 5回）未満なら `Status = 0 (Pending)` に戻してリースを放棄、次回試行へまわす。
- 限界を超えた場合や致命的エラー（ファイル不在等）は `Status = 3 (Failed)` とし、`LastError` にスタックトレース等の要約を保存する。失敗しても再試行ボタン等から `Pending` へ戻せる設計とする。

## 7. シャットダウン方針（即終了優先）
- アプリの終了（×ボタンなど）が指示された場合、インメモリの入力受付（①Watcher）を即座に停止する。
- 実行中ループは `CancellationTokenSource.Cancel()` で停止。
- 同期的な `Flush()` は一切行わず、「`Task.WhenAny(task, Task.Delay(例: 500ms))`」を用いた短時間の猶予のみで、超過時は待たずにプロセスをキルしてウィンドウを閉じる。
- ※ Persisterの書込周期が短いため漏れは最小限であり、生成中のジョブが中途半端に終わっても `LeaseUntilUtc` 切れとして後日再実行されるため問題ない。
