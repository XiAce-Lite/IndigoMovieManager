# IndigoMovieManager Project Files And Folders

最終更新日: 2026-04-01

## 1. この文書の目的

この文書は、IndigoMovieManager に新しく入る人が

- どこに何があるか
- どのフォルダから読めばよいか
- 変更したい内容ごとに、どの場所を見ればよいか

を素早く確認するための配置ガイドです。

設計の背景や責務分担は `Architecture_2026-02-28.md` に譲り、
この文書では「場所を迷わないこと」を優先します。

## 2. まず覚えるべき場所

最初に覚えるべき場所は次の 8 つです。

- `Views/Main/`
  - メイン画面です
- `ViewModels/`
  - 一覧表示や検索、ソートなどの表示状態です
- `Watcher/`
  - 監視、差分反映、MainDB 登録まわりです
- `Thumbnail/`
  - 本体アプリ側のサムネイル入口です
- `src/IndigoMovieManager.Thumbnail.Engine/`
  - サムネイル生成ロジック本体です
- `src/IndigoMovieManager.Thumbnail.Queue/`
  - QueueDB、FailureDb、processor です
- `Data/`
  - MainDB 読み書き導線の facade 群です
- `DB/`
  - SQLite 基盤です

## 3. ルート直下の主な項目

### 3.1 アプリ本体と設定

- `IndigoMovieManager.csproj`
  - 本体 WPF アプリのプロジェクトです
- `IndigoMovieManager.sln`
  - ソリューションです
- `App.xaml`
  - アプリ共通の WPF 設定です
- `App.xaml.cs`
  - 起動処理と全体初期化です
- `App.config`
  - アプリ構成ファイルです
- `Properties/launchSettings.json`
  - デバッグ起動設定です

### 3.2 主要フォルダ

- `Views/`
  - WPF 画面です
- `ViewModels/`
  - UI の表示状態です
- `UserControls/`
  - 再利用 UI 部品です
- `BottomTabs/`
  - 下部タブ単位の UI ロジックです
- `UpperTabs/`
  - 上部タブ単位の UI ロジックです
- `Watcher/`
  - 監視、差分反映、Everything 連携です
- `Thumbnail/`
  - 本体側のサムネイル処理入口です
- `Data/`
  - facade ベースの DB 入口です
- `DB/`
  - SQLite 基盤と既存 DB アクセスです
- `Models/`
  - ドメイン寄りの型です
- `Infrastructure/`
  - converter、補助クラス、validation です
- `Themes/`
  - テーマと色定義です
- `Startup/`
  - 起動時ロードの補助クラスです
- `src/`
  - 分離済みのサブプロジェクト群です
- `Tests/`
  - テストプロジェクトです
- `Docs/`
  - 人向け、AI向け、補助資料です
- `scripts/`
  - 配布、検証、補助スクリプトです
- `tools/`
  - ffmpeg、sqlite などの同梱ツールです

## 4. `Views` と UI まわり

### 4.1 画面の入口

- `Views/Main/MainWindow.xaml`
  - メイン画面の XAML です
- `Views/Main/MainWindow.xaml.cs`
  - メイン画面のシェル兼オーケストレータです
- `Views/Main/MainWindow.Startup.cs`
  - 起動時の段階ロードです
- `Views/Main/MainWindow.Search.cs`
  - 検索まわりです
- `Views/Main/MainWindow.Selection.cs`
  - 選択追従です

### 4.2 タブまわり

- `BottomTabs/`
  - 下部タブごとの partial class、View、Presenter です
- `UpperTabs/`
  - 上部タブごとの partial class、View、Presenter です

### 4.3 表示状態

- `ViewModels/MainWindowViewModel.cs`
  - 一覧、検索、ソート、表示コレクションの中心です
- `ViewModels/ThumbnailProgressViewState.cs`
  - サムネイル進捗表示状態です

## 5. 監視と DB 反映まわり

### 5.1 最初に見るファイル

- `Watcher/MainWindow.Watcher.cs`
  - 監視処理の本体です
- `Watcher/MainWindow.WatchScanCoordinator.cs`
  - watch 起点の per-file / per-batch 調停です
- `Watcher/MainWindow.WatcherUiBridge.cs`
  - watch と UI の橋渡しです
- `Watcher/IndexProviderFacade.cs`
  - provider 切替と fallback の統一入口です
- `Watcher/FileIndexProviderFactory.cs`
  - provider 生成の入口です

### 5.2 DB 導線

- `Data/MainDbMovieReadFacade.cs`
  - MainDB 読み取り導線です
- `Data/MainDbMovieMutationFacade.cs`
  - MainDB 更新導線です
- `Data/WatchMainDbFacade.cs`
  - watch 系の MainDB 入口です
- `DB/SQLite.cs`
  - SQLite 基本アクセスです

## 6. サムネイルまわり

### 6.1 本体側の入口

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - 本体からサムネイル生成へ入る入口です
- `Thumbnail/MainWindow.ThumbnailQueue.cs`
  - queue 連携です
- `Thumbnail/AppThumbnailCreationServiceFactory.cs`
  - UI 層と Engine 層をつなぐ factory です

### 6.2 Engine

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationServiceFactory.cs`
  - Engine 側の正規入口です
- `src/IndigoMovieManager.Thumbnail.Engine/IThumbnailCreationService.cs`
  - 公開インターフェースです
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateWorkflowCoordinator.cs`
  - サムネイル生成本流です
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailEngineExecutionPolicy.cs`
  - エンジン実行順序と再試行判断です

### 6.3 Queue と救済

- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
  - QueueDB からジョブを実行する本体です
- `src/IndigoMovieManager.Thumbnail.Queue/QueueDb/`
  - QueueDB まわりです
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`
  - 救済 worker 本体です

## 7. `src/` 配下の分離プロジェクト

- `src/IndigoMovieManager.Thumbnail.Contracts/`
  - 共有契約型です
- `src/IndigoMovieManager.Thumbnail.Engine/`
  - 生成ロジック本体です
- `src/IndigoMovieManager.Thumbnail.Queue/`
  - QueueDB、FailureDb、processor です
- `src/IndigoMovieManager.Thumbnail.Runtime/`
  - 保存先や runtime 共通ルールです
- `src/IndigoMovieManager.Thumbnail.RescueWorker/`
  - 通常生成で失敗した動画を救済する別 EXE です
- `src/IndigoMovieManager.FileIndex.UsnMft/`
  - 高速ファイル索引の実装です

## 8. 実行時ファイルの保存先

### 8.1 LocalAppData

保存先は基本的に `%LOCALAPPDATA%\{AppName}\` 配下です。

- `logs/`
  - 実行時ログです
- `QueueDb/`
  - QueueDB です
- `FailureDb/`
  - FailureDb です
- `RescueWorkerSessions/`
  - 救済 worker の一時ファイルです

### 8.2 WhiteBrowser 互換データ

- `*.wb`
  - メイン DB です
- `.wb` と同じフォルダ内の `.jpg`
  - サムネイル画像です
- `動画名.#ERROR.jpg`
  - エラーマーカーです

## 9. 作業テーマ別に見る場所

### 9.1 一覧や検索を直したい

- `Views/Main/`
- `ViewModels/MainWindowViewModel.cs`
- `UpperTabs/`
- `BottomTabs/`

### 9.2 監視を直したい

- `Watcher/`
- `Data/WatchMainDbFacade.cs`
- `DB/SQLite.cs`
- `src/IndigoMovieManager.FileIndex.UsnMft/`

### 9.3 サムネイルを直したい

- `Thumbnail/`
- `src/IndigoMovieManager.Thumbnail.Engine/`
- `src/IndigoMovieManager.Thumbnail.Queue/`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/`

### 9.4 ドキュメントを探したい

- `Docs/forHuman/`
  - 人向けの入口です
- `Docs/forAI/`
  - 実装計画、調査、レビュー結果です
- `Watcher/README.md`
  - watch 系の入口です
- `Thumbnail/README.md`
  - thumbnail 系の入口です

## 10. この文書の次に読むもの

この文書の次は、次の順で読むと理解がつながります。

1. `ProjectOverview_2026-03-29.md`
   - 全体像の正本です
2. `Architecture_2026-02-28.md`
   - 責務分担を確認します
3. `DatabaseSpec_2026-02-28.md`
   - DB の役割と保存先を確認します
4. `../../Watcher/README.md`
   - watch 系の入口です
5. `../../Thumbnail/README.md`
   - thumbnail 系の入口です

## 11. まとめ

このコードベースで最初に迷わないためのコツは次の 3 つです。

1. まず `Views/Main`、`Watcher`、`Thumbnail` の 3 箇所を覚えること
2. DB 導線は `Data/` と `DB/` の役割を分けて見ること
3. サムネイルは `Thumbnail/` と `src/IndigoMovieManager.Thumbnail.*` を分けて追うこと
