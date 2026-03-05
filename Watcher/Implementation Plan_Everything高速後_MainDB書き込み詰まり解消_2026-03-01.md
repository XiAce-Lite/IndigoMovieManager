# Implementation Plan（Everything高速後のMainDB書き込み詰まり解消）

## 1. 結論（先に要点）
- 詰まりの主因は「Everythingの列挙速度」ではなく、`CheckFolderAsync` 内の新規候補1件ごとの直列処理。
- とくに `DB存在確認SELECT` と `InsertMovieTable` の単発実行が積み上がって、MainDB反映完了まで待ちが発生している。
- 解消は「1件ずつ処理」から「事前キャッシュ + バッチ書き込み + 主DB待ちの分離」へ寄せるのが最短。

## 2. コード根拠（ボトルネック特定）

### 2.1 新規候補ループが実質直列
- 対象: `Watcher/MainWindow.Watcher.cs`
- 根拠:
  - `foreach (string movieFullPath in scanResult.NewMoviePaths)` で1件ずつ処理（494行付近）
  - 各件で `GetData(... where movie_path = '...' limit 1)` を実行（528-531行）
  - 未登録時は `MovieInfo` 生成 -> `InsertMovieTable` を待ってから次へ（542, 548-550行）
- 影響:
  - Everythingが候補を即返しても、後段が逐次待ちで詰まる。

### 2.2 DB存在確認が毎件SQL往復
- 対象: `Watcher/MainWindow.Watcher.cs`
- 根拠:
  - 1件ごとに `select movie_id, hash, title from movie where movie_path = ...`（530行）
- 影響:
  - 新規件数が増えるほどSQL往復が線形増加。
  - 文字列組み立てSQLで毎回パースが走る。

### 2.3 InsertMovieTableが単発トランザクション前提
- 対象: `DB/SQLite.cs`
- 根拠:
  - 毎回 `connection.Open()`（481-482行）
  - 毎回 `select max(movie_id) from movie`（485行）
  - 毎回 `BeginTransaction()` -> `insert` -> `Commit()`（519-575行）
- 影響:
  - 1件1コミットでI/O待ちが増える。
  - `max(movie_id)` 取得が件数ぶん発生する。

### 2.4 Insert前にSinku取得を毎件実施
- 対象: `DB/SQLite.cs`
- 根拠:
  - `TryReadBySinkuDll(movie.MoviePath...)` を毎回実行（506行）
  - `TryReadBySinkuDll` 内で `NativeLibrary.Load(...)` -> `Free(...)`（613行, 696行付近）
- 影響:
  - DB書き込み区間の前で重いネイティブ処理が直列に積まれる。

### 2.5 主DB完了後でないとキュー投入情報が確定しにくい構造
- 対象: `Watcher/MainWindow.Watcher.cs`
- 根拠:
  - `MovieId` をDB登録後に確定してから `QueueObj` を組み立て（555行, 617行）
- 影響:
  - サムネイル処理開始がMainDB書き込み完了に引きずられる。
  - 体感として「Everythingは速いのに待つ」になる。

## 3. 改善方針（互換重視）
- WhiteBrowser互換DBを壊す変更は避け、まずコードフロー改善で詰まりを解消する。
- 優先順位は以下:
1. 毎件SELECTの削減（事前キャッシュ化）
2. 毎件INSERTの削減（バッチ化）
3. 主DB完了待ちの分離（Queue先行）

## 4. 実装プラン

### Phase 1（最優先・低リスク）
1. `movie_path -> (movie_id, hash)` の辞書をスキャン開始時に1回だけ構築する。
- 例: `select movie_id, movie_path, hash from movie`
- ループ内の `GetData(... where movie_path=...)` を辞書参照へ置換する。

2. `InsertMovieTable` の `select max(movie_id)` を廃止する。
- `movie_id` はSQLiteの `INTEGER PRIMARY KEY` 自動採番を使う。
- INSERT後に `last_insert_rowid()` で `movie.MovieId` を確定する。

3. `InsertMovieTable` のSQLをパラメータ化済みの再利用コマンドへ寄せる準備をする。
- 現段階では単発でもよいが、Phase 2のバッチ化へ接続しやすい形にする。

### Phase 2（本命）
1. `InsertMovieTableBatch(List<MovieCore>)` を追加する。
- 1接続・1トランザクションでN件（例: 100件）をまとめてINSERT。
- 1件ごとの`Open/Commit`を削減する。

2. `CheckFolderAsync` は「発見」と「DB反映」を分離する。
- ループ中は `newMovies` リストに積む。
- 一定件数ごとに `InsertMovieTableBatch` を実行する。

3. `Sinku` メタ取得を同期必須から外す。
- 初回登録は `container/video/audio/extra` を空で登録可にし、必要時に後追い更新。
- もしくは、`Sinku` 読み取りを別ワーカーに分離してメイン挿入経路から外す。

### Phase 3（体感改善の決め手）
1. Queue投入を `MoviePath` 主体で先行させる。
- `QueueObj.MovieId` が未確定でも投入可とする（現在も補完ロジックあり）。
- `MainWindow.ThumbnailCreation.cs` の `ResolveMovieIdByPathAsync` を前提に整合を保つ。

2. UI反映はフォルダ単位・バッチ単位で実施する。
- 大量追加時に1件ごとのUI同期を避ける。
- 最終 `FilterAndSort` のみで一覧更新するモードを明確化する。

## 5. 計測計画（改善確認）
- 既存ログに加えて次を追加:
  - `db_exists_check_ms_total`
  - `db_batch_insert_ms_total`
  - `db_commit_count`
  - `sinku_read_ms_total`
  - `first_enqueue_until_first_thumbnail_start_ms`
- 比較条件:
  - 同一フォルダ・同一件数で改善前後を比較
  - 指標は `elapsed_ms`, `db_insert_ms`, 「初回サムネ開始までの時間」

## 6. 完了条件
1. 新規1000件以上で、`CheckFolderAsync` 全体時間が現状比で有意に短縮している。
2. MainDB反映完了前でも、サムネイル処理が先行して開始できる。
3. DB互換（既存`.wb`/運用DB）を壊すスキーマ変更が入っていない。
4. 重複登録・サムネ取りこぼしが増えていない。

## 7. 実装順（推奨）
1. Phase 1-1（辞書キャッシュ）
2. Phase 1-2（max(movie_id)廃止）
3. Phase 2-1（バッチINSERT）
4. Phase 3-1（Queue先行）
5. 計測と回帰確認

この順なら、リスクを抑えつつ詰まりの主因から順に消せる。

## 8. タスクリスト（順次実装）
- [x] T1: `CheckFolderAsync` 起点で `movie` テーブルの既存情報を辞書化する（毎件SELECT廃止）
  - 実装: `BuildExistingMovieSnapshotByPath` 追加、ループ内存在確認を辞書参照へ変更
- [x] T2: `InsertMovieTable` の `select max(movie_id)` 採番を廃止し、自動採番 + `last_insert_rowid()` 取得へ変更
  - 実装: `movie_id` 明示INSERTを削除し、INSERT後に `MovieId` を反映
- [x] T3: `InsertMovieTableBatch(List<MovieCore>)` を追加する
  - 実装: 1接続・1トランザクションで複数件INSERT、各要素へ採番IDを反映
- [x] T4: `CheckFolderAsync` の新規登録フローをバッチ書き込み対応に変更する
  - 実装: `pendingNewMovies` バッファ + `FlushPendingNewMoviesAsync` を導入
- [x] T5: 計測ログを拡張し、DB存在確認コストを可視化する
  - 実装: `db_lookup_ms` を `scan end` ログへ追加

## 9. 実装メモ（2026-03-01）
- 今回は「確認不要」の指定に合わせ、タスク作成と実装を連続で実施した。
- 互換性優先のため、DBスキーマ変更は入れていない。
- `Sinku` の後追い非同期化（Phase 2-3）は未着手。次段で着手する。

## 10. 次フェーズ計画（動的並列制御 + autogen再試行）

### 10.1 狙い
- PC性能差が大きい環境でも、サムネイル作成を安定動作させる。
- 高並列時に発生する `autogen` 一時失敗で即 `ffmpeg1pass` へ落ちる頻度を減らす。
- 処理量が多いときは性能を確保し、負荷が高いときは自動で安全側へ倒す。

### 10.2 方針
1. 全体並列を「固定値」から「実行中に再評価」へ変更する。
- バッチ開始ごとに現在の並列目標値を取得し、`MaxDegreeOfParallelism` へ反映する。
- 実行中ジョブはそのまま継続し、次バッチから新しい並列数を適用する。

2. 一時エラー時は `autogen` を1回だけリトライする。
- 対象: `GDI+`, `No frames decoded` などの一時失敗。
- 非対象: `Decoder not found`, `Invalid data found`, `Video stream not found` などの恒久失敗。

3. 動的制御ルールを導入する。
- 下げ条件: 一時エラー率や連続一時失敗が閾値超過。
- 戻し条件: 一定時間の安定継続 + キュー滞留がある場合のみ段階的に戻す。
- ヒステリシスを入れて、上げ下げの振動を防ぐ。

### 10.3 設定値（初期案）
- 並列下限: `4`
- 並列上限: `24`（現行上限維持）
- 初期並列: `Properties.Settings.Default.ThumbnailParallelism`（1〜24でクランプ）
- 下げ判定窓: `30秒`
- 戻し判定窓: `30秒 x 2回連続`
- 戻しクールダウン: `60秒`
- 再上げ禁止時間（下げ直後）: `90秒`
- autogen再試行: `最大4回`, `バックオフ200〜500ms`

### 10.4 タスクリスト（次段）
- [x] T6: `ThumbnailQueueProcessor` に動的並列リゾルバを導入する
  - `RunAsync` が固定値ではなく、バッチ単位で並列数を再評価する。
- [x] T7: 動的並列制御クラス（例: `ThumbnailParallelController`）を追加する
  - 一時エラー率、連続失敗、キュー滞留を入力にして「次バッチ並列数」を返す。
- [x] T8: `autogen` 一時失敗時の多回リトライを実装する
  - `ThumbnailCreationService` 内で `autogen` のみ再試行し、失敗時は既存フォールバックへ流す（既定4回）。
- [x] T9: エラー分類を実装する
  - 一時失敗と恒久失敗を文字列判定で分類し、制御ロジックに入力する。
- [x] T10: 計測ログを追加する
  - `parallel_current`, `parallel_target`, `transient_error_rate`, `autogen_retry_count`, `fallback_to_1pass_count` を出力する。
- [ ] T11: 設定導線を追加する
  - 動的制御ON/OFF、下限、戻し間隔、autogen再試行ON/OFFを設定可能にする。
- [ ] T12: 回帰確認を行う
  - 低スペック想定・高スペック想定の両条件で、処理速度と失敗率のバランスを比較する。

### 10.6 実装メモ（2026-03-01 追加）
- `RunAsync` に `maxParallelismResolver` を追加し、バッチ開始時に最新並列設定を読み直すようにした。
- `ThumbnailParallelController` を新規追加し、失敗傾向でスケールダウン、安定+滞留でスケールアップする制御を実装した。
- `ThumbnailCreationService` に `autogen` 多回リトライ（既定ON・4回）を追加し、再試行対象を一時エラー文字列で判定するようにした。
- `ThumbnailEngineRuntimeStats` を新規追加し、`autogen` 一時失敗・再試行成功・`ffmpeg1pass` フォールバック件数を集計できるようにした。
- 2026-03-01 20:58台ログ（`selected_autogen=816, autogen_failed=9, gdi=8, fallback_1pass=9`）を根拠に、単発失敗で過敏に下げないようスケールダウン率閾値を `2% -> 8%` へ調整した。

### 10.5 完了条件（次段）
1. 24固定運用と比較して、`autogen -> ffmpeg1pass` フォールバック率が有意に低下している。
2. 低スペックPC想定条件で、連続失敗時に並列数が自動で下がり、処理停止せず継続できる。
3. 負荷が落ち着いた後、段階的に並列数が戻ることをログで確認できる。

### 10.7 実装メモ（2026-03-01 22:25 追加: autogen保存GDI+対策）
- [x] T13: JPEG保存の共通セーフティ層を追加
  - 実装: `ThumbnailCreationService.TrySaveJpegWithRetry` を追加。
  - 内容: `再試行(最大3回) + 一時ファイル保存 + 原子的置換(File.Replace/File.Move)`。
- [x] T14: GDI+保存の同時実行上限を導入
  - 実装: `JpegSaveGate` を追加して、保存処理だけ同時実行を制御。
  - 設定: `IMM_THUMB_JPEG_SAVE_PARALLEL`（既定 `4`, 範囲 `1..32`）。
- [x] T15: autogen保存経路を共通ヘルパーへ切替
  - 実装: `FfmpegAutoGenThumbnailGenerationEngine` の
    - ブックマーク保存
    - 結合サムネ保存
    を `TrySaveJpegWithRetry` 利用へ変更。
- [x] T16: 既知の入力破損シグネチャ時に `ffmpeg1pass` を事前スキップ
  - 実装: `ThumbnailCreationService` に `ShouldSkipFfmpegOnePassByKnownInvalidInput` を追加。
  - 判定: `invalid data found when processing input` / `moov atom not found` が先行失敗に含まれる場合。
  - 効果: 壊れた入力での `ffmpeg.exe` 起動を減らし、GPUスパイクと無駄待ちを抑える。
- [x] T17: Everythingポーリングをキュー負荷連動で動的間引き
  - 実装: `MainWindow.xaml.cs` に `ResolveEverythingWatchPollDelayMs` を追加し、`RunEverythingWatchPollLoopAsync` の待機時間を動的化。
  - 設定値: `active_queue >= 200 -> 15000ms`, `>= 50 -> 6000ms`, それ以外 `3000ms`。
  - 効果: サムネイル大量処理中の `CheckFolderAsync` 空振り連打を抑え、UI負荷を下げる。
- [x] T18: `ffmpeg1pass` スキップ条件を拡張
  - 実装: `FfmpegOnePassSkipKeywords` に `video stream is missing` を追加。
  - 効果: `.wmv` 系のような非動画ストリーム入力で、不要な `ffmpeg1pass` 実行を回避。
- [x] T19: `video stream is missing` を DRM疑い分類へ変更
  - 実装: `DrmErrorKeywords` に `video stream is missing` を追加。
  - 効果: 該当ケースを `placeholder-drm` へ統一する。
- [x] T30: WMV/ASF のDRMプリチェックを `CreateThumbAsync` 入口へ追加
  - 実装タイミング: キュー実行時（`ThumbnailCreationService.CreateThumbAsync` のエンジン選択前）。
  - 判定方法: `.wmv/.asf` のみ、先頭 `64KB` を走査して `Content Encryption Object GUID` を検出。
  - GUID: `2211B3FB-BD23-11D2-B4B7-00A0C955FC6E`
  - 挙動: ヒット時はデコーダー実行をスキップし、`placeholder-drm-precheck` で即完了扱い。
  - 手動更新 (`isManual=true`) は対象外（既存手動フロー維持）。
  - テスト: `CreateThumbAsync_WmvDrmPrecheckHit_エンジン実行せずプレースホルダーで成功する` を追加。

期待効果:
- 高並列時の `A generic error occurred in GDI+` による単発失敗を吸収しやすくなる。
- 破損途中ファイルを残しにくくなり、`ffmediatoolkit` 側の `combined thumbnail save failed` 低減にも効く。

## 11. DB書き込み前のQueue投入/UI先行可否（2026-03-03追記）

### 11.1 結論
- `Queue投入先行`: 可能
  - `QueueObj.MovieId` が未確定でも、サムネ作成時に `MoviePath` から補完できる実装が既にある。
  - QueueDBの一意キーは `MainDbPathHash + MoviePathKey + TabIndex` であり、`MovieId` 非依存。
- `UI正式表示（MovieRecs追加）先行`: 原則不可（現行実装）
  - 現在の `TryAppendMovieToViewByPath` は `movie` テーブル読込前提のため、DB未反映状態では正式行を追加できない。
- `UI一部先行（仮表示）`: 可能
  - 「登録待ち」などの軽量プレースホルダを別コレクションで表示し、DB反映後に正式行へ差し替える方式なら実現できる。

### 11.2 実装方針（追加）
1. Queue先行（先に着手）
- スキャンで新規候補を見つけた時点で `MovieId=0` のまま `TryEnqueueThumbnailJob` へ投入する。
- MainDB書き込みは `pendingNewMovies` バッファで継続し、確定後に必要なUI反映のみ行う。

2. UI仮表示（任意）
- `MovieRecords` 本体には触らず、仮表示専用ViewModelを追加する。
- 表示項目は最小（`MoviePath`, `TabIndex`, `状態=登録待ち`）に限定する。
- DB反映完了時に仮表示を除去し、既存の `DataRowToViewData` 経由で正式表示へ統合する。

### 11.3 追加タスク
- [ ] T20: `CheckFolderAsync` で新規候補に対する `Queue先行投入(MovieId=0)` を実装する。
- [ ] T21: `ResolveMovieIdByPathAsync` の補完失敗時ログを追加し、追跡可能にする。
- [x] T22: （任意）UI仮表示レイヤを追加し、DB反映後に正式行へ置換する。
  - 実装: `PendingMovieRecs` を追加し、検知時に仮表示、DB反映完了時に除去する最小実装を導入。
  - UI: タイトルバーに `登録待ち` 件数を表示（既存 `MovieRecs` とは分離）。
- [ ] T23: 回帰確認（重複投入、サムネ反映漏れ、FilterAndSort後の表示整合）を実施する。

## 12. 新規要件（フォーム下タブ: サムネイル進捗）
- 要件定義書:
  - `Watcher/要件定義_サムネイル進捗タブ_2026-03-03.md`
- 目的:
  - 作成スレッドの可視化（左: 基本情報、右: スレッド別パネル）

### 12.1 タスク（要件定義から実装へ）
- [x] T24: 進捗スナップショット層を追加する（作成済み数/総キュー/現在並列/最大並列）。
  - 実装: `ThumbnailProgressRuntime` を追加し、`ThumbnailQueueProcessor` から進捗通知を受けてスナップショット化。
- [x] T25: キュー投入ログ（最新N件リングバッファ）を追加する。
  - 実装: `TryEnqueueThumbnailJob` 成功時に動画名を最新10件で保持し、UI表示へ反映。
- [x] T26: CPU/GPU/HDDメーター取得層を追加する（取得不可時 `N/A`）。
  - 実装: `MainWindow.xaml.cs` に500ms更新タイマーとサンプラを追加。CPUはシステム全体使用率、GPU/HDD取得失敗時は `N/A` 表示へフォールバック。
- [x] T27: 下部タブ `サムネイル進捗` を実装し、左/右UIを接続する。
  - 実装: `MainWindow.xaml` の下部タブに `サムネイル進捗` を追加し、`ThumbnailProgressViewState` へバインド。
- [x] T28: 長い動画名の省略表示（拡張子保持）と画像高さ統一表示を実装する。
  - 実装: 省略規則を `ThumbnailProgressRuntime` に実装し、右サイド画像は高さ `72px` 固定で表示。
- [x] T29-A: 自動回帰確認（Build/Test）を実施する。
  - 実施日: 2026-03-03
  - 実施内容:
    - `MSBuild.exe IndigoMovieManager_fork.sln /t:Build /p:Configuration=Debug /p:Platform=x64 /m` 成功
    - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug --no-build` 成功
    - 結果: `合格 23 / スキップ 1 / 失敗 0`（進捗タブ関連テスト4件を含む）
  - 影響範囲確認:
    - 再生・検索ロジック本体（`MainWindow.Player.cs`, `MainWindow.Search.cs`）へのコード変更なし
    - 変更は主に進捗タブ表示層とサムネイルキュー進捗収集層に限定
- [ ] T29-B: 実機UI目視回帰（体感遅延・表示追従）を実施する。
  - 観点:
    - サムネイル大量作成中に操作遅延が体感増加しないこと
    - 進捗タブの左情報と右パネルが崩れず追従すること

### 12.2 追加実装メモ（2026-03-04: 巨大動画で1スレッド貼り付き対策）
- [x] T31: 巨大動画混在時に「残1ジョブ待ち」で並列が遊ぶ問題を抑制
  - 実装: `ThumbnailQueueProcessor` に `EnumerateLeasedItemsAsync` を追加し、処理中でも次リースを逐次補充。
  - 効果: 72GB級ジョブが走っていても、空いたワーカーへ次ジョブを継続投入できる。
- [x] T32: バックログが2件以上ある時の最低並列を2に即時復帰
  - 実装: `ThumbnailParallelController.EnsureMinimum` を追加し、`activeCountAtBatchStart >= 2` の場合に `min=2` を適用。
  - 効果: 一時的に1並列へ落ちた後でも、次バッチ開始時に2スレ目を早く復帰させる。
- [x] T33: 動画サイズをキュー投入時に保持し、実行時まで引き回す
  - 実装: `QueueObj -> QueueRequest -> QueueDB -> QueueDbLeaseItem` に `MovieSizeBytes` を追加。
  - 実装: キュー投入時に取得できた `FileInfo.Length` を `QueueObj.MovieSizeBytes` へ保存。
  - 実装: `ThumbnailCreationService` で `queueObj.MovieSizeBytes` を優先利用し、未設定時のみ再取得。
  - 効果: サイズ依存制御やログ分析の土台を用意し、重いI/O再取得を減らせる。
