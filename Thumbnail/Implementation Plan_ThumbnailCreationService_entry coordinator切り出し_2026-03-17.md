# ThumbnailCreationService entry coordinator切り出し 実装計画

最終更新日: 2026-03-17

## 目的

- `ThumbnailCreationService` に残っている入口変換を外し、service を facade としてさらに薄くする
- `QueueObj` 互換処理と `ThumbnailCreateWorkflowRequest` 組み立てを 1 箇所へ集約する

## 今回の実装

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateEntryCoordinator.cs` を追加
- `QueueObj -> ThumbnailRequest` 変換と `ApplyThumbnailRequest(...)` の戻しを coordinator 側へ移動
- `ThumbnailCreateWorkflowRequest` の組み立ても coordinator 側へ移動
- `ThumbnailCreationService` は `CreateBookmarkThumbAsync(...)` と `CreateThumbAsync(...)` の委譲だけに整理
- direct test として `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreateEntryCoordinatorTests.cs` を追加

## 効果

- service 本体から legacy 互換処理と workflow request 組み立てが消える
- 入口変換の回帰を workflow 本流から独立してテストできる
- 今後 public API を整理する時の差し替え位置が明確になる
