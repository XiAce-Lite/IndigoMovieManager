# ThumbnailCreationService preparation resolver切り出し

更新日: 2026-03-17

## 背景

- precheck を外した後も、`ThumbnailCreationService` の冒頭には
  - layout 解決
  - thumb out path 解決
  - source path 正規化
  - cache 取得
  - hash の request 反映
  - save path 生成
  - duration hint 保持
  が残っていた

この塊を resolver 化して、service 本体の先頭をさらに薄くする

## 今回の方針

1. `ThumbnailCreatePreparationResolver` を追加する
2. `Prepare(...)` で path / cache / hash / duration hint をまとめて返す
3. duration の最終補完は `ResolveDurationIfMissing(...)` へ寄せる

## 変更点

### 1. `ThumbnailCreatePreparationResolver`

- `Prepare(...)`
  - `ThumbnailLayoutProfile`
  - `ThumbnailOutPath`
  - `MovieFullPath`
  - `SourceMovieFullPath`
  - `InitialEngineHint`
  - `CacheKey`
  - `CacheMeta`
  - `DurationSec`
  - `SaveThumbFileName`
  をまとめる
- `ResolveDurationIfMissing(...)`
  - provider / shell fallback を使う既存 resolver に委譲する

### 2. `ThumbnailCreationService`

- 冒頭の準備処理を resolver 呼び出しへ置換
- service 本体は
  - preparation
  - output lock
  - precheck
  - context build
  - engine execution
  - finalizer
  の流れが見える形になった

## テスト

- `ThumbnailCreatePreparationResolverTests`
  - hash を request へ戻すこと
  - save path と source path 正規化
  - duration 補完

## 残り

- `ThumbnailCreationService` には output lock と本体 orchestration が残る
- ただし A5 の主要責務分割はかなり進んだ
