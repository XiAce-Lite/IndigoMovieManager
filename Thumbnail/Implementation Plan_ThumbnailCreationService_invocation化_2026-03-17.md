# ThumbnailCreationService invocation化 実装計画

最終更新日: 2026-03-17

## 目的

- `CreateThumbAsync(...)` に残る長い引数列を 1 本の invocation 型へ寄せる
- service と entry coordinator の本体を、互換 overload ではなく invocation 中心で保つ

## 今回の実装

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateEntryCoordinator.cs` に `ThumbnailCreateInvocation` を追加
- `ThumbnailCreationService` の `CreateThumbAsync(...)` 2 本は invocation を組み立てて委譲するだけに変更
- `ThumbnailCreateEntryCoordinator` も invocation 版を本流にし、既存 overload は wrapper に変更
- `ThumbnailCreateEntryCoordinatorTests` を invocation 直叩きへ更新

## 効果

- service 本体から長い引数列の受け渡し実装がほぼ消える
- entry coordinator 側も workflow 入口を 1 本で扱える
- 将来 public API を request 型へ整理する時の移行先が明確になる
