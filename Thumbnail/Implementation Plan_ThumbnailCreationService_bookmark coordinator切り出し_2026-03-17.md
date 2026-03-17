# ThumbnailCreationService bookmark coordinator切り出し 実装計画

最終更新日: 2026-03-17

## 目的

- `ThumbnailCreationService` から bookmark 用 1 枚生成の実装を外す
- facade 本体には公開入口だけを残し、bookmark の分岐と例外処理を専用 coordinator に集約する

## 今回の実装

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailBookmarkCoordinator.cs` を追加
- `ThumbnailCreationServiceComposition` から `ThumbnailBookmarkCoordinator` を返すように変更
- `ThumbnailCreationService.CreateBookmarkThumbAsync(...)` は coordinator 委譲だけに変更

## 効果

- `ThumbnailCreationService` 本体から `Path.Exists` 判定、engine 選択、bookmark 例外処理が消える
- bookmark 経路の責務が `CreateThumbAsync(...)` 本流と分離され、今後の差し替え位置が明確になる

## 次の候補

- `ThumbnailCreationService` の constructor 群を request/option 型へ寄せて重複を減らす
- `ThumbnailCreationServiceComposition` の組み立てを、bookmark 系と workflow 系でさらに分割する
