# ThumbnailCreationService workflow coordinator切り出し

更新日: 2026-03-17

## 目的

`Thumbnail\ThumbnailCreationService.cs` に残っていた `CreateThumbAsync` の本流を、service 本体から切り離す。

- 準備
- 同一出力先 lock
- precheck
- duration 補完
- context build
- engine 実行
- result finalize

上の流れを 1 本の workflow として外へ寄せ、service は入口と互換 facade に縮退する。

## 今回の切り出し

追加:

- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailCreateWorkflowCoordinator.cs`

責務:

- `ThumbnailCreatePreparationResolver` で準備を行う
- 出力 jpg ごとの lock を取る
- `ThumbnailPrecheckCoordinator` を呼ぶ
- `ThumbnailJobContextBuilder` で context を組み立てる
- `ThumbnailEngineExecutionCoordinator` を呼ぶ
- `ThumbnailCreateResultFinalizer` で結果を仕上げる

`Thumbnail\ThumbnailCreationService.cs` は次だけを担当する。

- `QueueObj` から `ThumbnailRequest` への互換変換
- 公開 API の入口維持
- bookmark 生成
- 互換 static helper

## 効果

- service 本体の `CreateThumbAsync` が workflow への委譲だけになる
- orchestration の責務境界が `workflow / precheck / context / execution / finalizer` に分かれる
- 以後は workflow をさらに `request normalization` や `execution session` 単位へ崩しやすい

## 次

- workflow に残る request 正規化を、必要なら専用 request normalizer へ分ける
- `ThumbnailCreationService` に残る static helper 群のうち、本流から切れたものを個別 helper へ寄せる
