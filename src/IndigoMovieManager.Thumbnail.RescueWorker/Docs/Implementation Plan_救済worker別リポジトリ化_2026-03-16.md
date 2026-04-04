# Implementation Plan 救済worker別リポジトリ化 2026-03-16

最終更新日: 2026-04-05

変更概要:
- `RescueWorker` から本体 `IndigoMovieManager.csproj` への直接参照を外した
- 別リポジトリ化の前に潰すべき依存境界と実施順を整理した

## 1. 目的

`IndigoMovieManager.Thumbnail.RescueWorker` を将来的に別リポジトリへ分離し、
本体 repo から独立してビルド、配布、差し替えできる形へ寄せる。

この時、`workthree` の最優先である「ユーザー体感テンポ」を壊さないことを最上位条件とする。

## 2. 結論

いきなり救済worker一式を丸ごと別リポジトリへ切り出すのは危険である。
先に「本体依存を減らす」「共有ロジックの置き場所を固定する」「配布境界を決める」を順番に行う。

最初の現実解は次の形である。

1. 救済worker本体は引き続き別プロセスを維持する
2. 共有ロジックは `Engine` / `Queue` 側へ寄せる
3. 別リポジトリ化の対象は、まず `worker host` とその配布物に限定する

## 3. 現状整理

### 3.1 今回片付けたこと

- `src/IndigoMovieManager.Thumbnail.RescueWorker/IndigoMovieManager.Thumbnail.RescueWorker.csproj`
  から本体 `IndigoMovieManager.csproj` への直接参照を削除した
- 代わりに、実際に使用している `IndigoMovieManager.Thumbnail.Engine.csproj` を明示参照にした

これで `RescueWorker` の依存は少なくとも
`App -> Worker`
ではなく
`Worker -> Engine / Queue`
という形に一段寄った。

### 3.2 まだ残っている分離阻害要因

1. `IndigoMovieManager.Thumbnail.Engine.csproj` が `Thumbnail\*.cs` をリンクコンパイルしている
2. `RescueWorkerApplication` が `QueueObj` / `ThumbnailCreationService` / `TabInfo` / `ThumbInfo` / `ThumbnailPathResolver` に依存している
3. `ThumbnailRescueWorkerLauncher` が同一repo内のビルド出力探索を前提にしている
4. テストと運用手順が「同じsolution内にworkerプロジェクトがある」前提をまだ含んでいる

## 4. 分離後の目標像

### 4.1 目標構成

- 本体repo
  - WPF本体
  - launcher
  - `Engine` / `Queue` の共有契約
- worker repo
  - `RescueWorker` の host
  - publish 用スクリプト
  - worker 単体のテスト

### 4.2 守る境界

- 本体は worker を「外部実行物」として扱う
- worker は MainDB ではなく `FailureDb` と出力jpgの契約で連携する
- 本体と worker の共有物は `CLI引数`, `FailureDb schema`, `result json`, `stdout/stderr log` に絞る

## 5. 実施フェーズ

### Phase 1: 直接依存の除去

目的:
- `RescueWorker` から本体WPF依存を消す

作業:
1. `RescueWorker` の本体直接参照を削除する
2. `Engine` / `Queue` への明示参照へ置き換える
3. 単体ビルドで回帰がないことを確認する

完了条件:
- `dotnet build src/IndigoMovieManager.Thumbnail.RescueWorker/IndigoMovieManager.Thumbnail.RescueWorker.csproj -c Debug -p:Platform=x64`
  が成功する

現状:
- 完了

### Phase 2: Engine の物理自立

目的:
- `Engine` を別repoから参照できる実体へする

作業:
1. `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj` のリンクコンパイルを解消する
2. `Thumbnail\*.cs` から共有対象コードを `src/IndigoMovieManager.Thumbnail.Engine` 配下へ物理移動する
3. UI依存が混ざる場合は `Engine` と `Contracts` に分ける

注意:
- 名前空間は極力維持し、呼び出し側の破壊を避ける
- 先にファイルの置き場所を正し、ロジック改変は最小に抑える

完了条件:
- `Engine` が repo ルート外のソースをリンクせずにビルドできる

### Phase 3: worker 境界の固定

目的:
- worker repo へ持っていく責務を固定する

作業:
1. `RescueWorkerApplication` の外部契約を文書化する
2. `ThumbnailRescueWorkerLauncher` が期待する引数と出力を固定する
3. `FailureDb` の使用列を worker 観点で固定する

固定対象:
- `--main-db`
- `--thumb-folder`
- `--attempt-child`
- `result json` のフォーマット
- `rescue worker stdout/stderr` ログ行

完了条件:
- 本体repoとworker repoが、ソース共有ではなく契約共有で接続できる

### Phase 4: 配布方式の切り替え

目的:
- launcher が「同じrepoのbinを探す」状態を抜ける

作業:
1. worker publish 出力を zip または固定フォルダ構成で作る
2. `ThumbnailRescueWorkerLauncher` の探索順を「環境変数 > 配布物フォルダ > 開発用ローカル」に整理する
3. session copy は維持しつつ、入力を publish 成果物ベースに寄せる

完了条件:
- worker が別repoで publish された成果物だけで起動できる

現状:
- 一部進行済み
- `Publish-RescueWorkerArtifact.ps1` と Private repo 側 `create_rescue_worker_artifact_package.ps1` を追加済み
- `ThumbnailRescueWorkerLaunchSettingsFactory` は `compatibilityVersion` 一致の publish artifact を bin より優先して採用する
- Private repo 側 `private-engine-publish.yml` で CI 生成も追加済み
- Public root から worker artifact 個別生成 script は外し、Private repo 側を正本にした
- ただし app release 側との version 連携はまだ未完了

### Phase 5: 別リポジトリ切り出し

目的:
- worker host を独立repoとして運用開始する

作業:
1. worker 用 solution / CI / publish 手順を新repoへ作る
2. 本体repoから worker プロジェクト参照を外す
3. 本体repoは「成果物を使う側」へ切り替える

完了条件:
- 本体repoに worker ソースが無くても rescue 起動と live 確認が通る

## 6. 今やってはいけないこと

1. `RescueWorker` を in-proc DLL として本体へ戻す
2. `Engine` のリンクコンパイルが残ったまま repo だけ分ける
3. launcher の探索をローカル絶対パスへ固定する
4. `FailureDb` 契約が揺れている段階で repo を分ける

## 7. リスク

### 7.1 高

- `Engine` 内のリンクソースを剥がす時に、UI寄りの責務が混ざっていると分離が止まる
- 別repo化後に worker の publish 物と本体側期待バージョンがずれる

### 7.2 中

- テストが project reference 前提のままだと CI を組み替えにくい
- live 確認手順が repo 分離後に古くなる

## 8. 次の具体アクション

1. `Implementation Plan_独自repo化_ファイル単位仕分け_2026-03-17.md` で、一次対象 / 二次対象 / 本体残置を固定する
2. `QueueObj -> ThumbInfo -> Tools -> ThumbnailCreationService -> ThumbnailQueueProcessor` の順で自前化する
3. `ThumbnailRescueWorkerLauncher` は本体repo残置の host 責務として維持し、worker 側は artifact 消費前提の契約に寄せる

## 9. 参照ファイル

- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/Implementation Plan_独自repo化_ファイル単位仕分け_2026-03-17.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/IndigoMovieManager.Thumbnail.RescueWorker.csproj`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj`
- `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
