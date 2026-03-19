# ThumbnailCreationService entry coordinator切り出し 実装計画

最終更新日: 2026-03-19

## 目的

- `ThumbnailCreationService` に残っている入口変換を外し、service を facade としてさらに薄くする
- `QueueObj` 互換処理と `ThumbnailCreateWorkflowRequest` 組み立てを 1 箇所へ集約する

## 今回の実装

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateEntryCoordinator.cs` を追加
- `QueueObj -> ThumbnailRequest` 変換と `ApplyThumbnailRequest(...)` の戻しを coordinator 側へ移動
- `ThumbnailCreateWorkflowRequest` の組み立ても coordinator 側へ移動
- `ThumbnailCreationService` は `CreateBookmarkThumbAsync(...)` と `CreateThumbAsync(...)` の委譲だけに整理
- direct test として `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreateEntryCoordinatorTests.cs` を追加
- 内部の旧 overload は整理し、coordinator の canonical 入口も `CreateAsync(ThumbnailCreateArgs, CancellationToken)` に一本化した
- 中間 DTO `ThumbnailCreateInvocation` も削除し、`Args -> WorkflowRequest` をその場で組み立てる形へ簡素化した
- coordinator 内の public 面も整理し、constructor / `CreateAsync(...)` は internal に落として assembly 内境界を明示した
- `ThumbnailCreateArgs` は `QueueObj` か `Request` のどちらか必須として入口で検証するようにした
- 変換後 `ThumbnailRequest.MovieFullPath` も必須として検証するようにした
- 上記の必須条件検証は `ThumbnailRequestArgumentValidator` へ寄せ、service / coordinator の重複を外した
- validator の method も internal に落とし、assembly 内 helper であることを明示した

## 効果

- service 本体から legacy 互換処理と workflow request 組み立てが消える
- 入口変換の回帰を workflow 本流から独立してテストできる
- 今後 public API を整理する時の差し替え位置が明確になる
- internal API も `Args` 中心になり、service と coordinator の入口形が揃う
- coordinator 内部のデータ流れが 1 段減り、読解と変更の足場がさらに単純になる
- `EntryCoordinator` 自体も internal API として閉じ、外へ見せる面が `Factory + Interface + Args` に寄る
- 不完全な `Args` を silent に通さず、入口契約違反を早い段で検知できる
- `QueueObj` / `Request` のどちら経由でも `MovieFullPath` 必須が揃い、生成前提が明確になる
- 入口検証の変更点が 1 箇所に集まり、今後の契約調整を追いやすくなる
