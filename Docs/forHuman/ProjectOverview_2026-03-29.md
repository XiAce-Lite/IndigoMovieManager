# IndigoMovieManager Project Overview

最終更新日: 2026-03-29

## 1. この文書の目的

この文書は、IndigoMovieManager を初めて触る人が

- このアプリは何をするものか
- どのプロジェクトやフォルダに何があるか
- どこから読めばよいか
- いま何を優先して理解すべきか

を短時間でつかむための入口資料です。

詳細仕様や実装の深掘りは別資料に譲り、この文書では「迷わないための地図」に徹します。

## 2. 最初に読む順番

このコードベースに新しく入る人は、次の順番で読むと入りやすいです。

1. `DevelopmentSetup_2026-02-28.md`
   - 開発環境、起動方法、ビルドの前提を確認します。
2. `ProjectFilesAndFolders_2026-03-28.md`
   - どのファイルやフォルダがどこにあるかを確認します。
3. この `ProjectOverview_2026-02-28.md`
   - 全体像と読み進め方をつかみます。
4. `Architecture_2026-02-28.md`
   - プロジェクト単位の責務を理解します。
5. `DatabaseSpec_2026-02-28.md`
   - DB の役割と保存先を確認します。

AI や実装担当者は、上記に加えてリポジトリ直下の `AI向け_現在の全体プラン_workthree_2026-03-20.md` を先に確認してください。

## 3. このアプリは何をするか

IndigoMovieManager は、WhiteBrowser 互換を重視しながら動画一覧、検索、監視、サムネイル生成を扱う WPF デスクトップアプリです。

現在の主な特徴は次の通りです。

- メイン DB は WhiteBrowser 互換の `*.wb` を正本として扱う
- サムネイル生成は UI と切り離し、QueueDB を使って非同期で進める
- 通常生成で失敗した動画は FailureDb と RescueWorker で救済する
- 監視と候補抽出は Watcher と FileIndex 系に分離し、UI の詰まりを減らしている

## 4. 大きな構成

このコードベースに新しく入る人は、まず「本体 EXE」と「補助プロジェクト群」に分けて考えると分かりやすいです。

### 4.1 本体

- `IndigoMovieManager`
  WPF アプリ本体です。画面、DB 切り替え、監視開始、サムネイル常駐処理の起動などを担当します。

### 4.2 サムネイル系

- `src/IndigoMovieManager.Thumbnail.Engine`
  サムネイル生成の中核です。`ThumbnailCreationServiceFactory` と `IThumbnailCreationService` が入口です。
- `src/IndigoMovieManager.Thumbnail.Queue`
  QueueDB、FailureDb、`ThumbnailQueueProcessor` を持つキュー制御本体です。
- `src/IndigoMovieManager.Thumbnail.Runtime`
  ローカル保存先や実行時共通ルールを持ちます。
- `src/IndigoMovieManager.Thumbnail.Contracts`
  プロジェクト間で共有する契約型を持ちます。
- `src/IndigoMovieManager.Thumbnail.RescueWorker`
  通常生成で失敗した動画を救済する別 EXE です。

### 4.3 監視・ファイル索引系

- `Watcher/`
  監視、差分反映、MainDB 登録、UI 反映の入口です。
- `src/IndigoMovieManager.FileIndex.UsnMft`
  高速なファイル索引処理の実装本体です。

### 4.4 DB・UI 系

- `DB/`
  メイン DB の接続、スキーマ確認、基本アクセスを担当します。
- `Views/`
  WPF の画面です。メイン画面は `Views/Main/` にあります。
- `ViewModels/`
  UI 表示用の状態や一覧更新ロジックを持ちます。

## 5. 重要なデータの置き場所

このプロジェクトは「1つの DB だけ」で完結しません。役割ごとに分かれています。

### 5.1 メイン DB

- 形式: `*.wb`
- 役割: 動画一覧、履歴、設定などの正本
- 注意: WhiteBrowser 互換を壊さないことが前提です

### 5.2 QueueDB

- 形式: `*.queue.imm`
- 保存先: `%LOCALAPPDATA%\{AppName}\QueueDb\`
- 役割: サムネイル生成待ちジョブの管理

### 5.3 FailureDb

- 形式: `*.failure.imm`
- 保存先: `%LOCALAPPDATA%\{AppName}\FailureDb\`
- 役割: 失敗したサムネイル生成と救済状態の管理

### 5.4 ログ

- 保存先: `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\logs\`
- 役割: 実行時の調査、原因切り分け

## 6. まず覚えるべきコード上の入口

このコードベースに新しく入る人が最初に追うべき入口は、次の 6 つです。

1. `Views/Main/MainWindow.xaml.cs`
   - メイン画面のライフサイクル管理
2. `ViewModels/MainWindowViewModel.cs`
   - 一覧表示と UI 用データの中心
3. `Watcher/MainWindow.Watcher.cs`
   - 監視と再読込の中心
4. `Watcher/MainWindow.WatchScanCoordinator.cs`
   - watch 起点の処理整理
5. `Thumbnail/MainWindow.ThumbnailCreation.cs`
   - `CheckThumbAsync` から Queue 処理へつなぐ入口
6. `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
   - QueueDB からジョブを取得して実行する本体

サムネイル生成の設計を理解したい場合は、さらに次の 3 つを追うとつながります。

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationServiceFactory.cs`
- `src/IndigoMovieManager.Thumbnail.Engine/IThumbnailCreationService.cs`
- `Thumbnail/ThumbnailCreationService.cs`

## 7. 処理の流れを最短で理解する

### 7.1 起動から一覧表示まで

1. アプリが起動する
2. `MainWindow` が前回状態や DB を復元する
3. メイン DB から一覧データを読み込む
4. `MainWindowViewModel` が UI 表示用に整える
5. 必要に応じて監視やサムネイル常駐処理が起動する

### 7.2 監視から DB 反映まで

1. Watcher がファイル変化を検知する
2. FileIndex 系で候補を絞る
3. `WatchScanCoordinator` で反映単位を整理する
4. MainDB へ登録する
5. UI へ差分反映する
6. 必要ならサムネイル生成キューへ積む

### 7.3 サムネイル生成から救済まで

1. ジョブが QueueDB に積まれる
2. `CheckThumbAsync` が `ThumbnailQueueProcessor` を回す
3. `ThumbnailQueueProcessor` がジョブを取得する
4. `IThumbnailCreationService` 経由でサムネイルを生成する
5. 失敗時は FailureDb へ記録する
6. RescueWorker が救済し、成功結果を UI へ同期する

## 8. 現在の実装方針

2026-03-20 時点の正本は `AI向け_現在の全体プラン_workthree_2026-03-20.md` です。
このコードベースに新しく入る人は、優先順位だけ先に覚えれば十分です。

1. サムネイル生成入口の整理を崩さない
2. rescue 導線が通常動画のテンポを壊していないか確認する
3. Queue の観測性を最低限保つ
4. `ERROR` 動画向けの明示 UI を整える
5. 一覧更新や監視起点の UI テンポを良くする

特にサムネイル周りでは、次の公開入口を基本とします。

- `ThumbnailCreationServiceFactory`
- `IThumbnailCreationService`
- `ThumbnailCreateArgs`
- `ThumbnailBookmarkArgs`

「とりあえず service 本体を直接いじる」より、この入口を守る方が大事です。

## 9. 作業別の読み始め

### 9.1 一覧や UI が重い

次を順に見てください。

- `Views/Main/MainWindow.xaml.cs`
- `ViewModels/MainWindowViewModel.cs`
- `Watcher/MainWindow.WatcherUiBridge.cs`
- `AI向け_現在の全体プラン_workthree_2026-03-20.md`

### 9.2 監視や DB 登録がおかしい

次を順に見てください。

- `Watcher/README.md`
- `Watcher/MainWindow.Watcher.cs`
- `Watcher/MainWindow.WatchScanCoordinator.cs`
- `DB/SQLite.cs`

### 9.3 サムネイルが出ない

次を順に見てください。

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`

## 10. この文書で扱わないもの

次の内容は、この文書では深掘りしません。

- 細かい SQL 定義
- 各エンジンの詳細な比較
- RescueWorker の個別動画向け調整
- 過去の試行錯誤の履歴

それらは個別ドキュメントへ分離されています。まずは全体像をつかんでから必要な資料に進んでください。
