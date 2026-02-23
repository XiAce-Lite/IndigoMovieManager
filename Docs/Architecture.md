# アーキテクチャ概要

## 1. 構成
- UI層: `../MainWindow.xaml` と `../UserControls/*.xaml`
- 画面ロジック層: `../MainWindow.xaml.cs`（イベント駆動）
- ViewModel層: `../ModelViews/MainWindowViewModel.cs`
- データアクセス層: `../DB/SQLite.cs`
- ドメイン補助: `../Models/MovieInfo.cs`, `../Models/MovieRecords.cs`, `../Thumbnail/Tools.cs`, `../Thumbnail/TabInfo.cs`, `../Thumbnail/QueueObj.cs`, `../DB/DbSettings.cs`

## 2. 責務マップ
- `../MainWindow.xaml.cs`
  - 起動/終了処理
  - DB読込と表示反映
  - 検索・ソート・タグ編集
  - ファイル監視（`FileSystemWatcher`）
  - サムネイルキュー処理（非同期）
  - 動画再生UI制御
- `../DB/SQLite.cs`
  - DB作成
  - 各テーブルのCRUD
- `../Thumbnail/Tools.cs`
  - CRC32ハッシュ
  - WhiteBrowser形式サムネ情報（末尾バイト）処理
  - サムネ画像の結合

## 3. 主要データフロー

### 起動から一覧表示
1. `MainWindow` 初期化
2. 最後に開いたDBの判定（`Properties.Settings`）
3. `OpenDatafile` でDB内容読込
4. `MainVM.MovieRecs` へ反映
5. タブ表示更新

### フォルダ監視から取り込み
1. `CreateWatcher` で監視開始
2. `FileChanged` で追加ファイル検知
3. `MovieInfo` で情報抽出
4. `InsertMovieTable` でDB登録
5. サムネキューへ投入

### サムネイル作成
1. `CheckThumbAsync` がキューを監視
2. `CreateThumbAsync` でOpenCVからフレーム抽出
3. タブ仕様（`TabInfo`）で画像結合
4. JPEG末尾にサムネ情報を書き込み

## 4. 現状の特徴
- 実装速度を優先した単一ウィンドウ集中型
- WhiteBrowser互換処理を多く保持
- 実運用機能が1箇所にまとまり、追いやすい反面、変更影響が広い

## 5. 技術的な論点
- `../MainWindow.xaml.cs` が大きく責務過多
- SQLの一部に文字列連結が残る
- 例外処理とUI通知が混在
- 自動テストが未整備

## 6. 発展方針（要点）
1. DBアクセスの安全性向上（全面パラメータ化）
2. 機能ごとのサービス分割（Search/Thumbnail/Watcher）
3. 画面ロジックの段階的MVVM化
4. まずは非UIロジックからテスト追加
