# ThumbnailCreationService legacy optional compile化 実装計画

最終更新日: 2026-03-18

## 目的

- obsolete 化済みの `Legacy` API を「常に同梱」から「必要時だけ compile」へ進める
- 完全削除の前に、production 側に hidden dependency が残っていないかを dry-run で確認できるようにする

## 今回の実装

- `src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj`
  に `EnableThumbnailCreationServiceLegacyApi` を追加した
- 既定値は `true` とし、現状の互換動作は変えない
- `Thumbnail/ThumbnailCreationService.Legacy.cs` の compile include を条件付きに変更した
  - `true` または未指定: compile する
  - `false`: compile しない

## 使い方

- 既定の互換あり build
  - `dotnet build src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj -c Debug -p:Platform=x64`
- legacy なし dry-run
  - `dotnet build src/IndigoMovieManager.Thumbnail.Engine/IndigoMovieManager.Thumbnail.Engine.csproj -c Debug -p:Platform=x64 -p:EnableThumbnailCreationServiceLegacyApi=false`

## 効果

- `Legacy` API を消した時にどこが落ちるかを、コミット前に見える化できる
- 互換維持と削除準備を同時に進めやすくなる
- 将来の完全削除は property と compile include を外すだけの小さい差分に寄せられる

## 2026-03-18 追記

- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreationServiceArchitectureTests.cs`
  で optional compile property 自体もガード対象に加えた
- これで legacy 分離の設計と、削除準備の build property の両方が回帰検知に乗った
