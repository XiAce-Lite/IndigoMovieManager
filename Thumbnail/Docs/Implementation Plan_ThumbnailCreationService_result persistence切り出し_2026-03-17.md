# Implementation Plan ThumbnailCreationService result persistence切り出し 2026-03-17

最終更新日: 2026-03-17

変更概要:
- `ThumbnailFailurePlaceholderWriter` を追加し、placeholder 判定と描画を `ThumbnailCreationService` から外した
- `ThumbnailOutputMarkerCoordinator` を追加し、出力掃除と `#ERROR` marker 更新を `ThumbnailCreationService` から外した
- `ThumbnailCreationService` は result persistence の詳細を持たず、呼び出し順だけを管理する形へ寄せた
- helper 直叩きテストを追加した

## 1. 目的

`ThumbnailCreationService` に残っていた

- failure placeholder の判定
- placeholder 画像の描画
- 自動生成前の古い jpg 掃除
- success / failure 時の `#ERROR` marker 更新

を外へ出し、service 本体を実行 orchestration に寄せる。

## 2. 今回やったこと

1. `ThumbnailFailurePlaceholderWriter` を追加した
2. `ClassifyFailureKind` を追加した
3. `TryCreate` を追加し、placeholder の描画と JPEG 追記を集約した
4. `ResolveProcessEngineId` を追加し、placeholder 成功時の engineId を固定化した
5. `ThumbnailOutputMarkerCoordinator` を追加した
6. `ResetExistingOutputBeforeAutomaticAttempt` を移した
7. failure 時の marker 生成 / 既存成功jpg優先判定を移した
8. success 時の stale marker 掃除を移した
9. `ThumbnailCreationService` から上記 helper 群を削除した
10. helper 用テストを追加した

## 3. 判断

- placeholder 判定と描画は engine 実行本体ではなく result persistence の責務である
- `#ERROR` marker 更新はファイル副作用の塊なので、service 本体から外した方が追いやすい
- placeholder の `ProcessEngineId` を専用 helper に閉じると rescue 側の判定も守りやすい

## 4. 今回やらないこと

1. `SaveCombinedThumbnail` / `TrySaveJpegWithRetry` の完全分離
2. near-black 判定の別 helper 化
3. process log writer との統合
4. manual thumbInfo 復元の helper 化

## 5. 次の候補

1. `SaveCombinedThumbnail` / `TrySaveJpegWithRetry` を image writer へ寄せる
2. near-black reject と preview bitmap utility を別 helper へ寄せる
3. `CreateThumbAsync` の precheck 分岐を thin coordinator へ整理する
