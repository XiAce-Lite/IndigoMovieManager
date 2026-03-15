# Implementation Plan（サムネイル作成エンジン別プロジェクト化 2026-03-03）

## 1. 目的
- サムネイル作成機能を別プロジェクトへ分離し、将来的なDLL配布と他アプリ流用を可能にする。
- 既存機能のうち、どこまでを初回分離対象に含めるかを固定する。
- WhiteBrowser互換（サムネイル末尾メタ情報）を維持する。

## 2. 結論（今回の推奨スコープ）
- 初回分離は「サムネイル生成エンジン本体」までに限定する。
- キューDB運用（Producer/Persister/Consumer）は第2段階で分離する。
- 理由: 先にエンジンを独立させることで、UI依存・運用依存を切り離しつつ、機能退行リスクを最小化できる。

## 2.1 おすすめ方針（同期）
- Phase 1 は「アセンブリ分離のみ」を実施し、namespace は変更しない（`IndigoMovieManager.Thumbnail` を維持）。
- namespace 変更は Phase 2 以降で一括実施する（`...Thumbnail.Engine` / `...Thumbnail.Queue`）。
- コミットは「分離」と「namespace rename」を分離し、切り戻し可能性を確保する。

## 3. 現機能の含有範囲

### 3.1 初回分離に含める（Must）
- 生成オーケストレーション
  - `Thumbnail/ThumbnailCreationService.cs`
- 生成エンジン群
  - `Thumbnail/Engines/*.cs`
  - `Thumbnail/Decoders/*.cs`
- サムネイル互換データ
  - `Thumbnail/ThumbInfo.cs`（WhiteBrowser互換の末尾メタ情報）
  - `Thumbnail/TabInfo.cs`
  - `Thumbnail/QueueObj.cs`（後述のDTOへ改名を検討）
- 生成補助
  - `Thumbnail/ThumbnailPathResolver.cs`
  - `Thumbnail/ThumbnailEnvConfig.cs`
  - `Thumbnail/ThumbnailParallelController.cs`
  - `Thumbnail/Tools.cs`（タグ変換等の非エンジン用途は後で分離）

### 3.2 第2段階で含める（Should）
- 非同期キュー運用
  - `Thumbnail/QueueDb/*.cs`
  - `Thumbnail/QueuePipeline/*.cs`
  - `Thumbnail/ThumbnailQueueProcessor.cs`
- 理由
  - 現状 `ThumbnailQueueProcessor` が `Notification.Wpf` に依存し、汎用ライブラリとしては境界が曖昧。

### 3.3 初回分離から除外する（Out）
- UI結合コード
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `Thumbnail/MainWindow.ThumbnailQueue.cs`
- MainWindow内の設定取得・DB更新・Dispatcher反映
- 画面表示ロジック（進捗ダイアログ、MessageBox直接表示）

## 4. 汎用化のための依存切断ポイント

### 4.1 直近で切る依存
- `MovieInfo` 直参照
  - 現状: `ThumbnailCreationService` が `new MovieInfo(...).VideoCodec` に依存。
  - 対応: `IVideoMetadataProvider` を導入し、既定実装をアプリ側で注入。
- `DebugRuntimeLog` 直参照
  - 対応: `IThumbnailLogger`（Debug/Info/Warn/Error）へ置換。
- `ThumbInfo` の `MessageBox` 依存
  - 対応: 例外返却またはログ通知へ変更（UI表示は呼び出し側責務）。

### 4.2 境界インターフェース（最小セット）
- `IThumbnailEngineService`
  - `CreateThumbAsync(...)`
  - `CreateBookmarkThumbAsync(...)`
- `ThumbnailEngineOptions`
  - `IsResizeThumb`
  - `GpuDecodeMode`
  - `EngineOverride`
  - `OutputRoot`
- `IVideoMetadataProvider`
  - `TryGetVideoCodec(...)`
  - `TryGetDurationSec(...)`
- `IThumbnailLogger`
  - `Log(level, category, message)`

## 5. プロジェクト構成案

### 5.1 第1段階（今回）
- 新規: `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj`
- 参照
  - `FFMediaToolkit`
  - `FFmpeg.AutoGen`
  - `OpenCvSharp4.Windows`
  - `OpenCvSharp4.Extensions`
  - `System.Drawing.Common`
  - `System.IO.Hashing`
- App側は ProjectReference で参照し、既存UI経路は温存。

### 5.2 第2段階（次フェーズ）
- 新規: `src/IndigoMovieManager.Thumbnail.Queue/IndigoMovieManager.Thumbnail.Queue.csproj`
- `QueueDb/QueuePipeline/Processor` を移設し、進捗通知をコールバック化。

## 6. 実装ステップ

### Phase 1: 土台作成
- 新規プロジェクト `Thumbnail.Engine` 作成。
- `Thumbnail` 配下のエンジン関連ファイルを移設。
- Phase 1 中は既存 namespace（`IndigoMovieManager.Thumbnail`）を維持する。

### Phase 2: 依存切断
- `MovieInfo` 依存を `IVideoMetadataProvider` へ置換。
- `DebugRuntimeLog` 依存を `IThumbnailLogger` へ置換。
- `ThumbInfo` の `MessageBox` を除去。

### Phase 3: App接続
- `MainWindow` 側で新エンジンサービスをDI/生成。
- 既存の `CreateThumbAsync` 呼び出し経路を新APIへ差し替え。
- ベンチ/回帰テストを新プロジェクト参照へ更新。

### Phase 4: 安定化
- 既存ベンチ（engine比較）を再実行し性能劣化がないことを確認。
- ログCSV互換、サムネイル出力互換（ファイル名・メタ情報）を確認。

## 7. 完了条件（DoD）
- `Thumbnail.Engine` 単体でビルド可能（WPF参照なし）。
- WhiteBrowser互換メタ付きサムネイルを従来と同形式で出力できる。
- 既存4エンジン（autogen / ffmediatoolkit / ffmpeg1pass / opencv）の切替・フォールバックが維持される。
- MainWindow側の機能退行（手動サムネイル、詳細サムネイル、通常キュー処理）がない。

## 8. リスクと対策
- `System.Drawing` の実行環境差異
  - 対策: Windows前提を明文化し、将来はImageSharp等へ差し替え可能な抽象を追加。
- ffmpeg shared DLL 配置依存
  - 対策: 解決順（ENV -> 同梱パス）を共通化し、起動時診断ログを追加。
- API変更に伴うテスト破損
  - 対策: 先に `Tests/IndigoMovieManager_fork.Tests` の参照先だけ新プロジェクトへ変更して回帰を固定。

## 9. 今回の判断メモ
- 「汎用化」の最短距離は、まずエンジン本体だけを独立させること。
- キュー運用は価値が高いが、UI通知と運用ルール依存が強いため次段に回す。
- この順序なら、機能維持と将来DLL化の両方を現実的なコストで進められる。
