# ThumbnailCreationService constructor最小化 実装計画

最終更新日: 2026-03-17

## 目的

- `ThumbnailCreationService` の公開 constructor を実使用に必要な最小セットへ絞る
- internal constructor もテスト差し替えに必要な最小本数へ減らす

## 今回の実装

- public constructor を次の 3 本へ整理
  - `ThumbnailCreationService()`
  - `ThumbnailCreationService(IThumbnailCreationHostRuntime hostRuntime, IThumbnailCreateProcessLogWriter processLogWriter = null)`
  - `ThumbnailCreationService(IVideoMetadataProvider videoMetadataProvider, IThumbnailLogger logger, IThumbnailCreationHostRuntime hostRuntime, IThumbnailCreateProcessLogWriter processLogWriter = null)`
- internal constructor は廃止し、`ThumbnailCreationServiceFactory.CreateForTesting(...)` へ移動
- テスト差し替え入力は `ThumbnailCreationOptions` で渡す

## 効果

- constructor 一覧の見通しが良くなる
- 呼び出し側の既存コードは壊さずに、service 本体の API 面積だけ縮む
- 今後さらに factory 化する時に、撤去対象が明確になる

## 追記

- この段で internal constructor は 0 本になった
- テスト側は `ThumbnailCreationServiceFactory.CreateForTesting(...)` を通して engine 差し替えを行う
