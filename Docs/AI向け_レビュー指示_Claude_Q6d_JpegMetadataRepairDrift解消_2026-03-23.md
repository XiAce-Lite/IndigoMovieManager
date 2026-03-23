# AI向け レビュー指示 Claude Q6d JpegMetadataRepairDrift解消 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `jpeg metadata repair drift` の修正が、WB 互換メタ serializer の読み取り不整合だけを直し、null policy や別論点へ広がっていないかを review する

## 2. 対象

- `src/IndigoMovieManager.Thumbnail.Engine/Compatibility/WhiteBrowserThumbInfoSerializer.cs`
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailJpegMetadataWriterTests.cs`
- 参照
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailJpegMetadataWriter.cs`

## 3. 必須観点

1. repair 後に最新メタの `CaptureSeconds` を読む契約になっているか
2. failing test を消すだけで閉じていないか
3. null `thumbInfo` policy や incomplete jpeg delete policy を混ぜていないか
4. unrelated change が混ざっていないか

## 4. 受け入れ条件

- findings first
- repair drift に閉じている
- 変更ファイル帯が最小
