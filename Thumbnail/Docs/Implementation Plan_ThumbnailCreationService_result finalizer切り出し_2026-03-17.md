# ThumbnailCreationService result finalizer切り出し

更新日: 2026-03-17

## 背景

- `ThumbnailCreationService` には engine 実行後の後処理が残っていた
- ここには次が混在していた
  - failure placeholder 置換
  - `#ERROR` marker 出力
  - duration cache 更新
  - stale marker 掃除
  - process log 書き込み

この塊を `result finalizer` へ寄せて、service を orchestration に寄せる

## 今回の方針

1. `ThumbnailCreateResultFinalizer` を追加する
2. 即時返却用と engine 実行後用の 2 入口を持たせる
3. `ThumbnailCreationService` の local function をなくす

## 変更点

### 1. `ThumbnailCreateResultFinalizer`

- `FinalizeImmediate(...)`
  - `ProcessEngineId` 設定
  - process log 書き込み
- `FinalizeExecution(...)`
  - failure placeholder
  - `#ERROR` marker
  - duration cache
  - success marker cleanup
  - process log

### 2. `ThumbnailCreationService`

- `ReturnWithProcessLog` local function を削除
- precheck / missing-movie / DRM precheck 返却を finalizer 経由へ変更
- engine 実行後の後処理を finalizer 呼び出しへ変更

## テスト

- `ThumbnailCreateResultFinalizerTests`
  - 即時返却時の duration 補完 log
  - unsupported 失敗の placeholder 化
  - failure marker と duration cache 更新

## 残り

- `ThumbnailCreationService` にはまだ precheck 群が残る
- ただし本体はかなり orchestration 中心になった
