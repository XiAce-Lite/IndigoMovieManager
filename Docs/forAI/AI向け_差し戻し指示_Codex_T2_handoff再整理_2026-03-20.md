# AI向け 差し戻し指示 Codex T2 handoff再整理 2026-03-20

最終更新日: 2026-03-20

## 1. 対象

- T2: Thumbnail.Core handoff 整理

## 2. 差し戻し理由

- 初回レビューで F3 が出た
- rescue lane 判定が即時設定値ではなく、最大 1 秒古いキャッシュへ変わっている
- host 設定解決を queue 側へ寄せる方向も、完成形の依存方向として逆向き

## 3. 今回の修正要求

1. manual rescue と terminal failure 記録で、lane 判定が設定変更直後でも即時値を使うよう戻す
2. queue 側へ host 設定解決を持ち込まない
3. shared policy 化で得た failure kind / handoff kind の整理は維持する

## 4. 触る対象

- `Thumbnail\MainWindow.ThumbnailRescueLane.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailFailureRecorder.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailRescueHandoffPolicy.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailLaneClassifier.cs`
- 関連テスト

## 5. 触ってはいけないこと

- `Factory + Interface + Args` 境界を壊すこと
- `ThumbnailCreationService` へ orchestration を戻すこと
- rescue worker の failure kind 共通化そのものを巻き戻すこと

## 6. 修正の方向

- lane 判定は queue 側のキャッシュ依存に寄せず、即時設定値を使う small policy へ戻すか、host から値を渡す
- ただし failure kind / handoff kind の shared policy は残してよい
- 依存方向は「host 設定 -> queue policy」であって、「queue から host 設定を読みに行く」にしない

## 7. 最低限の確認

- `ThumbnailRescueHandoffPolicyTests`
- `ThumbnailFailureDbTests`
- `MissingThumbnailRescuePolicyTests`
- 関連 build

## 8. 完了条件

1. lane 判定が即時設定値で決まる
2. queue 側の host 設定依存が増えていない
3. failure kind / handoff kind の共通化は維持される
4. 追加または更新したテストで回帰が押さえられている
