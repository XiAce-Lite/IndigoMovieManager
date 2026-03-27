# Implementation Plan ThumbnailCreationService movie meta resolver切り出し 2026-03-17

最終更新日: 2026-03-17

変更概要:
- `ThumbnailMovieMetaResolver` を追加し、hash / duration / codec / DRM 事前判定と cache を `ThumbnailCreationService` から外した
- `ThumbnailCreationService` の前半は resolver 呼び出しへ寄せた
- `ThumbnailMovieMetaResolverTests` を追加し、provider 値反映と cache 更新を固定した

## 1. 目的

`CreateThumbAsync` 前半に混在していた

- outPath 解決
- hash 解決
- duration 解決
- codec 解決
- ASF/WMV の DRM 事前判定
- movie meta cache

を service 本体から外し、事前解決の責務を専用 resolver へまとめる。

## 2. 今回やったこと

1. `ThumbnailMovieMetaResolver` を追加した
2. `ResolveThumbnailOutPath` を resolver 側へ移した
3. `GetCachedMovieMeta` / `CacheMovieDuration` を resolver 側へ移した
4. `ResolveFileSizeBytes` / `ResolveVideoCodec` / `ResolveDurationSec` を resolver 側へ追加した
5. `IsAsfFamilyFile` / `TryDetectAsfDrmProtected` / `IndexOfBytes` を resolver 側へ移した
6. `CachedMovieMeta` を resolver 側へ移した
7. `ThumbnailCreationService` は resolver 経由で前半情報を組み立てる形へ変えた
8. `ThumbnailMovieMetaResolverTests` を追加した

## 3. 判断

- 事前解決は engine 実行や bitmap 生成と独立しているため、service 本体に残す理由が弱い
- provider 注入を持つ resolver に寄せると、`ThumbnailCreationService` の前半が読みやすくなる
- static cache と DRM 判定を resolver へ閉じることで、後続の result writer / failure policy 分割も進めやすい

## 4. 今回やらないこと

1. placeholder 作成の分離
2. process log writer 周辺の分離
3. bitmap utility 群の分離
4. manual thumbInfo 復元経路の helper 化

## 5. 次の候補

1. failure placeholder 判定と画像保存を result writer へ寄せる
2. manual / missing movie / DRM precheck の precheck 分岐を helper 化する
3. `CreateThumbAsync` の engine 実行後半を result persistence 側へ分割する
