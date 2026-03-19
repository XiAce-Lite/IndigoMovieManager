# Implementation Plan プレースホルダ追加 NoData AppleDouble Flash 2026-03-20

最終更新日: 2026-03-20

変更概要:
- `No Data` を engine 実行前の precheck で即 placeholder 化する
- AppleDouble シグネチャ `00 05 16 07` を precheck で検出し、`placeholder-appledouble` を返す
- SWF シグネチャ `FWS / CWS / ZWS` を engine 全失敗後に検出し、`placeholder-flash` を返す

## 1. 背景

- 0B 動画や AppleDouble は、サムネイル作成を試す前に非動画と分かる。
- SWF は拡張子だけではなく、先頭シグネチャで判定したい。
- 既存の placeholder 描画・process log・救済worker 連携は再利用したい。

## 2. 方針

1. 先頭数バイトだけを読む軽量判定 helper を追加する。
2. `No Data` と `AppleDouble` は `ThumbnailPrecheckCoordinator` で即完了にする。
3. `Flash` は engine 実行後の `ThumbnailCreateResultFinalizer` で placeholder 化する。
4. 新しい placeholder も `placeholder-*` 命名へ揃え、既存救済判定と整合させる。

## 3. 実装

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailFileSignatureInspector.cs`
  - AppleDouble と SWF のマジックナンバー判定を追加
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailPrecheckCoordinator.cs`
  - `No Data` / `AppleDouble` の即時 placeholder 化を追加
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailFailurePlaceholderWriter.cs`
  - `AppleDouble` / `ShockwaveFlash` 種別を追加
  - `placeholder-appledouble` / `placeholder-flash` を追加
  - placeholder 描画文言を追加

## 4. テスト

- `Tests/IndigoMovieManager_fork.Tests/ThumbnailPrecheckCoordinatorTests.cs`
  - 0B / AppleDouble の precheck 即完了を追加
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailFailurePlaceholderWriterTests.cs`
  - AppleDouble / SWF シグネチャ分類を追加
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreateResultFinalizerTests.cs`
  - SWF 失敗時の `placeholder-flash` を追加
