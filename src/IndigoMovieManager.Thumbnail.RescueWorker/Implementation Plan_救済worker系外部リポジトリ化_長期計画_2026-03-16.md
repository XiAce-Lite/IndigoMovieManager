# Implementation Plan 救済worker系外部リポジトリ化 長期計画 2026-03-16

最終更新日: 2026-03-16

変更概要:
- 救済worker系を長期的に外部リポジトリ化する大粒度計画を整理した
- 外へ出すものと本体repoに残すものを固定した
- フェーズ順と各段階の完了条件を定義した

## 1. 目的

救済workerまわりを、本体WPFアプリとは別の独立した開発単位へ育てる。

狙いは次の3点である。

1. 本体repoの責務を UI / 通常キュー / 起動制御に絞る
2. 救済worker系を独立ビルド、独立配布、独立改善できるようにする
3. `workthree` の最優先である「ユーザー体感テンポ」を守りつつ、重い完遂責務を本体から外す

## 2. 長期ゴール

長期的には、次の構成を 1 つの外部repo として持つ。

- `IndigoMovieManager.Thumbnail.Contracts`
- `IndigoMovieManager.Thumbnail.Engine`
- `IndigoMovieManager.Thumbnail.FailureDb`
- `IndigoMovieManager.Thumbnail.WorkerHost`
- `IndigoMovieManager.Thumbnail.Worker.Tests`

ここでの考え方は単純である。

- `worker` は概念名
- `WorkerHost` は実行物
- `Contracts / Engine / FailureDb` は WorkerHost が食う共有ライブラリ

つまり、長期計画としては
`worker + Contracts + Engine + FailureDb + WorkerHost`
をまとめて外部repoへ出す、でよい。

ただし `Queue` 全体は外へ出さない。
外へ出すのは `Queue` のうち、workerと契約共有する `FailureDb` 相当部分までに絞る。

## 3. 本体repoに残すもの

次は本体repoに残す。

- WPF 本体
- `ThumbnailRescueWorkerLauncher`
- 通常の `QueueDb`
- 通常サムネ生成の起動制御
- UI進捗反映
- MainDB / 一覧 / タブ / Watcher と強く結びついたコード

理由:

- これらは「ユーザーが触るテンポ」に直結する
- worker 側へ寄せると、境界をまたぐ通信が増えて逆に遅くなる
- 本体repoの責務は「選別」「起動」「反映」で止めるのが筋である

## 4. 外部repoに出すもの

次は外部repoに寄せる。

- サムネ救済の共通DTO
- 救済workerが使うエンジン群
- `FailureDb` のスキーマとアクセス
- rescue 実行計画
- isolated child process 制御
- worker 用 publish / テスト / CI

理由:

- ここは UI よりも「救済アルゴ」「障害隔離」「配布差し替え」が主戦場である
- 別repoのほうが変更責務が明確になる

## 5. 先に固定する境界

repo を分ける前に、次の境界を固定する。

1. CLI 引数
2. `FailureDb` の責務
3. stdout / stderr の観測形式
4. `result json` の形式
5. worker 成果物の配置規約

この境界が固まるまでは、物理的に repo を分けない。
先に repo を分けると、調整コストだけが増える。

## 6. 推奨repo構成

### 6.1 本体repo

- `IndigoMovieManager_fork`
- WPF
- Launcher
- 通常キュー
- UI同期

### 6.2 外部repo

repo 名の候補:
- `IndigoMovieManager.Thumbnail.Worker`

中のプロジェクト:
- `src/IndigoMovieManager.Thumbnail.Contracts`
- `src/IndigoMovieManager.Thumbnail.Engine`
- `src/IndigoMovieManager.Thumbnail.FailureDb`
- `src/IndigoMovieManager.Thumbnail.WorkerHost`
- `tests/IndigoMovieManager.Thumbnail.Worker.Tests`

## 7. フェーズ計画

### Phase A: 現repo内での責務整理

目的:
- まず「外に出せる形」にする

やること:
- `RescueWorker` から本体直接参照を消す
- `Engine` の依存を見える化する
- `FailureDb` の worker 観点責務を固定する

完了条件:
- `RescueWorker` が本体プロジェクト参照なしで単体ビルドできる

現状:
- 着手済み

### Phase B: `Engine` の物理自立

目的:
- `Engine` を repo 外に持ち出せる物理構造にする

やること:
- `Engine.csproj` のリンクコンパイルを解消する
- `QueueObj` / `TabInfo` / `ThumbInfo` / `ThumbnailPathResolver` などの所属先を整理する
- UI寄りコードを `Engine` から排除する

完了条件:
- `Engine` が repo ルート外のソースを参照せずビルドできる

### Phase C: `Contracts` 新設

目的:
- worker と本体の共有型を明示化する

やること:
- worker実行に必要な DTO を `Contracts` へ寄せる
- `Engine` と `WorkerHost` が `Contracts` にだけ依存する形へ寄せる
- 本体固有型を共有境界から外す

完了条件:
- worker系共有型が `Contracts` に集約される

### Phase D: `FailureDb` 独立

目的:
- queue 全体ではなく `FailureDb` だけを独立ライブラリにする

やること:
- `FailureDb` の schema / service / record を独立させる
- `Queue` から worker に不要な責務を切り離す
- MainDB や通常 `QueueDb` と混ざる責務を外す

完了条件:
- worker は `FailureDb` ライブラリだけで rescue 状態遷移を完結できる

### Phase E: `WorkerHost` 薄化

目的:
- 実行物を薄くし、ホストと中身を分離する

やること:
- `Program.cs` / 引数解釈 / ログ出力 / publish 前提処理を `WorkerHost` に閉じる
- rescue 本体ロジックをライブラリ寄りに寄せる
- child process 制御も `WorkerHost` 配下で責務を分ける

完了条件:
- `WorkerHost` が薄いホストとして成立する

### Phase F: 配布境界の固定

目的:
- 本体repoがソース参照ではなく成果物参照で worker を起動できるようにする

やること:
- worker publish 物のフォルダ構成を固定する
- version / hash / 配置規約を決める
- `ThumbnailRescueWorkerLauncher` の探索順を配布物前提へ寄せる

完了条件:
- 本体repoは worker の publish 物だけで起動できる

現状:
- 2026-03-17 時点で、repo 内では `rescue-worker-artifact.json` + `compatibilityVersion` 付き publish artifact を生成済み
- 2026-03-17 時点で、ローカル script と GitHub Actions の両方から同じ worker artifact package を作れる

### Phase G: 外部repo作成

目的:
- worker系を独立repoへ移し、運用を分ける

やること:
- 新repoを作る
- `Contracts / Engine / FailureDb / WorkerHost / Tests` を移す
- CI / publish / versioning を新repoへ置く

完了条件:
- 外部repo単体で build / test / publish が通る

### Phase H: 本体repo切替

目的:
- 本体repoから worker ソースを外す

やること:
- project reference を成果物参照へ切り替える
- 開発時 override は環境変数で逃がす
- 実動画確認手順を新構成へ更新する

完了条件:
- 本体repoに worker ソースが無くても rescue が動く

## 8. 判断基準

### 外部repo化を進めてよい条件

- `Engine` が物理自立している
- `FailureDb` の責務が固定している
- WorkerHost の入出力契約が固定している
- 本体側 launcher が成果物起動前提で動ける

### まだ repo を分けてはいけない条件

- `Engine` がリンクコンパイルに依存している
- shared 型が UI 型に寄っている
- `FailureDb` の列責務が揺れている
- live 確認手順が同一solution前提のままである

## 9. やらないこと

次は長期計画から除外する。

1. `Queue` 全体の外部repo化
2. `Watcher` の外部repo化
3. 本体から救済workerを in-proc DLL として呼ぶ構成
4. 先に repo を分けてから境界を合わせる進め方

## 10. リスク

### 高

- `Engine` に UI寄り責務が混ざっていて、切り出し時に広範囲改修になる
- worker publish 物と本体期待バージョンがずれて起動事故になる

### 中

- テストが project reference 前提で、repo 分離後に再設計が必要になる
- ドキュメントと live 手順が古いまま残る

## 11. 成功条件

長期計画の成功は次で判断する。

1. 本体repoは UI と通常経路に集中できる
2. worker系は独立repoで build / test / publish できる
3. rescue 改善を本体repoの巨大差分なしで回せる
4. 体感テンポを落とさずに、重い完遂責務を本体外へ閉じ込められる

## 12. 直近の次アクション

1. `Implementation Plan_独自repo化_ファイル単位仕分け_2026-03-17.md` を基準に、独自repoの一次対象と本体repo残置物を固定する
2. 優先度A の 5 本を `QueueObj -> ThumbInfo -> Tools -> ThumbnailCreationService -> ThumbnailQueueProcessor` の順で自前化する
3. `Engine.csproj` のリンクコンパイル棚卸し結果を、物理移管順へ落とし直す
4. `FailureDb` の worker 観点責務と `WorkerHost` の入出力契約を切り分けて固定する

## 13. 参照ファイル

- `src/IndigoMovieManager.Thumbnail.RescueWorker/Implementation Plan_救済worker別リポジトリ化_2026-03-16.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Implementation Plan_独自repo化_ファイル単位仕分け_2026-03-17.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/IndigoMovieManager.Thumbnail.RescueWorker.csproj`
- `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
- `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
