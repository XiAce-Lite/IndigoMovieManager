# Implementation Plan 独自repo化 ファイル単位仕分け 2026-03-17

最終更新日: 2026-03-17

変更概要:
- あなた独自の別repoを作る前提で、現worktreeをファイル単位で仕分けした
- `今すぐ独自repoの所属とみなすもの`、`書き換え後に移すもの`、`本体repoに残すもの` を固定した
- `Runtime は本体repo側の host 基盤に残す` 方針を明記した

## 1. 目的

`IndigoMovieManager` から、救済worker系をあなた独自の別repoへ育てる時に、
どのファイルを先に持っていき、どのファイルはまだ本体repoに残すかを固定する。

ここでの判断軸は次の3つである。

1. 体感テンポを壊さないこと
2. 現時点での独自性の強さ
3. 物理分離した時に build / publish が破綻しないこと

## 2. 結論

結論は次である。

1. 独自repo化は進めてよい
2. ただし初期の見せ方は `完全新規実装` ではなく `派生を再設計した独自拡張` に留める
3. `Runtime`、launcher、本体 release package は本体repoに残す
4. `Worker / FailureDb / artifact packaging / worker CI` を独自repoの一次対象とする
5. `ThumbnailCreationService` などの中核 5 本は、書き換え後に二次対象として移す

## 3. 仕分けルール

### 3.1 独自repoの一次対象

次は「今の時点で独自repoの所属とみなしてよい」層である。
物理移管の順番は後でもよいが、責務の持ち主はここで固定する。

#### worker host 本体

- `src/IndigoMovieManager.Thumbnail.RescueWorker/IndigoMovieManager.Thumbnail.RescueWorker.csproj`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Program.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerHostServices.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Publish-RescueWorkerArtifact.ps1`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/RescueWorker_外部接続仕様_2026-03-17.md`

#### worker 配布と CI

- `scripts/create_rescue_worker_artifact_package.ps1`
- `.github/workflows/rescue-worker-artifact.yml`
- `src/IndigoMovieManager.Thumbnail.Contracts/RescueWorkerArtifactContract.cs`

#### worker が共有する管理基盤

- `src/IndigoMovieManager.Thumbnail.FailureDb/ThumbnailFailureDbPathResolver.cs`
- `src/IndigoMovieManager.Thumbnail.FailureDb/ThumbnailFailureDbSchema.cs`
- `src/IndigoMovieManager.Thumbnail.FailureDb/ThumbnailFailureDbService.cs`
- `src/IndigoMovieManager.Thumbnail.Contracts/FailureDb/ThumbnailFailureRecord.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryContracts.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/AdminTelemetryRuntimeResolver.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcDtos.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcTransportPolicy.cs`
- `src/IndigoMovieManager.Thumbnail.Contracts/ThumbnailQueueHostPathPolicy.cs`

#### engine のうち独自色が強い周辺層

- `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/IThumbnailLogger.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/IVideoMetadataProvider.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/NoOpThumbnailLogger.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/NoOpVideoMetadataProvider.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/ThumbnailRuntimeLog.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateProcessLogWriter.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationHostRuntime.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailEngineRuntimeStats.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailRescueTraceLog.cs`

### 3.2 書き換え後に独自repoへ移す二次対象

次は「別repoの核にしたいが、今そのまま持ち出すと本家の系譜が強く見える」層である。
独自repoをあなた名義で強く押し出したいなら、ここを先に自前化する。

#### 優先度A

- `Thumbnail/ThumbnailCreationService.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `Thumbnail/ThumbInfo.cs`
- `Thumbnail/Tools.cs`
- `src/IndigoMovieManager.Thumbnail.Contracts/QueueObj.cs`

#### 優先度A の固定順

書き換え順は次で固定する。

1. `src/IndigoMovieManager.Thumbnail.Contracts/QueueObj.cs`
2. `Thumbnail/ThumbInfo.cs`
3. `Thumbnail/Tools.cs`
4. `Thumbnail/ThumbnailCreationService.cs`
5. `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`

理由:

- `QueueObj` はすでに `ThumbnailRequest` facade 化が入っており、最初に legacy 名から実質卒業しやすい
- `ThumbInfo` は WhiteBrowser 互換メタ情報の単独責務で、サービス本体より先に契約化しやすい
- `Tools` は静的 utility の寄せ集めなので、`ThumbnailCreationService` を軽くする前に分解しておく方が安全である
- `ThumbnailCreationService` は入力契約、メタ情報、utility が揃ってからでないと分解コストが跳ねる
- `ThumbnailQueueProcessor` は queue DB / lease / failure DB / createThumb callback の交点なので最後に触る

#### 優先度B

- `Thumbnail/ThumbnailEnvConfig.cs`
- `Thumbnail/ThumbnailPathResolver.cs`
- `Thumbnail/Decoders/**/*.cs`
- `Thumbnail/Engines/**/*.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailLayoutProfile.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbRootResolver.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailDetailModeRuntime.cs`

補足:

- `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj` は、まだ `Thumbnail/**/*.cs` をリンクコンパイルしている
- したがって `Engine project を丸ごと独自repoへ移す` のは、優先度A/B の自前化が進んだ後である

### 3.3 本体repoに残すもの

次は `Runtime は本体repo側の host 基盤` という現在方針に従い、本体repoへ残す。

#### host runtime

- `src/IndigoMovieManager.Thumbnail.Runtime/AppLocalDataPaths.cs`
- `src/IndigoMovieManager.Thumbnail.Runtime/DefaultThumbnailCreateProcessLogWriter.cs`
- `src/IndigoMovieManager.Thumbnail.Runtime/DefaultThumbnailCreationHostRuntime.cs`
- `src/IndigoMovieManager.Thumbnail.Runtime/IndigoMovieManager.Thumbnail.Runtime.csproj`

#### launcher と app 統合

- `Thumbnail/ThumbnailRescueWorkerLauncher.cs`
- `Thumbnail/ThumbnailRescueWorkerLaunchSettings.cs`
- `Thumbnail/ThumbnailRescueWorkerLaunchSettingsFactory.cs`
- `Thumbnail/MainWindow.ThumbnailRescueWorkerLauncher.cs`

#### app release package

- `scripts/create_github_release_package.ps1`
- `.github/workflows/github-release-package.yml`
- `Docs/GitHubRelease_実行可能バイナリ配布手順_2026-03-15.md`
- `Docs/Implementation Plan_release package_worker期待version固定_2026-03-17.md`

理由:

- ここは「本体が worker artifact をどう選び、どう配布に載せるか」という host 責務である
- worker 独自repoへ持っていくと、本体側 release 都合に引っ張られて責務が濁る

### 3.4 移さないもの

次は独自repoの初期移管対象から外す。

- WPF 本体 UI
- Watcher
- 通常 `QueueDb`
- `.local` 配下の資料と実データ

特に `.local` は判断材料であって、公開repoの中身には入れない。

## 4. 今の物理制約

2026-03-17 時点の物理制約は次である。

1. `RescueWorker` は `Runtime` を直接参照していない
2. `Queue` も `Runtime` を直接参照していない
3. ただし `Engine.csproj` はまだ `Thumbnail/**/*.cs` のリンクコンパイルを含む
4. よって「worker 単独repoの即日完全切り出し」ではなく、「所属先を先に固定し、危ない核だけ書き換えてから物理分離」が現実解である

## 5. 実施順

1. この仕分けを前提に、独自repoの README 表現を `派生を再設計した独自拡張` に固定する
2. 優先度A の 5 本を `QueueObj -> ThumbInfo -> Tools -> ThumbnailCreationService -> ThumbnailQueueProcessor` の順で自前実装へ置き換える
3. `Engine.csproj` のリンクコンパイルをゼロにする
4. `FailureDb` と worker host 一式を独自repoへ物理移管する
5. 本体repoは worker artifact 消費側へ切り替える

## 6. 成功条件

成功は次で判断する。

1. 独自repoの看板が `worker / FailureDb / artifact build / worker CI` で立つ
2. 本体repoは `Runtime / launcher / app release package` に責務集中できる
3. 危ない中核 5 本を書き換えた後に、`Engine / Contracts` を自前色で再編できる

## 7. 参照

- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/Implementation Plan_救済worker系外部リポジトリ化_長期計画_2026-03-16.md`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/Docs/Implementation Plan_救済worker別リポジトリ化_2026-03-16.md`
