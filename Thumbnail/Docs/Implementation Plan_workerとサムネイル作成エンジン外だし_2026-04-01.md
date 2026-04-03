# Implementation Plan worker とサムネイル作成エンジン外だし 2026-04-01

最終更新日: 2026-04-03

変更概要:
- `RescueWorker` と `Thumbnail.Engine` の外だし観点を、既存資料と現行コードから再調査した
- 「いまどこまで分離済みか」と「まだ main repo に縛られている点」を整理した
- `workthree` の体感テンポ最優先を崩さない外だし順を、実装タスク付きで固定した
- 2026-04-02 時点の release / artifact 運用を反映し、配布境界の現在地を追記した
- `TASK-008` がどこまで進んでいて、何が未了かを見えるようにした
- 2026-04-03 のレビューを反映し、`RescueWorkerApplication.cs` の最小分割を前倒しした
- Phase 2 / Phase 3 / 外部repo移行条件を、より計測可能な形へ補正した
- 2026-04-03 に `Phase 0.5` 第1段を実装し、worker host を partial へ分割した
- 2026-04-03 に `Phase 1` 第1段を実装し、utility 4 ファイルを `Engine` 配下へ物理移動した
- 2026-04-03 に `Phase 1` 第2段を実装し、`Engines / Decoders / IndexRepair` の物理移動で link compile をゼロ化した

## 1. 目的

`RescueWorker` とサムネイル作成 engine を、将来的に main repo から外へ出せる形へ育てる。

ただし、今回の主眼は「いますぐ repo を分ける」ことではない。
先に次を見極めることが目的である。

1. どこまで外だし準備が終わっているか
2. 何がまだ main repo 前提で残っているか
3. どの順で進めると `workthree` の体感テンポを崩しにくいか

## 2. 先に結論

結論は次である。

1. `worker` はかなり外だし準備が進んでいる
2. `engine` は論理分離は進んでいるが、物理自立がまだ未完である
3. したがって本命は「worker だけ先に外へ出す」ではなく、「shared core を repo 内で自立させてからまとめて外へ出す」である
4. 外だし先は 2 repo に割るより、まず 1 repo に `Contracts / Engine / FailureDb / WorkerHost / Tests` を揃える方が安全である
5. main repo 側には `WPF / launcher / Runtime / 通常キュー起動制御 / UI同期 / app release` を残す
6. `Contracts` はサムネ専用に閉じすぎず、将来の `DerivedAsset` worker 基盤へ拡張できる余地を残す

## 3. 調査結果

### 3.1 worker 側はかなり進んでいる

- `src/IndigoMovieManager.Thumbnail.RescueWorker/IndigoMovieManager.Thumbnail.RescueWorker.csproj`
  - 参照は `Contracts / Engine / Queue` に絞られている
  - `IndigoMovieManager.csproj` や `Thumbnail.Runtime` への直接参照は無い
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerHostServices.cs`
  - worker 内部の host runtime / process log writer を持っている
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/RescueWorker_外部接続仕様_2026-03-17.md`
  - CLI 引数、stdout/stderr、`result json`、artifact marker が既に文章化されている
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Publish-RescueWorkerArtifact.ps1`
  - worker 単体 publish artifact を作る導線がある
- `scripts/create_rescue_worker_artifact_package.ps1`
  - worker 単体 ZIP をローカルで安定して作る導線がある
- `.github/workflows/rescue-worker-artifact.yml`
  - worker 単体確認は `workflow_dispatch` 専用へ整理されている

つまり worker は、すでに「別プロセス host」としての形はかなり出来ている。

### 3.2 engine 側も公開境界はある

- `src/IndigoMovieManager.Thumbnail.Engine/IThumbnailCreationService.cs`
  - 公開入口が `IThumbnailCreationService` に寄っている
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateArguments.cs`
  - `ThumbnailCreateArgs` / `ThumbnailBookmarkArgs` が公開 DTO になっている
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationServiceFactory.cs`
  - host runtime と log writer を外から注入する factory になっている
- `Thumbnail/AppThumbnailCreationServiceFactory.cs`
  - app 側 adapter がある
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerThumbnailCreationServiceFactory.cs`
  - worker 側 adapter もある
- `Thumbnail/Docs/DCO_エンジン分離実装規則_2026-03-05.md`
  - UI 逆流禁止、App -> Engine/Queue 一方向依存のルールがある

つまり engine は「使い方の境界」はかなり整っている。

### 3.3 いま外だしを止めている本当のボトルネック

#### ボトルネックA: Engine がまだ物理自立していない

- `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj`
- 2026-04-03 時点で、旧 `Thumbnail` 配下から参照していた
  - `ThumbInfo.cs`
  - `Tools.cs`
  - `ThumbnailEnvConfig.cs`
  - `ThumbnailPathResolver.cs`
  - `ThumbnailCreationService.cs`
  - `Decoders/**/*.cs`
  - `Engines/**/*.cs`
  の物理移動が完了した
- `Engine.csproj` の link compile はゼロになった

したがって「engine project はあるが、まだ repo ルート外の物理ファイルに依存している」状態は解消済みである。
次の本命は、物理自立の次段である `shared contract 薄化` と `FailureDb 独立` である。

#### ボトルネックB: 公開 DTO がまだ legacy 型を含む

- `ThumbnailCreateArgs` は `Request` 本流へ寄り、`QueueObj` は public 面から外れた
- legacy `QueueObj` 入口は `ThumbnailCreateArgsCompatibility` へ寄せた
- ただし `ThumbInfo` はまだ公開 DTO 側に残っている

したがって Phase 2 は着手済みだが、`ThumbInfo` と周辺 DTO の整理はまだ残っている。

#### ボトルネックC: worker は Queue 全体ではなく FailureDb だけ欲しいのに、今は Queue project ごと参照している

- `src/IndigoMovieManager.Thumbnail.RescueWorker/IndigoMovieManager.Thumbnail.RescueWorker.csproj`
  - `IndigoMovieManager.Thumbnail.Queue.csproj` を参照している
- 実際に欲しい中心は
  - `FailureDb`
  - `ThumbnailQueueHostPathPolicy`
  - 一部 IPC / path policy
  である

したがって worker 外だしの本命は、`Queue` 全体外だしではなく `FailureDb` 相当の独立である。

#### ボトルネックD: worker host が 1 ファイルに重すぎる

- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
  - 2026-04-03 実測で 3967 行まで縮小した
- ただし host 全体では
  - `RescueWorkerApplication.Arguments.cs`
  - `RescueWorkerApplication.EntryModes.cs`
  - `RescueWorkerApplication.Orchestration.cs`
  - `RescueWorkerApplication.EngineInvocation.cs`
  - `RescueWorkerApplication.RescuePlanning.cs`
  へ分かれたばかりで、Phase 4 の薄化自体はまだ残っている

repo を分ける前に、この host を
- 引数解釈
- lease / state 遷移
- rescue plan 構築
- engine 実行
- index repair
- trace / stdout
へ分けないと、移管後の改修テンポが落ちる。

### 3.4 main repo に残すべきものはむしろ明確

次は残す方が自然である。

- WPF 本体
- `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
- `Thumbnail/MainWindow.ThumbnailRescueWorkerLauncher.cs`
- `src/IndigoMovieManager.Thumbnail.Runtime/*`
- 通常キューの UI 起動制御
- rescued 同期と main tab 反映
- app release package

ここは「ユーザー体感テンポ」と「本体 release 都合」を握る host 側責務だからである。

### 3.5 配布境界は先に一部整理済み

2026-04-02 時点で、main repo 側の release / artifact 運用は次に整理されている。

- `.github/workflows/github-release-package.yml`
  - 公開 GitHub Release asset は app ZIP のみ
- `.github/workflows/rescue-worker-artifact.yml`
  - worker 単体 ZIP は手動実行の Actions Artifact 用
- `scripts/invoke_release.ps1`
  - 既定は app release 優先
  - worker 単体 ZIP は `-IncludeWorkerArtifactPackage` 明示時だけローカル生成

これは外だし計画にとって前進である。
利用者向け配布と worker 単体切り分けが分かれ、main repo に残す host 責務が見えやすくなったためである。

## 4. 推奨する目標構成

### 4.1 main repo に残すもの

- WPF 本体
- `Runtime`
- launcher
- 通常キューの起動制御
- UI 反映
- app 配布

### 4.2 外だし先 1 repo に寄せるもの

- `IndigoMovieManager.Thumbnail.Contracts`
- `IndigoMovieManager.Thumbnail.Engine`
- `IndigoMovieManager.Thumbnail.FailureDb`
- `IndigoMovieManager.Thumbnail.WorkerHost`
- `IndigoMovieManager.Thumbnail.Worker.Tests`

ここで重要なのは、外へ出すのは「worker 単体 exe」だけではなく、
worker が食う shared core ごと外へ出す、である。

### 4.3 main repo が外だし後の Engine をどう消費するか

- 第一候補は `Contracts / Engine / FailureDb` を package として消費する形である
- 当面の想定は private feed またはローカル feed、将来的には GitHub Packages 等の固定 feed を使う
- `WorkerHost` 本体だけは package ではなく publish artifact / ZIP として扱う

理由:

- `ProjectReference` を外しやすい
- main repo と external repo の version pin を CI で明示しやすい
- `compatibilityVersion` と package version の対応関係を整理しやすい

## 5. 実施順

### Phase 0: 境界固定

目的:
- main repo に残すものと外へ出すものを確定する

やること:
- `CLI 引数`
- `result json`
- `stdout/stderr`
- `rescue-worker-artifact.json`
- `compatibilityVersion`
- `FailureDb` 使用列
を正本化する

完了条件:
- host / worker 間の接続仕様が doc で固定している

現状:
- `CLI 引数`、`result json`、`rescue-worker-artifact.json`、`compatibilityVersion` は概ね固定済み
- 未固定なのは `stdout/stderr` の正式契約化と `FailureDb` 使用列の最終確定である

### Phase 0.5: WorkerHost 最低限分割

目的:
- 後続 Phase が巨大な `RescueWorkerApplication.cs` と全面衝突しないようにする

やること:
1. `CLI 引数解析`
2. `実行 orchestration`
3. `engine / index repair 呼び出し入口`
を最低 3 つの責務へ分ける

注意:
- この Phase では振る舞い変更を入れない
- 「薄くする」より先に「触れるサイズへする」を目的とする

完了条件:
- `RescueWorkerApplication.cs` から `CLI 引数解析` と `engine / index repair 呼び出し入口` が別ファイルへ出ている
- `RescueWorkerApplication.cs` 単体が 4000 行未満になっている

現状:
- 2026-04-03 に `Arguments / EntryModes / Orchestration / EngineInvocation / RescuePlanning` の partial 分割を入れた
- `RescueWorkerApplication.cs` 単体は 3967 行になり、最低限分割の完了条件は満たした
- ただし `RescueWorkerApplication` 全体の責務整理は Phase 4 で続ける

### Phase 1: Engine 物理自立

目的:
- `Thumbnail.Engine.csproj` のリンクコンパイルをゼロにする

やること:
1. `ThumbInfo`
2. `Tools`
3. `ThumbnailEnvConfig`
4. `ThumbnailPathResolver`
5. `ThumbnailCreationService`
6. `Decoders`
7. `Engines`
を `src/IndigoMovieManager.Thumbnail.Engine` 配下へ物理移動する

完了条件:
- `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj`
  が `Thumbnail/**/*.cs` を 1 本もリンクしない

現状:
- 2026-04-03 に
  - `ThumbInfo`
  - `Tools`
  - `ThumbnailEnvConfig`
  - `ThumbnailPathResolver`
  - `ThumbnailCreationService`
  - `Decoders`
  - `Engines`
  を `Engine` 配下へ物理移動した
- `Engine.csproj` の `Thumbnail/**/*.cs` link compile はゼロになった
- したがって Phase 1 は完了である

移管順:
1. `ThumbInfo / Tools / ThumbnailEnvConfig / ThumbnailPathResolver`
   - 依存が軽い基礎ユーティリティなので先に移す
2. `ThumbnailCreationService`
   - wrapper 本体だけで、`Composition` と公開契約がすでに `Engine` 側にあるため次に移す
3. `Thumbnail/Decoders/**/*.cs`
   - `Engines` から参照される葉に近い実装なので先に移す
4. `Thumbnail/Engines/**/*.cs`
   - `Decoders` と基礎ユーティリティへ依存するため最後に移す

TASK-001 結論:
- 物理移動の安全順は `基礎ユーティリティ -> ThumbnailCreationService -> Decoders -> Engines`
- `Engines` は `Decoders` より後ろに固定する
- `ThumbnailCreationService` は shell だけなので `Decoders / Engines` より先に動かしてよい

### Phase 2: shared contract 薄化

目的:
- public DTO から legacy 色を減らす

やること:
1. `ThumbnailCreateArgs` の `QueueObj` 依存を互換入口だけへ押し込む
2. `ThumbInfo` を facade のままでもよいので `Compatibility` 側へ寄せやすい形にする
3. worker / app 共通で必要な DTO を `Contracts` に揃える

完了条件:
- `ThumbnailCreateArgs` の public 面から `QueueObj` が消えている
- worker / app 側の公開呼び出しが `Contracts` と `IThumbnailCreationService` / host runtime interface だけで成立する
- worker csproj から見える shared public 型が、実質 `Contracts` 中心に限定される

現状:
- 2026-04-03 に `ThumbnailCreateArgs` から `QueueObj` を外した
- legacy `QueueObj` 入口は `ThumbnailCreateArgsCompatibility` へ寄せた
- `MainWindow` / `RescueWorker` は compatibility helper 経由で `Request` 本流へ接続した
- したがって `TASK-003` は完了した
- ただし `ThumbInfo` の公開面整理と shared public 型の更なる圧縮は未了である

### Phase 3: FailureDb 独立

目的:
- worker が `Queue` 全体ではなく `FailureDb` 相当だけに依存する形へ寄せる

やること:
1. `ThumbnailFailureDbPathResolver`
2. `ThumbnailFailureDbSchema`
3. `ThumbnailFailureDbService`
4. `ThumbnailFailureRecord`
を `FailureDb` ライブラリ候補として切り出す
5. `ThumbnailQueueHostPathPolicy` は `FailureDb` へ入れず、`Contracts` または host 境界の共通層へ寄せる

完了条件:
- worker csproj が `Queue` ではなく `FailureDb` + host 境界だけを参照できる
- `FailureDb` 側には DB スキーマ / CRUD / record 型だけが残る

現状:
- 2026-04-03 に `MainDbPathHash` / `MoviePathKey` 規約を `ThumbnailPathKeyHelper` として `Contracts` へ抽出した
- `FailureDb` から `QueueDbPathResolver` 直参照は外し、shared helper 経由へ寄せた
- `ThumbnailFailureRecord` / `ThumbnailFailureKind` は `Contracts` へ移し、worker と queue の共有データ型として固定した
- `ThumbnailFailureDbPathResolver / Schema / Service` は `IndigoMovieManager.Thumbnail.FailureDb` project へ移し、`Queue` の物理所有から外した
- `ThumbnailQueueHostPathPolicy` は `Contracts` へ移し、循環参照なしで `FailureDb` が参照できる形にした
- `ThumbnailRescueHandoffPolicy` も `Contracts` へ移し、worker csproj の `Queue` 参照を外した

### Phase 4: WorkerHost 薄化

目的:
- repo 分離後も扱いやすい host にする

やること:
1. `RescueWorkerApplication.cs` を役割ごとに分割する
2. rescue plan 構築を host から library 側へ寄せる
3. index repair と rescue 本線の分岐を読みやすくする

完了条件:
- `Program` / CLI / orchestration / rescue core が別ファイルで追える

現状:
- `Phase 0.5` で partial 分割は入った
- ただし rescue plan の library 側移送と host orchestration の更なる薄化は未着手
- host 薄化は引き続き外だし前の本命残件である

### Phase 5: 外部 repo 作成

目的:
- shared core + WorkerHost を別 repo として build / test / publish できるようにする

やること:
1. 新 repo に `Contracts / Engine / FailureDb / WorkerHost / Tests` を置く
2. worker artifact CI を新 repo へ移す
3. main repo は artifact 消費側に寄せる

完了条件:
- 外部 repo 単体で build / test / publish が通る

現状:
- まだ未着手
- ただし worker artifact 生成と CI の足場自体は main repo 内に先行実装済みである

### Phase 6: main repo 切替

目的:
- main repo から worker ソース参照を外す

やること:
1. launcher は publish artifact 前提の起動だけを担う
2. 実動画確認手順を新構成へ更新する
3. release helper / release doc を新構成へ揃える

完了条件:
- main repo に worker ソースが無くても rescue が動く

現状:
- まだ未着手
- release helper / workflow / doc は「app 公開 + worker 単体切り分け」までは整理済み
- 残るのは external repo 化後の起動・release・live 手順へ正本を切り替えることである

## 6. やらないこと

1. `Queue` 全体を外部 repo に出す
2. `Watcher` を外へ出す
3. worker を in-proc DLL に戻す
4. Engine のリンクコンパイルが残ったまま repo だけ分ける
5. launcher をローカル絶対パス前提に戻す

## 7. 実装タスクリスト

- [x] TASK-000 `RescueWorkerApplication.cs` の最低限分割（CLI / orchestration / engine invoke）を Phase 1 の前に行う
- [x] TASK-001 `Engine.csproj` のリンクコンパイル一覧をゼロにする移管順を確定する
- [x] TASK-002 `ThumbInfo / Tools / ThumbnailEnvConfig / ThumbnailPathResolver / ThumbnailCreationService / Decoders / Engines` を物理移動する
- [x] TASK-003 `ThumbnailCreateArgs` の legacy `QueueObj` 入口を互換層へ閉じ込める
- [x] TASK-004 `FailureDb` 独立ライブラリ候補の所属を固定し、shared path key 規約を `Contracts` へ上げる
- [x] TASK-005 worker csproj から `Queue` 参照を外せる最小単位を作る
- [x] TASK-006 `RescueWorkerApplication.cs` の分割方針を決める
- [ ] TASK-007 外部 repo の project 構成案と CI 最小構成を作る
- [ ] TASK-008 launcher / release / live 確認の main repo 残置責務を最終確定する

## 8. 判断基準

外だしを進めてよい条件:

1. `Engine` が物理自立している
2. worker が `Queue` 全体ではなく `FailureDb` 相当だけへ依存している
3. host / worker 間契約が doc で固定している
4. launcher が artifact 起動だけを担える
5. `compatibilityVersion` の bump 条件が固定している
6. main repo / external repo を同時変更する時の開発フローが決まっている
7. CI が package / artifact を決定的に取得できる

まだ repo を分けてはいけない条件:

1. `Engine.csproj` にリンクコンパイルが残っている
2. shared DTO が UI / app 固有事情へ引っ張られている
3. `FailureDb` と通常 `QueueDb` の責務が混ざっている
4. 実動画確認手順が同一 solution 前提のままである

### 8.1 運用条件

最低限、次は Phase 5 前に固定する。

1. `compatibilityVersion` は
   - `CLI 引数`
   - `result json`
   - `FailureDb schema`
   - worker artifact 配置規約
   のどれかが壊れる変更で bump する
2. algorithm だけの変更では `compatibilityVersion` をむやみに上げない
3. 両 repo 同時変更が必要な時は
   - external repo 側で package / artifact の preview 版を発行
   - main repo 側はその preview version を明示参照して検証
   の順で進める

## 9. 今回の調査での実務判断

実務上のおすすめは次である。

1. 先に `worker 単体外だし` を急がない
2. まず `RescueWorkerApplication.cs` の最低限分割を入れる
3. 次に `Engine 物理自立` と `FailureDb 独立` を main repo 内で終える
4. その後に `WorkerHost` を別 repo へ出す
5. 最後に main repo を artifact / package 消費専用へ寄せる

この順なら、`workthree` の体感テンポを壊さずに前へ進めやすい。

補足:
- release 運用は先に整理が進んだため、次に急ぐべきは workflow 再整理ではない
- いまの本命は引き続き `Engine 物理自立` と `FailureDb 独立` である

## 10. 参照

- `Thumbnail/Docs/DCO_エンジン分離実装規則_2026-03-05.md`
- `Thumbnail/Docs/history/Implementation Plan_Phase1_サムネイル作成エンジン別プロジェクト化_2026-03-03.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/Implementation Plan_救済worker別リポジトリ化_2026-03-16.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/Implementation Plan_救済worker系外部リポジトリ化_長期計画_2026-03-16.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/Implementation Plan_独自repo化_ファイル単位仕分け_2026-03-17.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/Implementation Plan_救済worker_Runtime参照除去_2026-03-17.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/RescueWorker_外部接続仕様_2026-03-17.md`
- `scripts/正式Release手順_GitHubTag運用_2026-03-30.md`
- `.github/workflows/github-release-package.yml`
- `.github/workflows/rescue-worker-artifact.yml`
