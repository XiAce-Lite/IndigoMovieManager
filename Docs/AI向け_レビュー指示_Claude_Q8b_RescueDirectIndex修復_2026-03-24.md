# Q8b RescueDirectIndex 修復 レビュー指示

## 審査観点
- `requestedFailureId` が `RescueLane`、`RescueWorkerLauncher`、`RescueWorkerApplication` の間で破綻しないこと。
- `failure db` の参照更新と placeholder cleanup の順序が逆転しないこと。
- `direct` 経路でのみ本件差分が収束し、`L2/L3` の no-op 事象を再導入していないこと。
- `Watch` 系 (`Watcher*`) 側の既知混線や他レーン差分が混入していないこと。
- 既存テストを壊す最小差分であること、必要なら regression テストを追加していること。

## 禁止事項
- `L2`（`thumbnail metadata`）や `L3`（`timeout`）の変更は本帯で採らない。
- 別レーンの `SelectedIndex`/`MainWindow` 粗変更を混在させない。

## 受け入れ基準
- review で `findings なし` が取れること。
- 本件の差分だけを reviewer が再現可能な最小差分として確認できること。
