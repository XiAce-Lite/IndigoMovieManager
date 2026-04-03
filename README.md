# IndigoMovieManager

最終更新日: 2026-04-04

IndigoMovieManager は、WhiteBrowser 互換を重視した動画管理アプリです。
動画一覧の管理、検索、監視フォルダからの取り込み、サムネイル生成をまとめて扱えます。

このリポジトリの目標と要件は次を参照してください。

- [リポジトリ目標と要件_2026-04-04.md](C:/Users/na6ce/source/repos/IndigoMovieManager/リポジトリ目標と要件_2026-04-04.md)

[!['動作風景動画'](https://img.youtube.com/vi/kvj_862G4iI/maxresdefault.jpg)](https://youtu.be/kvj_862G4iI)

## 最近の更新

- 2026-04-01 共有フォルダ、NASに対応

  UNC/NAS 上の DB や監視フォルダを、これまでより安心して扱えるよう整理しました。

- 2026-03-29 ドラッグ＆ドロップ対応

  画像フォルダ、WhiteBrowser の `*.wb` のD&Dに対応しました。

## このアプリでできること

- 指定したフォルダから動画を取り込み
- サムネイルを自動で作成します
- 動画リストから再生等の様々な機能

## WhiteBrowser互換機能

- スキン対応
- ダウンローダー(Youtube,X)


## WhiteBrowserに出来てこのアプリでできないこと

- スタイル
- タグバー
- タグレット
- ジェスチャー
- キースクリプト
- コマンドライン
- サムネイル作成bat処理
## ダウンロード

- https://github.com/T-Hamada0101/IndigoMovieManager_fork/releases
- [初めて使う方向けの手順](Docs/forHuman/簡単マニュアル_ReleaseAssetsからダウンロードして使い始めるまで_2026-03-29.md)

## 使い始め方

1. Release の `Assets` からアプリ本体 ZIP をダウンロードします。
2. ZIP を展開します。
3. `IndigoMovieManager_fork_workthree.exe` を起動します。
4. 既存の `*.wb` を使うか、動画フォルダをドロップして新しい DB を作成します。

## WhiteBrowser から移行する方へ

- 元の WhiteBrowser DB を直接使わず、必ずコピーした `*.wb` を使ってください
- 既存 DB を使う時は、その `*.wb` をメイン画面へドラッグ＆ドロップすると開けます
- 新しく始める時は、動画フォルダをメイン画面へドラッグ＆ドロップしてください

詳しい注意点:
Docs/Gemini/Migration_from_WhiteBrowser_Notes_2026-03-25.md

## 動作に必要なもの

- Windows
- `.NET 8 Desktop Runtime`

利用だけなら `.NET 8 Desktop Runtime` があれば動きます。

## 配布内容

- アプリ本体
- 必要な DLL
- `rescue-worker`

`rescue-worker` はアプリ本体 ZIP の中に同梱されています。

## 困った時

- 起動しない時は `.NET 8 Desktop Runtime` の有無を確認してください
- 使い始め方は次の手順書を確認してください
  Docs/forHuman/簡単マニュアル_ReleaseAssetsからダウンロードして使い始めるまで_2026-03-29.md

## 開発者向け情報

開発向け README は次を見てください。

- README_2026-03-28.md
