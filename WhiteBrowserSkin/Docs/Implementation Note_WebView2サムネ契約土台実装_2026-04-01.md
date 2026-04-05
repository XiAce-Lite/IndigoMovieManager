# Implementation Note: WebView2サムネ契約土台実装 2026-04-01

最終更新日: 2026-04-01

## 1. 今回入ったもの

- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinThumbnailContracts.cs`
  - `dbIdentity / recordKey / thumbUrl / thumbSourceKind / 寸法 DTO` の契約型を追加
  - `thum.local/__external/...` を含む URL codec を追加
- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinThumbnailContractService.cs`
  - `MovieRecords` からサムネ契約 DTO を組み立てる正本 service を追加
  - `thumbRevision` は `sourceKind + 正規化パス + length + lastWriteUtcTicks` の SHA-256 hex
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailSourceImageImportMarkerHelper.cs`
  - same-name 画像 import 由来かどうかを sidecar marker で保持する helper を追加
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateResultFinalizer.cs`
  - 成功時に import marker を同期する処理を追加
- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinApiService.cs`
  - サムネ解決は `WhiteBrowserSkinThumbnailContractService` を使う形へ寄せた

## 2. 今回固定した実装判断

- `dbIdentity`
  - `NormalizeMainDbPath(DBFullPath)` 相当の結果を UTF-8 化し、SHA-256 hex へ変換する
- `recordKey`
  - `"{dbIdentity}:{movieId}"`
- `thumbUrl`
  - 常に `?rev={thumbRevision}` 付きで返す
  - managed root 配下は `https://thum.local/...`
  - managed root 外は `https://thum.local/__external/...`
- `thumbSourceKind`
  - `managed-thumbnail`
  - `source-image-direct`
  - `source-image-imported`
  - `error-placeholder`
  - `missing-file-placeholder`
- `thumbNaturalWidth / Height`
  - 実画像サイズを優先する
  - WB metadata がある場合も、列行は metadata、natural size は実画像サイズを優先する

## 3. まだ残しているもの

- `MainWindow.WebViewSkin.Api.cs` など host 側の実配線は未着手
- `WhiteBrowserSkinRuntimeBridge` 側の `__external` 実ファイル応答は未接続
  - URL codec は先に固定済み
  - 実際の `WebResourceRequested` 受けは WebView 側作業で接続する

## 4. 確認済みテスト

- `WhiteBrowserSkinThumbnailContractTests`
- `WhiteBrowserSkinThumbnailContractServiceTests`
- `WhiteBrowserSkinApiServiceTests`

合計 18 件が `dotnet test Tests/IndigoMovieManager.Tests/IndigoMovieManager.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~WhiteBrowserSkinThumbnailContractTests|FullyQualifiedName~WhiteBrowserSkinThumbnailContractServiceTests|FullyQualifiedName~WhiteBrowserSkinApiServiceTests"` で成功した。
