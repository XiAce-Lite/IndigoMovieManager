# Models ドキュメント案内

このフォルダは、動画メタ情報モデルまわりのコードと補足資料を扱います。
現状は `MovieInfo` 系の取得仕様メモが中心です。

## 人間向けの入口

- [MovieInfo_取得値と取得方法.md](MovieInfo_取得値と取得方法.md)
  - `MovieInfo` が何をどう取っているかを追える現行資料です。

## AI / 実装向けの入口

- [MovieInfo_取得値と取得方法.md](MovieInfo_取得値と取得方法.md)
  - 現行実装の取得経路と既定値を確認できます。
- [MovieInfo_FFMediaToolkit必要情報取得方法.md](MovieInfo_FFMediaToolkit必要情報取得方法.md)
  - FFMediaToolkit 前提の取得条件を詰める時に使います。

## 現状のコード配置 (2026-03-12)

- `MovieInfo.cs`
  - 動画メタ情報取得の中心です。
- `MovieCore.cs`
  - 共通プロパティの土台です。
- `MovieCoreMapper.cs`
  - モデル変換の補助です。
- `MovieRecords.cs`
  - DBや一覧まわりで使う型です。

## 配置ルール

- モデルの取得仕様、項目定義、互換性メモはこのフォルダに置く
- `MovieInfo` の取得経路や既定値が変わった時は、ここも更新する
