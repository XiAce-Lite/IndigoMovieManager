# 設計メモ engine-client責務表 Public本体責務集中 2026-04-04

最終更新日: 2026-04-04

## 1. 目的

Public repo 側の `engine-client` が、何を担当し、何を担当しないかを固定する。

この文書の目的は次である。

1. Public repo を `app に機能を追加し、配る` 責務へ集中させる
2. `engine-client` が engine 本体の実装詳細を抱え込まないようにする
3. Private repo との境界を、実装依存ではなく契約依存で保つ

## 2. 先に結論

`engine-client` は、Public repo 側の薄い呼び出し層である。

やることは次に限る。

- job.json を作る
- worker / engine を起動する
- result.json を読む
- compatibilityVersion と lock file を見る
- 失敗理由を app 側で扱える形へ正規化する

逆に、次は持たない。

- rescue の実アルゴリズム
- engine 内部 route 判定
- index repair の処理本体
- worker 内部 DTO
- preview artifact の build 責務

## 3. 置き場所の考え方

初期形では、`engine-client` は Public repo の launcher / 設定 / contract adapter として置く。

想定する責務帯は次である。

- `Thumbnail/ThumbnailRescueWorkerLaunchSettings.cs`
- `Thumbnail/ThumbnailRescueWorkerLaunchSettingsFactory.cs`
- `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
- 今後追加する `job.json / result.json` adapter

つまり、`engine-client` は新しい巨大 module を作るというより、
既存 launcher 群を「外部 engine 呼び出し責務」として再整理したものと考える。

## 4. 責務表

| 領域 | engine-client が担当する | engine-client が担当しない |
| :--- | :--- | :--- |
| job 構築 | `job.json` の組み立て | engine 内部 DTO の生成 |
| 起動 | exe path 解決、引数組み立て、process 起動 | rescue 本体の処理実装 |
| 結果受信 | `result.json` 読取、exit code 判定 | result の業務意味を増やしすぎること |
| 互換判定 | `compatibilityVersion` / lock file / marker 照合 | compatibilityVersion の定義 |
| ログ | 起動失敗、契約不一致、artifact 不一致を app log に残す | engine 詳細ログの生成 |
| release | app package へ worker を同梱し、使う版を pin する | worker artifact 自体の build / publish |

## 5. Public repo 側で持つべき情報

`engine-client` が Public repo 側で持つのは、次だけでよい。

- worker 実行ファイルの所在
- supplemental file / dir の所在
- `compatibilityVersion`
- `rescue-worker.lock.json`
- `job.json` schema の最小必須項目
- `result.json` の最小読取項目

ここで大事なのは、engine の内部処理都合を Public repo へ逆流させないことである。

## 6. Public repo 側で持たない情報

次は Private repo 側へ寄せる。

- rescue plan の順番
- engine ごとの retry 条件
- repair probe の判定
- failure kind の細かい分類ロジック
- worker artifact の生成詳細
- preview package の CI

## 7. 入出力の責務

### 7.1 入力

`engine-client` は app の操作文脈から、最小 job を作る。

v1 main mode の正本フィールド:

- `contractVersion`
- `mode`
- `requestId`
- `mainDbFullPath`
- `thumbFolderOverride`
- `logDirectoryPath`
- `failureDbDirectoryPath`
- `requestedFailureId`

### 7.2 出力

`engine-client` は `result.json` から次だけを読む。

- `status`
- `resultCode`
- `message`
- `engineVersion`
- `compatibilityVersion`
- `artifacts`
- `errors`

これ以上の意味付けは、必要最小限に留める。

## 8. fail-fast の責務

Public repo 側の `engine-client` は、起動前に次を fail-fast で確認する。

- lock file が読める
- worker exe がある
- marker がある
- `compatibilityVersion` が一致する
- `sha256` が一致する

ここで失敗した時は silent fallback せず、app log に理由を残す。

## 9. UI との境界

UI は `engine-client` の内部事情を知らなくてよい。

UI 側は次だけを知ればよい。

- 受け付けできたか
- 起動できたか
- 完了を待つか
- 失敗理由の要約は何か

つまり UI は engine の詳細ではなく、app の機能追加と体感テンポへ集中する。

## 10. release の責務

Public repo 側の `engine-client` は、release 時に次を担う。

- app package に worker を同梱する
- lock file で使用 worker を pin する
- 利用者向けには app package だけを見せる

Private repo 側は次を担う。

- worker artifact を作る
- preview package を回す
- compatibilityVersion を定義する

## 11. 実務判断

`engine-client` は厚くしない方が強い。

理由は次である。

1. Public repo は app に機能を追加し、配る責務に集中したい
2. engine の改善速度は Private repo 側で確保したい
3. 両者の間に厚い shared logic を置くと、また再結合が始まる

したがって `engine-client` は、

- adapter
- launcher
- contract reader
- fail-fast checker

に留めるのがよい。

## 12. 参照先

- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/設計メモ_repo構成表_Public本体_PrivateEngine_2026-04-04.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/Implementation Plan_RescueWorker_v1契約_PrivateRepo前提_2026-04-04.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/TASK-008_main repo残置責務とexternal worker運用_2026-04-03.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/TASK-009_worker lock file schemaとlauncher読取骨格_2026-04-03.md`
