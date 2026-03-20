# Implementation Plan: debug 動画別サムネ詳細トレース 2026-03-20

## 1. 目的

- `Debug` 実行時だけ、特定動画ごとに「どのサムネイル処理を通ったか」を時系列で追えるようにする。
- 既存の `debug-runtime.log` をさらに太らせず、必要時だけ専用ログへ詳細を落とせるようにする。
- 通常レーンと rescue worker の両方で、同じ動画の流れを 1 本の trace として追えるようにする。
- 既存の `thumbnail-create-process.csv` や `thumbnail-rescue-trace.csv` は壊さず、用途を分けて共存させる。

## 2. 現状整理

### 2.1 既にある観測

- `Infrastructure/DebugRuntimeLog.cs`
  - `DEBUG` 限定で `debug-runtime.log` へ追記する。
  - ただし全体ログであり、動画単位の束読みには弱い。
- `src/IndigoMovieManager.Thumbnail.Runtime/DefaultThumbnailCreateProcessLogWriter.cs`
  - `thumbnail-create-process.csv` に最終結果だけを書く。
  - 途中の `engine selected`、`fallback`、`marker cleanup`、`retry` は追えない。
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailRescueTraceLog.cs`
  - rescue 系の流れは追える。
  - ただし rescue 専用で、通常レーンと同じ粒度ではない。
- `Thumbnail/Adapters/AppThumbnailLogger.cs`
  - engine 側ログは最終的に `DebugRuntimeLog` へ流れる。
  - ここに動画別 trace の分岐点を足せる。

### 2.2 問題

- `debug-runtime.log` はカテゴリ単位では読めるが、同一動画の 1 回の生成束を安定して拾いにくい。
- `thumbnail-create-process.csv` は成功/失敗の確定結果しか残らず、途中経路が見えない。
- rescue worker へ handoff した後は `failure_id` や `ExtraJson` を辿る必要があり、通常レーンと連結しにくい。
- 調査したいのは大抵「1 本か数本の動画」なので、全動画へ常時詳細ログを出す設計は筋が悪い。

## 3. 方針

- 既定は完全無効のままにする。
- `Debug` 実行かつ環境変数で有効化した時だけ、専用トレースログへ書く。
- 対象動画は環境変数フィルタで絞る。
- 1 回の生成束を識別する `TraceId` を導入し、通常レーンから rescue worker まで引き回す。
- 既存ログは要約用、専用トレースログは調査用として役割分離する。

## 4. 提案する出力

### 4.1 専用ログ

- 新規ファイル名
  - `thumbnail-movie-trace.ndjson`
- 理由
  - 途中経路では `reason`、`engine_order`、`route_id`、`symptom_class`、`failure_id` など可変情報が多い。
  - CSV で列を増やし続けるより、1 行 1 JSON の方が後から読む側も機械処理しやすい。
  - PowerShell でも `Get-Content | ConvertFrom-Json` で即読める。

### 4.2 1 行の想定項目

- `ts`
- `trace_id`
- `movie_path`
- `movie_file_name`
- `source`
  - `main`
  - `queue`
  - `engine`
  - `rescue-worker`
  - `attempt-child`
- `lane`
  - `normal`
  - `rescue`
- `phase`
  - `enqueue`
  - `dequeue`
  - `precheck`
  - `engine_selected`
  - `engine_start`
  - `engine_retry`
  - `engine_result`
  - `marker_cleanup`
  - `handoff`
  - `repair_probe`
  - `repair`
  - `completed`
  - `gave_up`
- `engine`
- `tab_index`
- `failure_id`
- `attempt_group_id`
- `route_id`
- `symptom_class`
- `result`
- `elapsed_ms`
- `output_path`
- `detail`

## 5. 環境変数

### 5.1 最小セット

- `IMM_THUMB_MOVIE_TRACE`
  - `1/true/on/yes` で有効
  - 未指定は無効
- `IMM_THUMB_MOVIE_TRACE_FILTER`
  - 対象動画の絞り込み
  - まずは `;` 区切りの単純一致 + ファイル名一致で十分
  - 例
    - `sango72GB.mkv`
    - `C:\Users\na6ce\Desktop\_読み取り困難ログ用\超巨大動画\72\sango72GB.mkv`
- `IMM_THUMB_MOVIE_TRACE_LOG_DIR`
  - 未指定時は既存 `logs` 配下
  - 指定時だけ別フォルダへ出す

### 5.2 追加するなら有効なもの

- `IMM_THUMB_MOVIE_TRACE_ALL`
  - フィルタ無視で全動画を出したい時だけ使う
  - 常用は禁止寄り
- `IMM_THUMB_MOVIE_TRACE_APPEND_DEBUG_RUNTIME`
  - 専用ログに加えて `debug-runtime.log` に `trace_id=...` を短く併記したい時だけ使う
  - 初期段階では未実装でもよい

## 6. 実装方針

### 6.1 新規コンポーネント

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailMovieTraceRuntime.cs`
  - 環境変数の解決
  - 対象動画フィルタ判定
  - `TraceId` の生成
  - `Debug` 限定 no-op 境界
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailMovieTraceLog.cs`
  - `ndjson` 追記
  - `UTF-8 (BOMなし) + LF`
  - 例外非伝播
  - `LogFileTimeWindowSeparator` 再利用

### 6.2 既存 DTO への最小追加

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateArguments.cs`
  - `TraceId`
  - `TraceEnabled`
  - `TraceLane`
- `Thumbnail/Engines/ThumbnailJobContext.cs`
  - 同じ値を保持

これで engine / finalizer / image writer / precheck が引数から同じ trace を参照できる。

### 6.3 本体側の差し込み点

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `CreateThumbAsync(...)` 入口で trace 判定
  - 対象動画だけ `TraceId` を採番
  - `CreateThumbAsync start/end`
  - normal lane timeout 有無
  - source override
  - initial engine hint
  - 成功/失敗
- `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
  - rescue 要求作成時に `TraceId` を `ExtraJson` へ格納
  - `display error rerouted to normal queue`
  - `request_enqueued`
  - `duplicate`
  - `promoted`
  - `worker launch deferred/prioritized`

### 6.4 engine 側の差し込み点

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailEngineExecutionCoordinator.cs`
  - `engine_selected`
  - `engine_start`
  - `engine_retry`
  - `engine_result`
  - `engine_fallback`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailPrecheckCoordinator.cs`
  - `precheck_start`
  - `precheck_skip`
  - `precheck_existing_success`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateResultFinalizer.cs`
  - `marker_cleanup`
  - `failure_placeholder_created`
  - `process_log_written`
  - `completed`

### 6.5 rescue worker 側の差し込み点

- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
  - `FailureRecord.ExtraJson` から `TraceId` を読む
  - なければ lease 時に補助 trace を採番
  - `worker_leased`
  - `container_probe_start/result`
  - `plan_selected`
  - `repair_probe`
  - `repair_start/result`
  - `attempt_child_start/result`
  - `route_promoted`
  - `terminal`

### 6.6 isolated child への伝播

- worker の `--attempt-child` 引数に `--trace-id` を追加
- child 側も同じ `TraceId` で書く

これをやらないと、重い個体で一番見たい isolated attempt の線が切れる。

## 7. ログ肥大対策

### 7.1 既定値

- 無効
- フィルタ必須

### 7.2 出力量の考え方

- `debug-runtime.log`
  - 既存のまま要約
- `thumbnail-create-process.csv`
  - 既存のまま最終結果
- `thumbnail-rescue-trace.csv`
  - 既存のまま rescue 要約
- `thumbnail-movie-trace.ndjson`
  - 調査対象動画だけの詳細

### 7.3 なぜこの分離が必要か

- 既存ログへ詳細を混ぜると、普段の束読みが悪化する。
- 調査時の欲しい情報は「全部」ではなく「特定動画の 1 本の流れ」なので、別ファイルの方が圧倒的に扱いやすい。

## 8. 追跡キー

### 8.1 主キー

- `TraceId`

### 8.2 既存キーとの関係

- `failure_id`
  - rescue 入口以降の DB 側キー
- `attempt_group_id`
  - rescue 束の既存キー
- `movie_path`
  - 最終フォールバックの読み取りキー

### 8.3 方針

- 動画単位の人間向け束読みは `TraceId` を主にする。
- 既存 DB や既存サマリとの接続は `failure_id` / `attempt_group_id` を併記して保つ。

## 9. 実装順

### Phase 1: 最小導入

- `ThumbnailMovieTraceRuntime`
- `ThumbnailMovieTraceLog`
- `CreateThumbAsync` 入口/終了
- `ThumbnailEngineExecutionCoordinator`
- `ThumbnailCreateResultFinalizer`

完了条件:

- 対象動画 1 本について
  - 通常レーンの start
  - engine select/start/fail/success
  - 最終 success/fail
  が専用ログで読める

### Phase 2: rescue 伝播

- `ThumbnailRescueLane` で `TraceId` を `ExtraJson` へ保存
- worker lease 時に復元
- repair / route / terminal を追記

完了条件:

- 通常レーン失敗から rescue worker 成功まで、同じ `TraceId` で追える

### Phase 3: isolated child 伝播

- `--attempt-child --trace-id`
- child 結果を同じ `TraceId` へ統合

完了条件:

- worker 内子プロセス実行も線が切れない

## 10. 影響ファイル候補

- `Infrastructure/DebugRuntimeLog.cs`
  - 必須ではない
  - mirror 追加をするならここ
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
- `Thumbnail/Adapters/AppThumbnailLogger.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateArguments.cs`
- `Thumbnail/Engines/ThumbnailJobContext.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailEngineExecutionCoordinator.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailPrecheckCoordinator.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateResultFinalizer.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`

## 11. テスト

### 11.1 単体

- 環境変数 on/off 判定
- フィルタ一致
  - フルパス一致
  - ファイル名一致
  - 大文字小文字差
- `TraceId` 未指定時 no-op
- `ndjson` 1 行生成
- worker `ExtraJson` から `TraceId` 復元

### 11.2 手動

PowerShell 例:

```powershell
$env:IMM_THUMB_MOVIE_TRACE = "1"
$env:IMM_THUMB_MOVIE_TRACE_FILTER = "sango72GB.mkv"
```

確認:

- `logs/thumbnail-movie-trace.ndjson`
- 対象動画だけが出る
- 通常レーン -> rescue worker の線が `trace_id` で追える
- `debug-runtime.log` の肥大が従来運用を壊さない

## 12. 採用判断

- 既定無効で通常運用のテンポを落とさない
- 特定動画 1 本の詳細フローを 1 ファイルで追える
- rescue handoff 後も同じ `TraceId` で追える
- 既存の `thumbnail-create-process.csv` と `thumbnail-rescue-trace.csv` を壊さない

## 13. 今回あえてやらないこと

- Release 常時有効
- UI からのトレース設定画面追加
- 全動画の常時詳細記録
- 既存 `debug-runtime.log` の完全置換

まずは `Debug + 環境変数 + 対象動画絞り込み` の三点固定で入れるのが正しい。
