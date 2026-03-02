# Implementation Plan（Phase 1 詳細: サムネイル作成エンジン別プロジェクト化 2026-03-03）

## 今回の変更ファイルリスト（Phase 1）
- 新規（プロジェクト）
  - [x] `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj`
  - [x] `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/IVideoMetadataProvider.cs`
  - [x] `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/IThumbnailLogger.cs`
  - [x] `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/NoOpVideoMetadataProvider.cs`
  - [x] `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/NoOpThumbnailLogger.cs`
  - [x] `src/IndigoMovieManager.Thumbnail.Engine/Abstractions/ThumbnailRuntimeLog.cs`
  - [x] `src/IndigoMovieManager.Thumbnail.Engine/Properties/InternalsVisibleTo.Tests.cs`
- 新規（App側Adapter）
  - [x] `Thumbnail/Adapters/AppVideoMetadataProvider.cs`
  - [x] `Thumbnail/Adapters/AppThumbnailLogger.cs`
- 移設対象（Thumbnail -> Engine）
  - [x] `Thumbnail/ThumbnailCreationService.cs`
  - [x] `Thumbnail/ThumbnailEnvConfig.cs`
  - [x] `Thumbnail/ThumbnailPathResolver.cs`
  - [x] `Thumbnail/ThumbnailParallelController.cs`
  - [x] `Thumbnail/Tools.cs`
  - [x] `Thumbnail/QueueObj.cs`
  - [x] `Thumbnail/TabInfo.cs`
  - [x] `Thumbnail/ThumbInfo.cs`
  - [x] `Thumbnail/Engines/*.cs`
  - [x] `Thumbnail/Decoders/*.cs`
- 更新（参照切替）
  - [x] `IndigoMovieManager_fork.csproj`
  - [x] `MainWindow.xaml.cs`
  - [x] `Thumbnail/MainWindow.ThumbnailCreation.cs`（変更不要を確認）
  - [x] `Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj`

## 0. 運用ルール（タスクリスト兼用）
- このドキュメントは実装計画と進捗チェックを兼ねる。
- 各チェック項目は完了時に `- [x]` へ更新する。

## 1. この計画の目的
- 第1段階（エンジン本体の別プロジェクト化）を、実装者が迷わず進められる粒度に分解する。
- 第2段階へ影響を残さないよう、境界・責務・完了条件を固定する。

## 2. Phase 1 の到達点
- 新規プロジェクト `IndigoMovieManager.Thumbnail.Engine` を追加し、サムネイル生成エンジン本体を移設する。
- 既存アプリは新プロジェクトを参照し、UI機能（手動サムネ、通常キュー経由の生成）を維持する。
- QueueDB/Persister/Consumer は移設しない（現状維持）。

## 2.1 おすすめ方針（確定）
- [x] Phase 1 は「アセンブリ分離のみ」を実施し、namespace は変更しない（`IndigoMovieManager.Thumbnail` を維持）。
- [x] namespace 変更は Phase 2 以降で一括実施する（`...Thumbnail.Engine` / `...Thumbnail.Queue`）。
- [ ] コミットは「分離」と「namespace rename」を分離し、切り戻し可能性を確保する。

## 3. 対象スコープ

### 3.1 移設対象（IN）
- `Thumbnail/ThumbnailCreationService.cs`
- `Thumbnail/ThumbnailEnvConfig.cs`
- `Thumbnail/ThumbnailPathResolver.cs`
- `Thumbnail/ThumbnailParallelController.cs`
- `Thumbnail/Tools.cs`
- `Thumbnail/QueueObj.cs`
- `Thumbnail/TabInfo.cs`
- `Thumbnail/ThumbInfo.cs`
- `Thumbnail/Engines/*.cs`
- `Thumbnail/Decoders/*.cs`

### 3.2 非対象（OUT）
- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
- `Thumbnail/QueueDb/*.cs`
- `Thumbnail/QueuePipeline/*.cs`
- `Thumbnail/ThumbnailQueueProcessor.cs`
- 画面UI制御（WPF依存）

## 4. 先に解消する技術課題
- [x] `ThumbnailCreationService` の `MovieInfo` 直依存を切る。
- [x] `DebugRuntimeLog` 直依存を切る。
- [x] `ThumbInfo` の `MessageBox` 依存を切る。

## 5. 実装方針

### 5.1 プロジェクト構成
- 新規ディレクトリ: `src/IndigoMovieManager.Thumbnail.Engine/`
- 新規csproj: `IndigoMovieManager.Thumbnail.Engine.csproj`
- 参照パッケージ（最小）
  - `FFMediaToolkit`
  - `FFmpeg.AutoGen`
  - `OpenCvSharp4.Windows`
  - `OpenCvSharp4.Extensions`
  - `System.Drawing.Common`
  - `System.IO.Hashing`

### 5.2 境界インターフェース
- `IVideoMetadataProvider`
  - `TryGetVideoCodec(string moviePath, out string codec)`
  - `TryGetDurationSec(string moviePath, out double durationSec)`
- `IThumbnailLogger`
  - `LogDebug(string category, string message)`
  - `LogInfo(string category, string message)`
  - `LogWarning(string category, string message)`
  - `LogError(string category, string message)`

### 5.3 依存注入ルール
- `ThumbnailCreationService` はコンストラクタで `IVideoMetadataProvider` と `IThumbnailLogger` を受け取る。
- 既存呼び出し互換のため、引数なしコンストラクタは「NoOp実装」を内部採用する。
- App側からは既存 `MovieInfo` と `DebugRuntimeLog` に接続するAdapterを渡す。

## 6. 作業ブロック（実施順固定）

### Block A: 受け皿作成
- [x] 新規プロジェクト作成（csproj, AssemblyInfo相当）。
- [x] Phase 1 中は既存 namespace（`IndigoMovieManager.Thumbnail`）を維持する。

### Block B: 共通モデル移設
- [x] `QueueObj`, `TabInfo`, `ThumbInfo` を移設。
- [x] `ThumbInfo` の `MessageBox` を廃止し、例外またはエラー戻り値へ変更。

### Block C: エンジン移設
- [x] `Engines`/`Decoders` を移設。
- [x] `internal/public` 可視性を見直し（テスト参照分は `public` 化を必要最小で実施）。

### Block D: サービス移設
- [x] `ThumbnailCreationService` を移設。
- [x] `MovieInfo`, `DebugRuntimeLog` 依存をインターフェース経由へ変更。

### Block E: App側Adapter実装
- [x] `IndigoMovieManager_fork` 側にAdapterを追加。
- [x] 既存の呼び出しを新サービスインスタンスへ差し替え。

### Block F: 参照切替
- [x] `IndigoMovieManager_fork.csproj` に `ProjectReference` 追加。
- [x] 旧 `Thumbnail` 配下の重複コードを除去。

### Block G: テスト追従
- [x] `Tests/IndigoMovieManager_fork.Tests` を新プロジェクト参照へ追従。
- [x] `InternalsVisibleTo` または公開APIのどちらでテストを成立させるか固定。

### Block H: 回帰確認
- [x] 出力サムネイル形式（ファイル名、末尾メタ）互換確認。
- [x] エンジン切替（autogen / ffmediatoolkit / ffmpeg1pass / opencv）経路確認。

## 7. 完了条件（Phase 1 DoD）
- [x] `IndigoMovieManager.Thumbnail.Engine` がWPF依存なしでビルドできる。
- [x] MainWindowからの既存生成機能が退行しない。
- [x] 既存テスト（サムネイル関連）が新参照構成で実行可能。
- [x] 旧 `Thumbnail` 配下に二重定義が残っていない。

## 8. リスクと対策
- リスク: API公開範囲が広がりすぎる。
  - 対策: `public` は境界型のみ、実装は原則 `internal`。
- リスク: Adapter層で責務が逆流する。
  - 対策: App側Adapterは「変換と委譲のみ」に限定。
- リスク: 既存テストが内部型依存で破綻する。
  - 対策: テストが使う型だけ明示公開、または `InternalsVisibleTo` を採用。

## 9. ロールバック戦略
- 変更はBlock単位でコミット分割する。
- 破綻時は「最後に完了したBlock」まで戻す。
- `MainWindow` 側呼び出し差し替え（Block E）は単独コミットに分離する。

## 10. 実施結果メモ（2026-03-03）
- `MSBuild`（Debug|Any CPU）: 成功（0警告 / 0エラー）
- `dotnet test --no-build`（Tests/IndigoMovieManager_fork.Tests）: 合格 19 / 失敗 0 / スキップ 1
- 手動回帰チェック手順と実施ログ: `Thumbnail/ManualRegressionCheck_Phase1_サムネイル作成エンジン別プロジェクト化_2026-03-03.md`
- MainWindowの手動操作による実画面回帰（通常サムネ/手動サムネ）: 実施済み（ユーザー実施）
- 実動画ベンチ（autogen / ffmediatoolkit）: 成功（`thumbnail-engine-bench-20260303-033133.csv`）
- ルーター/フォールバック順（ffmpeg1pass / opencv含む）: 回帰テストで確認済み
