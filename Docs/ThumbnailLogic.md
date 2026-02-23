# サムネイル処理ドキュメント

## 目的
このドキュメントは、IndigoMovieManager の現在のサムネイル処理について、以下を整理するためのものです。

- 現在のサムネイル処理ロジック（生成フロー）
- 実装済みの高速化ポイント
- サムネイル作成トリガー一覧

## 現在の処理ロジック

### 全体フロー
1. 各UI/監視イベントからサムネイル作成ジョブ（`QueueObj`）を投入する。  
   `Thumbnail/MainWindow.ThumbnailQueue.cs`
2. `CheckThumbAsync` がキューを監視し、`ThumbnailQueueProcessor.RunAsync` でバッチ並列実行する。  
   `Thumbnail/MainWindow.ThumbnailCreation.cs`
3. 各ジョブで `ThumbnailCreationService.CreateThumbAsync` を呼び、画像を生成する。  
   `Thumbnail/ThumbnailCreationService.cs`
4. 生成後、UI側の `MovieRecords` にサムネイルパスを反映し、必要に応じて動画長をDB更新する。  
   `Thumbnail/MainWindow.ThumbnailCreation.cs`

### キュー投入と重複抑止
- キー: `(MovieId, Tabindex)` を `MovieId:Tabindex` 文字列化して管理。
- 同じキーがキュー内にある間は再投入しない。
- 完了時にキー解放、タスク再起動時はキューとキーをクリア。

対象:
- `TryEnqueueThumbnailJob` / `ReleaseThumbnailJob` / `ClearThumbnailQueue`  
  `Thumbnail/MainWindow.ThumbnailQueue.cs`

### 並列処理と進捗表示
- `ThumbnailQueueProcessor` はキューからバッチを取り出し、`Parallel.ForEachAsync` で並列実行。
- 並列数は設定値（1〜24）を利用。
- 進捗バー表示あり（Notification.Wpf）。

対象:
- `RunAsync`  
  `Thumbnail/ThumbnailQueueProcessor.cs`
- 並列数取得: `GetThumbnailQueueMaxParallelism`  
  `MainWindow.xaml.cs`

### サムネイル生成（CreateThumbAsync）
主な処理:
- 出力先の競合回避（出力ファイル単位ロック）
- 動画ハッシュ取得（キャッシュあり）
- 動画長取得（`frameCount/fps` 優先、無効時のみ Shell フォールバック）
- サムネイル時刻の決定（自動等間隔 or 手動置換）
- フレーム抽出・トリミング・リサイズ
- 中間JPEGを作らず、メモリ上で結合して最終JPEGを1回保存
- 末尾に `ThumbInfo` メタデータ追記

対象:
- `Thumbnail/ThumbnailCreationService.cs`
- `Thumbnail/ThumbInfo.cs`
- `Thumbnail/TabInfo.cs`

## 実装済み高速化ポイント

### 1. 並列処理（設定画面で可変）
- 並列実行数を 1〜24 で設定可能。
- 設定項目: `ThumbnailParallelism`（既定値 4）

対象:
- `CommonSettingsWindow.xaml`
- `CommonSettingsWindow.xaml.cs`
- `Properties/Settings.settings`
- `MainWindow.xaml.cs`

### 2. 重複投入抑止
- `(MovieId, Tabindex)` 単位の重複投入を抑止。
- 無駄な再生成ジョブを削減。

対象:
- `Thumbnail/MainWindow.ThumbnailQueue.cs`

### 3. 出力競合回避
- 同一出力ファイルへの同時書き込みを `SemaphoreSlim` で直列化。

対象:
- `Thumbnail/ThumbnailCreationService.cs`

### 4. 中間JPEG廃止（メモリ結合化）
- 以前の「一時JPEG保存→再読込結合」を廃止。
- `Mat` をメモリ保持し、最終画像のみ保存。

対象:
- `Thumbnail/ThumbnailCreationService.cs`

### 5. duration取得の軽量化
- `frameCount/fps` を優先。
- 不正値時のみ Shell COM 参照へフォールバック。

対象:
- `Thumbnail/ThumbnailCreationService.cs`

### 6. ハッシュ/長さキャッシュ
- キー: `path|size|lastWriteTimeUtcTicks`
- 再処理時の計算コスト削減。

対象:
- `Thumbnail/ThumbnailCreationService.cs`

### 7. GPUデコード検証実装
- 環境変数 `IMM_THUMB_GPU_DECODE` でモード切替。
  - `off`, `auto`, `cuda`, `d3d11va`, `dxva2`, `qsv`
- 失敗時は CPU へフォールバック。
- 検証ログを Debug 出力 + ファイルへ出力。

対象:
- `Thumbnail/ThumbnailCreationService.cs`

### 8. GPU比較向け集計ログ
- バッチ単位: `batch_count`, `batch_ms`
- 累計: `total_count`, `total_ms`
- `gpu` と `parallel` も同時出力。

対象:
- `Thumbnail/ThumbnailQueueProcessor.cs`

ログ保存先:
- `%LOCALAPPDATA%\\IndigoMovieManager\\logs\\thumb_decode.log`

## サムネイル作成トリガー一覧（現行）

### 通常サムネイル（9経路）
1. 全件再作成ボタン  
   `MainWindow.MenuActions.cs`
2. ツール「全ファイルサムネイル再作成」  
   `MainWindow.MenuActions.cs`
3. 一覧選択時（詳細サムネが error）  
   `MainWindow.Selection.cs`
4. タブ切替時（現在タブの error サムネ）  
   `MainWindow.xaml.cs`
5. タブ切替時（ThumbDetail が error）  
   `MainWindow.xaml.cs`
6. 監視フォルダ: 新規ファイル検知  
   `MainWindow.Watcher.cs`
7. 監視フォルダ: 更新チェック結果の追加分  
   `MainWindow.Watcher.cs`
8. 手動等間隔サムネ作成（複数選択）  
   `Thumbnail/MainWindow.ThumbnailCreation.cs`
9. 手動キャプチャ確定（直接生成）  
   `MainWindow.Player.cs`

### ブックマークサムネイル（+1経路）
1. ブックマーク追加時の専用サムネ生成  
   `MainWindow.Player.cs`

## GPUあり/なし比較の見方

同一条件で次を比較する。

- `gpu=off` 実行
- `gpu=cuda`（または `auto`）実行

比較指標:
- 同じ処理量（`total_count`）での `total_ms`
- 1件あたり時間: `total_ms / total_count`

注意:
- `gpu=cuda` でも `cpu-fallback` が混ざる場合は、実GPUデコードが使えていない。
- 差が小さい場合、ボトルネックがデコード以外（シーク/リサイズ/保存）である可能性が高い。

## 今後の高速化候補（未実装）

- `ffmpeg` の `hwaccel + filter_complex(tile)` による「1動画=1コマンド」生成
- タブ跨ぎの同一動画ジョブをまとめる（1回デコードで複数サイズ展開）
- 画像保存形式/品質の見直し（品質固定とI/O量の最適化）
