# ThumbnailCreationService public request型追加 実装計画

最終更新日: 2026-03-18

## 目的

- `ThumbnailCreationService` に残る長い public 引数列を、将来整理しやすい request DTO へ寄せる
- 既存の互換 overload は残しつつ、canonical な public 入口を 1 本に揃える
- facade 本体の責務を「互換 overload」と「request DTO 入口」へ分ける

## 今回の実装

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateArguments.cs` を追加
  - `ThumbnailCreateArgs`
  - `ThumbnailBookmarkArgs`
- `ThumbnailCreationService` に次を追加
  - `CreateThumbAsync(ThumbnailCreateArgs, CancellationToken)`
  - `CreateBookmarkThumbAsync(ThumbnailBookmarkArgs, CancellationToken)`
- 既存の `CreateThumbAsync(...)` overload は `ThumbnailCreateArgs` を組み立てて新入口へ委譲
- 既存の `CreateBookmarkThumbAsync(...)` overload も `ThumbnailBookmarkArgs` を組み立てて新入口へ委譲
- `ThumbnailCreateEntryCoordinator` も `CreateAsync(ThumbnailCreateArgs, CancellationToken)` を canonical 入口に追加
- `ThumbnailCreateEntryCoordinatorTests` と `ThumbnailCreationServicePublicRequestTests` で新入口を確認

## 効果

- public API を段階的に request 型中心へ寄せる足場ができる
- 既存呼び出しを壊さずに、新しい呼び出し側だけ先に簡潔な DTO 入口へ移行できる
- facade 本体は内部 invocation を知らず、public request DTO だけを扱う形へ近づく

## 2026-03-18 呼び出し側移行

- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs` の通常実行経路と isolated child 実行経路を `ThumbnailCreateArgs` 入口へ移行した
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailEngineBenchTests.cs` も bench 実行時の service 呼び出しを `ThumbnailCreateArgs` へ移した
- `Thumbnail/MainWindow.ThumbnailCreation.cs` も、別件差分を崩さない最小変更で `ThumbnailCreateArgs` / `ThumbnailBookmarkArgs` 入口へ移行した
- `Tests/IndigoMovieManager_fork.Tests/AutogenExecutionFlowTests.cs` と `ThumbnailCreationHostRuntimeTests.cs` も `ThumbnailCreateArgs` 入口へ寄せた
- 旧 overload は `ThumbnailCreationService` 内で「既存呼び出し互換の wrapper」としてコメント明示した
