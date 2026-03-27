# RescueWorker 外部接続仕様書 2026-03-17

## 1. この資料の位置づけ

- これは **現在実装されている** `IndigoMovieManager.Thumbnail.RescueWorker` の外部接続仕様。
- 将来候補の `--main-db-hash` / `--main-db-name` や standalone mode は含めない。

## 2. 起動モード

### 2.1 巡回・救済モード

- 用途:
  - Failure DB を見て `pending_rescue` を拾い、救済を実行する
- 引数:
  - `--main-db <path>` 必須
  - `--thumb-folder <path>` 任意
  - `--log-dir <path>` 任意
  - `--failure-db-dir <path>` 任意

### 2.2 個別試行モード

- 用途:
  - 指定 engine の 1 回試行を子プロセスへ隔離して実行する
- 引数:
  - `--attempt-child`
  - `--engine <id>`
  - `--movie <path>`
  - `--source-movie <path>` 任意
  - `--db-name <name>`
  - `--thumb-folder <path>`
  - `--tab-index <index>`
  - `--movie-size-bytes <size>`
  - `--thumb-sec-csv <csv>` 任意
  - `--result-json <path>`
  - `--log-dir <path>` 任意

## 3. Host 注入ポリシー

- `RescueWorker` は host 側 path policy を自分で決めない方針で進めている
- 現在注入しているもの:
  - launcher ctor で渡す session root
  - launcher ctor で渡す resolved worker executable path
  - launcher ctor で渡す supplemental directory paths
  - launcher ctor で渡す supplemental file paths
  - `--thumb-folder`
  - `--log-dir`
  - `--failure-db-dir`
- まだ worker が受け取っていないもの:
  - `--queue-db-dir`
  - main db の識別子抽象化

## 4. 現在の依存境界

- worker は `Runtime` project を参照しない
- worker が使うのは `Engine` 契約:
  - `IThumbnailCreationHostRuntime`
  - `IThumbnailCreateProcessLogWriter`
- worker 内部には最小の既定実装がある:
  - `RescueWorkerHostRuntime`
  - `RescueWorkerProcessLogWriter`
- marker 付き publish artifact
  - `rescue-worker-artifact.json` が exe と同じ directory にあり、`compatibilityVersion` が一致する時だけ、Factory はそれを完成済み artifact とみなす
  - この経路では `SupplementalDirectoryPaths` / `SupplementalFilePaths` は空でよい

## 5. 状態管理

- `ThumbnailFailureDb` を通じて lease / heartbeat / status 更新を行う
- `ThumbnailFailureDb` の保存先は `ThumbnailQueueHostPathPolicy` へ `--failure-db-dir` で注入した値を優先する
- 主な状態遷移:
  - `pending_rescue`
  - `processing_rescue`
  - `rescued`
  - `gave_up`
  - `skipped`

## 6. 出力

### 6.1 サムネイル

- 生成成功時は `--thumb-folder` 解決先へ `.jpg` を出力

### 6.2 process log

- `thumbnail-create-process.csv`
- `--log-dir` 指定時はそこを優先
- 未指定時は worker 実行 directory 配下の `logs`

### 6.3 rescue trace

- `thumbnail-rescue-trace.csv`
- `ThumbnailRescueTraceLog.ConfigureLogDirectory(...)` へ渡した directory を使う

## 7. 終了コード

| code | 意味 |
| :--- | :--- |
| `0` | 正常終了 |
| `1` | 実行中例外 |
| `2` | 引数エラー |
