# ThumbnailCreationService composition切り出し 実装計画

最終更新日: 2026-03-17

## 目的

- `ThumbnailCreationService` から依存組み立てを外し、service 本体を facade として保つ
- engine 群、workflow、logger 差し替えの責務を composition 側へ集約する
- 今後の constructor 整理や DI 化を、service 本体を太らせずに進められる状態へ寄せる

## 今回の実装

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationServiceComposition.cs` を追加
- `ThumbnailCreationEngineSet` で 4 engine を束ねる
- `ThumbnailCreationOptions` で metadata provider / logger / host runtime / process log writer を束ねる
- `ThumbnailCreationServiceComponentFactory` で次を担当する
  - 既定 engine set の生成
  - 既定 options の生成
  - テスト差し替え用 engine set の生成
  - テスト差し替え用 options の生成
  - `ThumbnailRuntimeLog` への logger 登録
  - `ThumbnailEngineRouter` / `ThumbnailEngineExecutionPolicy` / `ThumbnailEngineExecutionCoordinator` の組み立て
  - `ThumbnailMovieMetaResolver` / `ThumbnailCreatePreparationResolver` / `ThumbnailJobContextBuilder` / `ThumbnailCreateResultFinalizer` / `ThumbnailPrecheckCoordinator` / `ThumbnailCreateWorkflowCoordinator` / `ThumbnailCreateEntryCoordinator` の組み立て
- `ThumbnailCreationService` は `Compose(...)` の結果から次だけを保持する
  - `ThumbnailBookmarkCoordinator`
  - `ThumbnailCreateEntryCoordinator`

## 効果

- service 本体から constructor の巨大組み立てが消える
- engine 差し替え系テストの入口を維持したまま、本流の依存生成を 1 箇所へ固定できる
- 今後 `ThumbnailCreationService` の public API を保ったまま、内部の構成だけ安全に差し替えられる

## 次の候補

- `ThumbnailCreationServiceComposition` 内の組み立てをさらに小さい factory 群へ分割する
- `ThumbnailRuntimeLog.SetLogger(...)` のグローバル副作用を composition 境界で明示化する
- 将来 DI を入れる場合は、この composition を adapter として残して移行コストを下げる
