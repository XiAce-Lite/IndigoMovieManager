# サムネイル処理ドキュメント

## 1. 目的
このドキュメントは、IndigoMovieManager のサムネイル生成処理について、以下を素早く把握できるようにするための実装ガイドです。

- 現在の設計方針
- 生成フロー
- 生成トリガー
- GPU/並列設定
- 計測ログの読み方

## 2. 現在の方針（2026-02-24時点）

- デコード経路は `OpenCvSharp` の `VideoCapture` を標準採用。
- `ffmpeg` 分岐は一旦使わない（検証は保留）。
- GPUデコードは「デフォルト ON」。
- GPU ON/OFF は共通設定画面から切替可能。
- 並列実行数は共通設定で `1-24` の範囲で設定可能。

## 3. 主要コンポーネント

- キュー制御
  - `Thumbnail/MainWindow.ThumbnailQueue.cs`
- キュー実行（並列バッチ）
  - `Thumbnail/ThumbnailQueueProcessor.cs`
- サムネイル本体生成
  - `Thumbnail/ThumbnailCreationService.cs`
- メイン画面側の連携
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `MainWindow.xaml.cs`

## 4. 生成フロー

1. 各トリガーで `QueueObj` を作成し、キューへ投入。
2. 常駐タスク `CheckThumbAsync` がキューを監視。
3. `ThumbnailQueueProcessor.RunAsync` がバッチ化して並列実行。
4. 各ジョブで `ThumbnailCreationService.CreateThumbAsync` を実行。
5. 生成結果のファイルパスを `MovieRecords` に反映。
6. 必要時に動画長を DB へ反映。

## 5. サムネイル生成トリガー

### 5.1 キュー投入トリガー

- 全件再生成（メニュー）
  - `MainWindow.MenuActions.cs`
- 全件再生成（ツール）
  - `MainWindow.MenuActions.cs`
- 選択変更時（`ThumbDetail` が `error`）
  - `MainWindow.Selection.cs`
- フォルダ監視で新規ファイル検知
  - `MainWindow.Watcher.cs`
- フォルダ手動チェックで新規検知
  - `MainWindow.Watcher.cs`
- 等間隔サムネイル再生成（選択対象）
  - `Thumbnail/MainWindow.ThumbnailCreation.cs`

### 5.2 直接実行トリガー（キューを通さない）

- プレイヤー画面の手動サムネイル更新
  - `MainWindow.Player.cs`
- ブックマーク用サムネイル生成
  - `MainWindow.Player.cs`

## 6. 並列制御と重複抑止

- 並列実行
  - `Parallel.ForEachAsync` を利用。
  - 並列数は `ThumbnailParallelism`（1-24）。
- 重複抑止
  - `MovieId:Tabindex` をキーに重複投入を抑止。
  - 実装: `queuedThumbnailKeys`。
- 出力ファイル衝突対策
  - 出力パス単位で `SemaphoreSlim` による排他。

## 7. 生成アルゴリズム（CreateThumbAsync）

- 出力先を決定し、動画存在チェック。
- 動画長を取得（優先: frameCount/fps、失敗時は Shell フォールバック）。
- パネル数（columns x rows）に応じて等間隔秒を算出。
- 各秒位置のフレームを読み出し、アスペクト補正してリサイズ。
- フレーム群をメモリ上で結合して JPEG 出力。
- 末尾に `ThumbInfo` メタデータを追記。

関連:
- `Thumbnail/ThumbnailCreationService.cs`
- `Thumbnail/ThumbInfo.cs`
- `Thumbnail/TabInfo.cs`

## 8. GPU 設定

### 8.1 既定値

- 設定名: `ThumbnailGpuDecodeEnabled`
- 既定値: `True`（GPUデコード有効）

定義:
- `Properties/Settings.settings`
- `Properties/Settings.Designer.cs`
- `App.config`

### 8.2 画面設定

- 共通設定画面で ON/OFF 可能。
- `CommonSettingsWindow.xaml`
- `CommonSettingsWindow.xaml.cs`

### 8.3 実行時反映

- 起動時に設定値を `IMM_THUMB_GPU_DECODE` へ反映。
- 共通設定画面を閉じた後も再反映。
- 実装:
  - `MainWindow.xaml.cs`
  - `MainWindow.MenuActions.cs`

### 8.4 OpenCV 側への橋渡し

- `IMM_THUMB_GPU_DECODE=cuda` のときのみ、
  `OPENCV_FFMPEG_CAPTURE_OPTIONS=hwaccel;cuda|hwaccel_output_format;cuda` を設定。
- `off` のときは、このアプリが設定した値のみクリア。
- 実装:
  - `Thumbnail/ThumbnailCreationService.cs`

## 9. ログと計測

- バッチ完了ごとにサマリログを出力。
- 形式:
  - `thumb queue summary: gpu=..., parallel=..., batch_count=..., batch_ms=..., total_count=..., total_ms=...`
- 実装:
  - `Thumbnail/ThumbnailQueueProcessor.cs`

補足:
- `IMM_THUMB_FILE_LOG=1` のとき、`%LOCALAPPDATA%\IndigoMovieManager\logs\thumb_decode.log` に追記。

## 10. 運用メモ

- 実測では、GPU ON で速度差が小さいケースでも CPU 負荷低減に効果がある。
- 並列数は環境差が大きいので、`8` と `12` を基準に比較するのが無難。
- 速度比較の生データは以下を参照。
  - `Docs/サムネイル生成速度比較.txt`