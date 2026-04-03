# Implementation Plan RescueWorker v1契約 PrivateRepo前提 2026-04-04

最終更新日: 2026-04-04

## 1. 目的

`RescueWorker` を Private な別 repo へ出す時に、最初の外部契約を広げすぎないための正本である。

この文書の目的は次の 3 つである。

1. `IndigoMovieManager` から見た v1 契約を固定する
2. 現在の `RescueWorker` 実装境界を壊さずに JSON 契約へ昇格する
3. 将来の `IndigoMovieEngine` 化へつながるが、最初から汎用 platform を作り込みすぎない

## 2. 先に結論

結論は次である。

1. v1 の外部契約は `RescueWorker` の main mode だけを対象にする
2. `attempt-child` と `direct-index-repair` は v1 では engine 内部 API として扱い、外部契約へ出さない
3. v1 job schema は `sourcePath / outputPath` のような汎用形ではなく、現行 main mode の実引数をそのまま使う
4. 本体と engine の間は `CLI + JSON` を正式境界にする
5. wrapper は新設してよいが、内部では現行 CLI 引数へ 1 対 1 変換する

## 3. v1 のスコープ

### 3.1 外部契約に含めるもの

- `RescueWorker` の巡回・救済 main mode
- `job.json`
- `result.json`
- 終了コード
- `stdout/stderr` の最低限ルール

### 3.2 v1 では外部契約に含めないもの

- `--attempt-child` の直接呼び出し
- `--direct-index-repair` の直接呼び出し
- 汎用 `jobType` プラットフォーム化
- HTTP API
- 本体と engine の内部 DTO 共有

この判断により、v1 は「RescueWorker を安全に外へ出す最小契約」に留める。

## 4. v1 の呼び出し形

本体からは次の形だけを正式呼び出しとする。

```powershell
indigo-engine rescue --job-json <path> --result-json <path>
```

ここで重要なのは、外部契約としては `job.json / result.json` を見るが、
engine repo 内部では現行 `RescueWorkerApplication.Arguments.cs` の CLI 引数へ変換してよい、である。

つまり v1 は:

- 外から見る境界は `CLI + JSON`
- 中で使う既存 worker の実引数は維持

という 2 層構造で進める。

## 5. v1 job.json

### 5.1 正本フィールド

main mode の v1 では、次を正本名にする。

- `contractVersion`
- `mode`
- `requestId`
- `mainDbFullPath`
- `thumbFolderOverride`
- `logDirectoryPath`
- `failureDbDirectoryPath`
- `requestedFailureId`
- `metadata`

### 5.2 必須 / 任意

- 必須
  - `contractVersion`
  - `mode`
  - `requestId`
  - `mainDbFullPath`
- 任意
  - `thumbFolderOverride`
  - `logDirectoryPath`
  - `failureDbDirectoryPath`
  - `requestedFailureId`
  - `metadata`

### 5.3 例

```json
{
  "contractVersion": "1",
  "mode": "rescue-main",
  "requestId": "2026-04-04-0001",
  "mainDbFullPath": "D:/Indigo/sample.wb",
  "thumbFolderOverride": "D:/Indigo/thumb",
  "logDirectoryPath": "D:/Indigo/logs",
  "failureDbDirectoryPath": "D:/Indigo/failure-db",
  "requestedFailureId": 0,
  "metadata": {
    "caller": "IndigoMovieManager",
    "callerVersion": "1.0.0"
  }
}
```

### 5.4 現行 CLI との対応

| v1 job.json | 現行 CLI |
| :--- | :--- |
| `mainDbFullPath` | `--main-db <path>` |
| `thumbFolderOverride` | `--thumb-folder <path>` |
| `logDirectoryPath` | `--log-dir <path>` |
| `failureDbDirectoryPath` | `--failure-db-dir <path>` |
| `requestedFailureId` | `--failure-id <id>` |

## 6. v1 result.json

### 6.1 正本フィールド

- `contractVersion`
- `mode`
- `requestId`
- `status`
- `resultCode`
- `message`
- `engineVersion`
- `compatibilityVersion`
- `startedAt`
- `finishedAt`
- `artifacts`
- `errors`

### 6.2 status

- `success`
- `failed`

v1 では `partial` は入れない。
main mode は `FailureDb` を通じて個別の rescued / gave_up / skipped を管理するため、
wrapper の戻り値はまず `process と契約が正常完了したか` に寄せる。

### 6.3 artifacts

v1 では次だけを想定する。

- `process-log`
- `rescue-trace`
- `worker-artifact-manifest`

### 6.4 例

```json
{
  "contractVersion": "1",
  "mode": "rescue-main",
  "requestId": "2026-04-04-0001",
  "status": "success",
  "resultCode": "OK",
  "message": "RescueWorker completed.",
  "engineVersion": "1.0.0",
  "compatibilityVersion": "2026-03-17.1",
  "startedAt": "2026-04-04T10:30:00+09:00",
  "finishedAt": "2026-04-04T10:31:02+09:00",
  "artifacts": [
    {
      "type": "process-log",
      "path": "D:/Indigo/logs/thumbnail-create-process.csv"
    },
    {
      "type": "rescue-trace",
      "path": "D:/Indigo/logs/thumbnail-rescue-trace.csv"
    }
  ],
  "errors": []
}
```

## 7. 終了コード

v1 では、まず現行 worker の終了コード体系に合わせる。

| code | 意味 |
| :--- | :--- |
| `0` | 正常終了 |
| `1` | 実行中例外または業務失敗 |
| `2` | 引数または契約不正 |

ここでは `3` 以上を先回りで定義しない。
理由は、最初の契約で意味を増やしすぎると manager / engine の両方で余計な分岐が増えるためである。

## 8. stdout / stderr

### 8.1 stdout

- 進捗と簡易診断を出してよい
- 人が追えることを優先する
- manager 側の UI 文言の正本にはしない

### 8.2 stderr

- 契約不正
- 起動失敗
- 例外要約

を短く出す。

### 8.3 v1 の原則

- 正式な機械判定は `result.json` と終了コード
- `stdout/stderr` は観測補助

## 9. なぜ汎用 job にしないか

`sourcePath / outputPath` 型の汎用契約は、将来像としては理解できる。
ただし v1 でそれを採ると、次の問題が起きやすい。

- 現在の `RescueWorker` 実装境界と 1 対 1 で対応しなくなる
- wrapper 側で暗黙変換が増える
- どこが契約変更なのか曖昧になる
- Private repo 化の前に境界がぶれる

したがって v1 は「現実の境界を固定する」ことを優先する。

## 10. v1 で internal 扱いに残すもの

### 10.1 attempt-child

これは engine 実行を隔離する内部実装であり、manager が直接知る必要はない。

### 10.2 direct-index-repair

これは user action / launcher から見れば別導線だが、
engine repo 外部契約の第1段としては main rescue mode と切り分けた方が安全である。

必要なら v2 以降で別 mode として外部契約化する。

## 11. 実装順

1. `RescueWorker` main mode の v1 `job.json / result.json` を doc で固定する
2. engine repo 側に `--job-json / --result-json` wrapper を追加する
3. wrapper 内部では現行 `--main-db / --thumb-folder / --log-dir / --failure-db-dir / --failure-id` へ 1 対 1 変換する
4. manager 側に最小の engine client を作る
5. 実機確認後に Private repo へ切り出す

## 12. 2026-04-04 時点の実装反映

- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.JobJsonMode.cs`
  - `rescue --job-json <path> --result-json <path>` の wrapper 骨格を追加済み
- いまの wrapper は
  - `job.json` 読取
  - `contractVersion / mode / requestId / mainDbFullPath` 検証
  - 既存 `RunMainRescueAsync(...)` への橋渡し
  - 最小 `result.json` 出力
  までを担う
- Public 側 `engine-client` は
  - `result.json` の `contractVersion / mode / requestId` を検証し
  - 起動時に発行した `requestId` と一致しない結果を fail-fast で弾く
  実装まで入っている
- `attempt-child` と `direct-index-repair` は予定どおり internal のままで、外部契約へはまだ出していない

## 13. 結論

最高のやり方は、「最初から理想の汎用 engine platform を作る」ことではない。

最初にやるべきなのは、

- `Private repo` 前提で
- `RescueWorker` の main mode を
- 現行実装の実境界ベースで
- `CLI + JSON` 契約へ固定する

ことである。

この順なら、

- 今の worker を壊しにくい
- manager 側の移行が読みやすい
- 将来 `IndigoMovieEngine` へ広げる余地も残る

## 13. 参照

- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/RescueWorker_外部接続仕様_2026-03-17.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.Arguments.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.EntryModes.cs`
- `Thumbnail/Docs/Implementation Plan_workerとサムネイル作成エンジン外だし_2026-04-01.md`
