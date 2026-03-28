# AI向け 差し戻し指示 Codex T3 Watcher再整理 2回目 2026-03-20

最終更新日: 2026-03-20

## 1. 対象

- T3: Watcher `CheckFolderAsync` 薄化

## 2. 再差し戻し理由

- 初回の F1 / F2 は概ね解消した
- ただし再レビューで F4 が出た
- 前回 backlog の deferred batch が残っている時、新しく発生した visible 動画でも deferred を吐き切るまで観測されない

## 3. 今回の修正要求

1. deferred batch が残っていても、新しい visible 候補が古い backlog の後ろへ回り続けないようにする
2. `visible-only + 200件上限 + deferred batch` の結合動作を崩さない
3. DB スコープ付き deferred state / last_sync の整理は壊さない

## 4. 触る対象

- `Watcher\MainWindow.Watcher.cs`
- `Watcher\MainWindow.WatchScanCoordinator.cs`
- 関連テスト

## 5. 触ってはいけないこと

- 初回 F2 を戻すこと
- `visible-only` の優先順を単一スキャン内だけの話へ後退させること
- UI 抑制を強制中断型へ変えること

## 6. 修正の方向

- deferred batch が残っていても、新規候補の再収集を完全に飛ばさない構造へ寄せる
- もしくは deferred backlog と新規 visible 候補を同一回で再マージし、visible を先に返す
- 少なくとも `TryTakeDeferredWatchScanBatch(...)` 成功時の即 return が、visible 優先を watch またぎで壊さないことを示す

## 7. 最低限の確認

- `WatchVisibleOnlyGatePolicyTests`
- `WatchDeferredScanStatePolicyTests`
- `WatchScanCoordinatorPolicyTests`
- 今回追加する結合テスト

## 8. 完了条件

1. 旧 deferred backlog が残っていても、新規 visible 候補が先に処理される
2. DB 切替またぎ防止は維持される
3. `CheckFolderAsync` の責務は薄いまま維持される
4. 結合テストで F4 の再発防止が固定される
