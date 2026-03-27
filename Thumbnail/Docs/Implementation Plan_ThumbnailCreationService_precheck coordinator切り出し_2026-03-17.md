# ThumbnailCreationService precheck coordinator切り出し

更新日: 2026-03-17

## 背景

- `ThumbnailCreationService` の先頭には、即時返却系の precheck が残っていた
- ここには次が混在していた
  - manual 更新時の既存 jpg 存在確認
  - 出力フォルダ作成
  - missing movie placeholder
  - file size 解決
  - DRM precheck と placeholder 化

この塊を coordinator 化して、service 本体の先頭を薄くする

## 今回の方針

1. `ThumbnailPrecheckCoordinator` を追加する
2. 継続可否は `ThumbnailPrecheckOutcome` で返す
3. 即時返却系は finalizer を通す

## 変更点

### 1. `ThumbnailPrecheckCoordinator`

- manual target の存在確認
- 出力フォルダ作成
- missing movie placeholder
- file size 解決と request 反映
- DRM precheck placeholder

をまとめた

### 2. `ThumbnailCreationService`

- precheck 群を coordinator 呼び出しへ置換
- service 本体は
  - path / cache 準備
  - duration 解決
  - context build
  - engine 実行
  - finalizer
  の順がはっきり見える形になった

## テスト

- `ThumbnailPrecheckCoordinatorTests`
  - manual target missing
  - missing movie placeholder
  - DRM precheck placeholder
  - 通常継続時の file size 反映

## 残り

- `ThumbnailCreationService` にはまだ path / cache 準備と duration 解決が残る
- ただし A5 の主戦場だった orchestration の大きな塊はほぼ分離できた
