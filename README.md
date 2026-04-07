# IndigoMovieManager

最終更新日: 2026-04-04

IndigoMovieManager は、WhiteBrowser 互換を重視した動画管理アプリです。
動画一覧の管理、検索、監視フォルダからの取り込み、サムネイル生成をまとめて扱えます。

[!['動作風景動画'](https://img.youtube.com/vi/kvj_862G4iI/maxresdefault.jpg)](https://youtu.be/kvj_862G4iI)

## 最近の更新

### 2026-04-07 各種機能追加・修正

- インストーラー化しました
- かな・ローマ字検索対応
- カスタムサムネ対応 (動画名.jpg)

#### バグ修正

- 日時表示がずれる問題を修正
- 検索BOXで強制終了する問題を修正


### 2026-04-01 共有フォルダ、NASに対応

 - UNC/NAS 上の監視フォルダに対応
   (*.wb対応は考慮中)

### 2026-03-29 ドラッグ＆ドロップ対応

  - 画像フォルダ、WhiteBrowser の `*.wb` のD&Dに対応しました。

## このアプリでできること

- 指定したフォルダから動画を取り込み
- サムネイルを自動で作成します
- 動画リストから再生等の様々な機能

## WhiteBrowser互換機能

#### 現在開発中

- タグ機能
- スキン対応は順次増やします
- ブックマーク

## WhiteBrowserに出来てこのアプリでできないこと

- スタイル
- タグバー
- タグレット
- ジェスチャー
- キースクリプト
- コマンドライン

## ダウンロード

- https://github.com/T-Hamada0101/IndigoMovieManager_fork/releases
- [初めて使う方向けの手順](Docs/forHuman/簡単マニュアル_ReleaseAssetsからダウンロードして使い始めるまで_2026-03-29.md)

## 使い始め方

1. Release の `Assets` からアプリ本体 ZIP をダウンロードします。
2. ZIP を展開します。
3. `IndigoMovieManager.exe` を起動します。
4. 既存の `*.wb` を使うか、動画フォルダをドロップして新しい DB を作成します。

## WhiteBrowser から移行する方へ

- 元の WhiteBrowser DB を直接使わず、必ずコピーした `*.wb` を使ってください
- 既存 DB を使う時は、その `*.wb` をメイン画面へドラッグ＆ドロップすると開けます
- 新しく始める時は、動画フォルダをメイン画面へドラッグ＆ドロップしてください
[WhiteBrowser併用時の詳しい注意点](Docs/Gemini/Migration_from_WhiteBrowser_Notes_2026-03-25.md)

## 動作に必要なもの

- Windows
- `.NET 8 Desktop Runtime`
   https://dotnet.microsoft.com/ja-jp/download/dotnet/thank-you/runtime-8.0.25-windows-x64-installer



## 配布内容

- アプリ本体
- 必要な DLL
- `rescue-worker`

`rescue-worker` はアプリ本体 ZIP の中に同梱されています。

## 困った時

- 起動しない時は `.NET 8 Desktop Runtime` の有無を確認してください
- [困った時](Docs/forHuman/簡単マニュアル_ReleaseAssetsからダウンロードして使い始めるまで_2026-03-29.md)

## 開発者向け情報

開発向け README は次を見てください。

- [README_2026-03-28.md](README_2026-03-28.md)
- [開発者向けリポジトリ目標と要件_2026-04-04.md](リポジトリ目標と要件_2026-04-04.md)

