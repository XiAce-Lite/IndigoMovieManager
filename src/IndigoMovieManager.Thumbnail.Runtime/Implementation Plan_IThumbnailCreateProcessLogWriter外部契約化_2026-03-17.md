# IThumbnailCreateProcessLogWriter 外部契約化プラン 2026-03-17

## 1. 目的

`IndigoMovieManager.Thumbnail.Engine` の物理的自立に向けて、アプリケーション固有の CSV 保存責務を engine から外し、host 側が自由に差し替えられる public 契約へ揃える。

## 2. 今回の整理

- `Engine` に残す
  - `ThumbnailCreateProcessLogEntry`
  - `IThumbnailCreateProcessLogWriter`
- `Runtime` に移す
  - `DefaultThumbnailCreateProcessLogWriter`
  - CSV の列順、ヘッダー、日時・状態表現、I/O 規約

## 3. 実装方針

- `ThumbnailCreationService` の既定 writer は no-op にする
- app / worker の host 側だけが `DefaultThumbnailCreateProcessLogWriter` を明示注入する
- これにより、外部利用者は CSV 保存を使うか、別 writer を使うか、完全に無効化するかを選べる

## 4. 現在の状態

- `DefaultThumbnailCreateProcessLogWriter` は `src/IndigoMovieManager.Thumbnail.Runtime` 所属
- `DefaultThumbnailCreationHostRuntime` も `src/IndigoMovieManager.Thumbnail.Runtime` 所属
- `Engine` は CSV 保存 I/O を持たない
- `Engine` の既定 host は internal fallback に縮退
- 既定の app / rescue worker は従来どおり CSV を書く

## 5. 次の論点

- rescue worker を別 repo 化する段で、`Runtime` 依存を CLI 引数注入へさらに薄くするか
