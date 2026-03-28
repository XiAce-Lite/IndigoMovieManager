# ThumbnailCreationService context builder切り出し

更新日: 2026-03-17

## 背景

- `ThumbnailCreationService` には engine 実行前の context 構築が残っていた
- ここには次が混在していた
  - manual 時の `ThumbInfo` 復元と秒差し替え
  - auto 時の `ThumbInfo` 既定生成
  - 平均 bitrate 計算
  - codec 解決
  - `ThumbnailJobContext` 組み立て

この塊を service 本体から外し、前処理の見通しを良くする

## 今回の方針

1. `ThumbnailJobContextBuilder` を追加する
2. `manual` / `auto` の `ThumbInfo` 分岐を builder 側へ寄せる
3. `ThumbnailCreationService` は builder の成功/失敗だけを見る

## 変更点

### 1. `ThumbnailJobContextBuilder`

- `ThumbnailJobContextBuildRequest`
- `ThumbnailJobContextBuildOutcome`

を追加し、context 構築の入口を 1 つにまとめた

### 2. builder に寄せた責務

- manual 時の `GetThumbInfo()` と `ThumbPanelPosition` / `ThumbTimePosition` 反映
- auto 時の `BuildAutoThumbInfo(...)`
- 平均 bitrate 計算
- `ResolveVideoCodec(...)`
- `HasEmojiPath` 判定を含む `ThumbnailJobContext` 構築

### 3. `ThumbnailCreationService`

- DRM placeholder 用 context 生成
- 通常 engine 実行用 context 生成

を builder 呼び出しへ差し替えた

## テスト

- `ThumbnailJobContextBuilderTests`
  - auto 生成時の bitrate / codec / thumbInfo
  - manual 生成時の panel 秒更新
  - WB互換メタが無い manual jpg の失敗

## 残り

- `ThumbnailCreationService` にはまだ
  - precheck 群
  - placeholder / marker / process log 後処理
  が残っている
- 次段では result finalizer を外すと、service はさらに coordinator 寄りになる
