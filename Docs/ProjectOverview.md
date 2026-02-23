# IndigoMovieManager プロジェクト理解ガイド

## 1. 目的
このドキュメントは、`IndigoMovieManager` を短時間で理解し、次の開発に入るための入口です。

## 2. まず読む順番（最短）
1. `README.md`
2. `../IndigoMovieManager.csproj`（技術スタックと依存関係）
3. `../MainWindow.xaml`（画面構造）
4. `../MainWindow.xaml.cs`（主要ロジック）
5. `../DB/SQLite.cs`（DBアクセス）
6. `../Thumbnail/Tools.cs` と `../Thumbnail/TabInfo.cs`（サムネイル関連）
7. `../ModelViews/MainWindowViewModel.cs`（画面バインドの入口）

## 3. プロジェクトの全体像
- 種別: WPFデスクトップアプリ（`net8.0-windows`）
- 主用途: 動画の管理（一覧、タグ、検索、スコア、再生、サムネイル）
- 互換方針: WhiteBrowser のDB/サムネイル仕様をできるだけ踏襲
- 永続化: SQLite（`movie`, `bookmark`, `history`, `watch`, `system` など）

## 4. 主要機能
- 管理DBの新規作成・読込
- 5種類の表示タブ（Small / Big / Grid / List / 5x2）
- タグ編集、スコア更新、ファイル操作（コピー・移動・削除・リネーム）
- 検索履歴・ソート
- フォルダ監視による自動取込
- サムネイル作成（等間隔 / 手動）
- ブックマーク作成

## 5. 主要ファイルと責務
- `../MainWindow.xaml.cs`: 画面イベント、検索・ソート、再生、監視、サムネイル生成まで一括管理
- `../DB/SQLite.cs`: テーブル作成とCRUD
- `../Thumbnail/Tools.cs`: CRC32、サムネイル情報の読み書き、画像結合
- `../Models/MovieInfo.cs`: 動画メタ情報の取得（OpenCvSharp）
- `../ModelViews/MainWindowViewModel.cs`: UIバインド対象の集約
- `../UserControls/*.xaml`: タブごとの表示部品

## 6. 今後の理解ポイント（優先）
1. `../MainWindow.xaml.cs` の機能境界を把握する
2. `../DB/SQLite.cs` のSQL生成方法を把握する
3. `CreateThumbAsync` と `CheckThumbAsync` のキュー処理を把握する
4. `system` テーブルと `Properties.Settings` の役割分担を把握する

## 7. 関連ドキュメント
- `DevelopmentSetup.md`
- `Architecture.md`
- `DatabaseSpec.md`
- `Implementation Plan.md`
