# ThumbnailCreationService test static factory化 実装計画

最終更新日: 2026-03-17

## 目的

- テスト専用の special constructor を service 本体から消す
- engine 差し替え入口を `ThumbnailCreationServiceFactory.CreateForTesting(...)` へまとめ、public API と test API の境界を明確にする

## 今回の実装

- internal constructor を全削除
- `ThumbnailCreationServiceFactory.CreateForTesting(...)` を追加
- テストや legacy test は engine 差し替え時に `ThumbnailCreationServiceFactory.CreateForTesting(...)` を使う形へ変更
- 差し替え時の補助入力は `ThumbnailCreationOptions` で渡す

## 効果

- `ThumbnailCreationService` の constructor 一覧と static helper が public 用だけになる
- テスト用の入口が名前付きになり、用途が読み取りやすくなる
- 将来 `ServiceFactory` に分離したくなっても、移行点が 1 箇所に固定される
