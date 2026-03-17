# ThumbnailCreationService options factory寄せ 実装計画

最終更新日: 2026-03-17

## 目的

- `ThumbnailCreationService` に残っている options 組み立て helper を外し、service 本体を入口委譲にさらに寄せる
- 既定 options と testing options の生成位置を component factory 側へ統一する

## 今回の実装

- `ThumbnailCreationServiceComponentFactory` に次を追加
  - `CreateDefaultOptions()`
  - `CreateOptions(...)`
  - `CreateTestingOptions(...)`
- `ThumbnailCreationService` は上記 factory を呼ぶだけに変更
- service 内の `CreateOptions(...)` static helper は削除

## 効果

- service 本体から依存既定値の知識がさらに減る
- `CreateForTesting(...)` も options 組み立てを自前で持たなくなる
- options 生成と composition 生成が同じ factory に集約され、責務が揃う
