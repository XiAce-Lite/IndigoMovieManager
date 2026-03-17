# ThumbnailCreationService constructor options化 実装計画

最終更新日: 2026-03-17

## 目的

- `ThumbnailCreationService` の public / internal overload は維持したまま、内部の依存組み立て入力を 1 本へ束ねる
- engine 差し替え、logger 差し替え、host runtime 差し替えの既存テスト入口を壊さずに、constructor の重複を減らす

## 今回の実装

- `ThumbnailCreationOptions` を service 内部の options として利用
- `ThumbnailCreationService.CreateOptions(...)` を追加
- 各 constructor は個別の依存組み立てをやめて、`CreateOptions(...)` を経由する形へ変更
- private constructor は `ThumbnailCreationOptions` 1 本を受けて `Compose(...)` へ渡すだけに変更

## 効果

- constructor 群の重複が減り、既定値の置き場が 1 箇所に揃う
- 今後 constructor を削減する時も、まず `CreateComponentRequest(...)` を調整すればよくなる
- service 本体の責務がさらに facade 寄りになる

## 次の候補

- public constructor を最小セットへ整理し、テスト側は factory helper を使う形へ寄せる
- `CreateForTesting(...)` を使う形へ統一し、special constructor を service 本体から外す
