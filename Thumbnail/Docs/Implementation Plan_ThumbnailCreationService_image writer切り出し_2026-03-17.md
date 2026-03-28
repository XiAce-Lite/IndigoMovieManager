# ThumbnailCreationService image writer切り出し

更新日: 2026-03-17

## 背景

- `ThumbnailCreationService` の後半には、結果保存そのものとは別に
  - JPEG 保存の retry / temp file / atomic replace
  - 複数フレームの結合保存
  - near-black reject 判定
  が残っていた
- A5 の目的は orchestration を薄くすることなので、この 3 つは service 本体から外す

## 今回の方針

1. JPEG 保存まわりを `ThumbnailImageWriter` へ集約する
2. near-black 判定を `ThumbnailNearBlackDetector` へ集約する
3. `ThumbnailCreationService` の static helper は後方互換 wrapper として残す
4. 実利用箇所は新 helper を直接呼ぶ

## 変更点

### 1. `ThumbnailImageWriter`

- `SaveCombinedThumbnail`
- `TrySaveJpegWithRetry`
- JPEG save gate
- temp path 生成
- atomic replace

を移動した。

### 2. `ThumbnailNearBlackDetector`

- `IsNearBlackBitmap`
- `IsNearBlackImageFile`

を移動した。

### 3. 呼び出し側の整理

- `ThumbnailCreationService`
  - engine 出力 reject を detector へ委譲
  - static helper は wrapper 化
- `ThumbnailFailurePlaceholderWriter`
  - placeholder 保存を `ThumbnailImageWriter` へ委譲
- `FrameDecoderThumbnailGenerationEngine`
  - 結合保存を `ThumbnailImageWriter` へ委譲
- `FfmpegAutoGenThumbnailGenerationEngine`
  - near-black 判定と JPEG 保存を新 helper へ委譲

## テスト

- `ThumbnailAspectRatioTests`
  - near-black 判定テストを detector 直呼びに更新
- `ThumbnailImageWriterTests`
  - 2x2 JPEG 保存
  - 空パス validation

## 残り

- `ThumbnailCreationService` の `CreateThumbAsync` には、まだ result 組み立てと engine 実行ループの判断が残っている
- 次段では preview / result assembly か、engine loop の session 状態をさらに別型へ寄せる
