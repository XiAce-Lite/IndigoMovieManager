# AI向け レビュー結果 Q6d JpegMetadataRepairDrift解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `jpeg metadata repair drift` は、WB 互換メタ serializer が最新 `infoBuffer` と旧 `CaptureSeconds` を混読していたことが原因だった
- clean worktree では `WhiteBrowserThumbInfoSerializer.cs` 1 ファイルだけで修正し、レビュー専任役 `findings なし` を確認した
- main 側の同一ファイルは未汚染だったため、本線 commit `1304dfe9c617e7e073f0009bd32ab4d2ebe69fc4` で取り込んだ

## 1. 対象

- `src/IndigoMovieManager.Thumbnail.Engine/Compatibility/WhiteBrowserThumbInfoSerializer.cs`

## 2. 実行場所

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q6d-jpeg-metadata-repair`

## 3. root cause

- `WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(...)`
  - `infoBuffer` は末尾の最新追記分を読む
  - しかし `CaptureSeconds` は JPEG 終端直後の最初の旧メタ列から拾っていた
- その結果
  - `ThumbWidth/Height` は新値
  - `CaptureSeconds` だけ旧値 `9`
  となり、repair 後も spec 一致判定に失敗していた

## 4. 着地

- `CaptureSeconds` も末尾の最新メタ列から読むように統一した
- `thumbCount` を使って、末尾 footer と秒数列の位置を直接読む helper へ整理した
- `null thumbInfo policy` と `TryDeleteIncompleteJpeg(...)` には触れていない

## 5. 検証

- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ThumbnailJpegMetadataWriterTests"`
  - 成功
  - `5` 件合格
- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "(FullyQualifiedName~ThumbInfoCompatibilityTests|FullyQualifiedName~ThumbnailJpegMetadataWriterTests)"`
  - 成功
  - `10` 件合格
- `git diff --check`
  - 成功

## 6. レビュー結果

- レビュー専任役
  - `findings なし`
- 調整役判断
  - 受け入れ

## 7. 残留リスク

- 「複数回追記された JPEG で末尾の最新メタを読む」契約は、今は `ThumbnailJpegMetadataWriterTests` 経由の間接確認が中心
- serializer 単体で同契約を固定するテストがあると、さらに硬くなる

## 8. 本線取り込み結果

- 本線 commit
  - `1304dfe9c617e7e073f0009bd32ab4d2ebe69fc4`
  - `jpegメタ修復で最新追記メタを優先する`
- 取り込み対象
  - `src/IndigoMovieManager.Thumbnail.Engine/Compatibility/WhiteBrowserThumbInfoSerializer.cs`
