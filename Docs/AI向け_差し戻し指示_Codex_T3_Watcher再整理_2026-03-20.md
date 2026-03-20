# AI向け 差し戻し指示 Codex T3 Watcher再整理 2026-03-20

最終更新日: 2026-03-20

## 1. 対象

- T3: Watcher `CheckFolderAsync` 薄化

## 2. 差し戻し理由

- 初回レビューで重大 finding が 2 件出た
- F1:
  - `visible-only` より先に 200 件制限をかけており、表示中動画が deferred へ押し出される
- F2:
  - deferred watch state が DB 切替を跨いで再混入する経路がある

## 3. 今回の修正要求

1. `visible-only` の目的を守り、表示中動画が deferred 側へ押し出されないようにする
2. deferred watch state に DB 切替またぎの混入が起きないようにする
3. 既存の `visible-only gate`、deferred batch、UI 抑制の整合は維持する

## 4. 触る対象

- `Watcher\MainWindow.Watcher.cs`
- `Watcher\MainWindow.WatchScanCoordinator.cs`
- `Views\Main\MainWindow.xaml.cs`
- 関連テスト

## 5. 触ってはいけないこと

- `Created` 直書き MainDB 登録復活
- `Renamed` 直呼び復活
- `FilterAndSort(..., true)` の即時全面再評価復活
- 左ドロワー抑制の強制中断化

## 6. 修正の方向

- 200 件制限は、表示中動画優先を壊さない順序に直す
- deferred batch のキーか保持条件に DB スコープを持たせるか、DB 切替後の旧 batch 再投入を構造的に止める
- background scan 完了後の `StoreDeferredWatchScanBatch` が旧 DB へ紐づく結果を新 DB に入れない

## 7. 最低限の確認

- `WatchVisibleOnlyGatePolicyTests`
- `WatchUiSuppressionPolicyTests`
- `WatchScanCoordinatorPolicyTests`
- 今回追加した DB 切替またぎ防止テスト

## 8. 完了条件

1. 表示中動画優先が守られる
2. deferred watch state が DB 切替を跨がない
3. `CheckFolderAsync` の責務は薄いまま維持される
4. テストで F1/F2 を再発防止できている
