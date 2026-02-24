# 絵文字パス4段階対策 詳細設計（実装反映版）

## 1. 対象
- `Thumbnail/ThumbnailCreationService.cs`
- 対象機能: OpenCV/ffmpeg入力パス解決、保存fallback、一時資源クリーンアップ

## 2. 実装方針
- OpenCV/ffmpegともに同じ順序で処理する。
- 1,2で通るケースは3を作らない（遅延作成）。
- 4（コピー）は最終手段とし、3GB超は後回し確認へ回す。

## 3. 入力パス解決フロー
1. `Raw`
2. `ShortPath`
3. `JunctionAlias` / `HardLinkAlias`（1,2失敗時のみ作成）
4. `Copy`（ffmpegの最終手段）

実装上の型:
- `InputPathStage`
- `LibraryInputCandidate`

## 4. OpenCV 経路
- 入口: `SelectOpenCvInputPath`
- 試行:
  - `BuildNoCopyInputCandidates` で `Raw/ShortPath`
  - 失敗時のみ `BuildAliasInputCandidates` で `Junction/HardLink`
- 選択されたパスで `VideoCapture` を実行
- 開けない場合は ffmpegフォールバックへ移行

## 5. ffmpeg 経路
- 入口: `TryCreateThumbByFfmpegAsync`
- 試行:
  - `Raw/ShortPath` を先に試行
  - 失敗時のみ `Junction/HardLink` を試行
  - 全滅時のみ `PrepareCopiedInputPathForFallback`（コピー）
- コピー判定:
  - `3GB超` かつ `未許可` は `Deferred` を返す
  - ユーザー確認は既存キュー後回し機構を利用

## 6. 保存経路（ImWrite）
- 入口: `SaveCombinedThumbnail`
- 直接保存:
  - `HasUnmappableAnsiChar` で危険判定
  - 危険でない場合も `Cv2.ImWrite` 例外時はfallbackへ移行
- fallback保存:
  - 一時ASCIIパスへ保存
  - `.NET File.Move` で最終Unicodeパスへ移動

## 7. ANSI判定とコードページ
- `HasUnmappableAnsiChar` は `Encoding.GetEncoding(ANSICodePage, ExceptionFallback, ExceptionFallback)` で厳密判定する。
- `Encoding 932` 未登録環境対策として、`ThumbnailCreationService` の static ctor で以下を実施する。
  - `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`

## 8. クリーンアップ設計
- `CleanupTempDirectory` を `finally` で必ず実行
- 削除順序:
  1. 直下ファイル削除
  2. 直下ディレクトリ削除
- ジャンクション対策:
  - `FileAttributes.ReparsePoint` を判定し、再解析ポイントは `Directory.Delete(path, false)` で削除
  - リンク先実体は削除しない
- 失敗時は `Debug.WriteLine` に記録し、処理継続

## 9. ログ設計
- 入力試行: `thumb opencv input try`
- 入力採用: `thumb opencv input selected`
- Open失敗: `thumb capture open failed`
- コピー遅延: `thumb ffmpeg fallback deferred`
- 直接保存失敗fallback: `thumb save direct marshal failed` / `thumb save direct failed`
- クリーンアップ失敗: `thumb cleanup ... failed`

## 10. 既知の別件
- `System.Windows.Data Error: 5`（`MaxWidth` が負値）は本対策外（WPFレイアウト側）。
