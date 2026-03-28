# IndigoMovieManager Architecture

最終更新日: 2026-03-28

## 1. この文書の役割

この文書は、このコードベースに新しく入る人が「どのプロジェクトが何を担当しているか」をつかむための地図です。

目的

- どこから読めばよいか分かる
- どのファイルが正規入口か分かる
- 触ってよい境界と、壊しやすい境界が分かる

実装優先度の正本は、別途 **[AI向け_現在の全体プラン_workthree_2026-03-20.md](../../AI向け_現在の全体プラン_workthree_2026-03-20.md)** を見てください。

## 2. まず理解する考え方

このリポジトリは、大きく 4 つに分けて考えると追いやすいです。

1. UI と本体制御
2. 監視とファイル索引
3. サムネイル生成
4. DB と保存先

## 3. 全体の大きな責務

### 3.1 UI と本体制御

主な場所

- `Views/Main/MainWindow.xaml`
- `Views/Main/MainWindow.xaml.cs`
- `ViewModels/MainWindowViewModel.cs`

役割

- 画面を出す
- DB を開く
- 一覧を表示する
- 監視やサムネイル常駐処理を起動する

最初の見方

- `MainWindow.xaml.cs` は司令塔
- `MainWindowViewModel.cs` は表示データの整理役
- 最初は「全部読む」より「どこで起動してどこで一覧が出るか」を見る

### 3.2 監視とファイル索引

主な場所

- `Watcher/MainWindow.Watcher.cs`
- `Watcher/MainWindow.WatcherEventQueue.cs`
- `Watcher/MainWindow.WatchScanCoordinator.cs`
- `Watcher/IndexProviderFacade.cs`
- `Watcher/FileIndexProviderFactory.cs`
- `src/IndigoMovieManager.FileIndex.UsnMft`

役割

- ファイル変化を拾う
- 候補を絞る
- MainDB へ反映する
- UI へ差分反映する

最初の見方

- `Watcher` は「監視イベントをどう処理するか」
- `FileIndex.UsnMft` は「高速に候補を探す実装」
- まずは `MainWindow.Watcher.cs` と `WatchScanCoordinator.cs` を見る

### 3.3 サムネイル生成

主な場所

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `Thumbnail/AppThumbnailCreationServiceFactory.cs`
- `Thumbnail/ThumbnailCreationService.cs`
- `src/IndigoMovieManager.Thumbnail.Engine`
- `src/IndigoMovieManager.Thumbnail.Queue`
- `src/IndigoMovieManager.Thumbnail.RescueWorker`

役割

- サムネイル生成依頼を積む
- QueueDB からジョブを取る
- 実際に画像を生成する
- 失敗を FailureDb へ逃がす
- RescueWorker で救済する

最初の見方

- `ThumbnailCreationServiceFactory`
- `IThumbnailCreationService`
- `ThumbnailCreateArgs`

この 3 つが公開入口です。

`ThumbnailCreationService.cs` は内部実装なので、最初からそこを直接触る前提で読まない方が安全です。

### 3.4 DB と保存先

主な場所

- `DB/SQLite.cs`
- `Data/MainDbMovieReadFacade.cs`
- `src/IndigoMovieManager.Thumbnail.Runtime/AppLocalDataPaths.cs`

役割

- メイン DB の読み書き
- 読み取り hot path の整理
- QueueDB / FailureDb / ログ保存先の規約管理

最初の見方

- `DB/SQLite.cs` はメイン DB の基本入口
- `MainDbMovieReadFacade.cs` は読み取り導線の整理役
- 保存先ルールは `AppLocalDataPaths.cs` で確認する

## 4. プロジェクト単位の見取り図

### 4.1 ルート直下の本体

- `IndigoMovieManager.csproj`
  - WPF アプリ本体
  - `net8.0-windows`
  - `x64`

### 4.2 `src/` 配下の分離プロジェクト

- `IndigoMovieManager.Thumbnail.Contracts`
  - 共通契約
- `IndigoMovieManager.Thumbnail.Engine`
  - 生成ロジック本体
- `IndigoMovieManager.Thumbnail.Queue`
  - QueueDB / FailureDb / queue pipeline
- `IndigoMovieManager.Thumbnail.Runtime`
  - 実行時共通ルール
- `IndigoMovieManager.Thumbnail.RescueWorker`
  - 救済 worker
- `IndigoMovieManager.FileIndex.UsnMft`
  - ファイル索引実装

### 4.3 テスト

- `Tests/IndigoMovieManager.Tests`

最初に押さえるポイント

- テストは仕様確認にも使える
- 特に境界系のテストは「今の正規入口」を教えてくれる

## 5. 主要フロー

### 5.1 起動から一覧表示

1. アプリが起動する
2. `MainWindow` が前回状態を復元する
3. メイン DB から一覧情報を読む
4. `MainWindowViewModel` が表示用に整える
5. 監視やサムネイル常駐処理が走り始める

### 5.2 監視から DB 反映

1. Watcher がイベントを受ける
2. イベントを queue 化して整理する
3. FileIndex 系で候補を絞る
4. MainDB に登録する
5. UI へ差分反映する
6. 必要ならサムネイル生成キューへ積む

### 5.3 サムネイル生成から救済

1. QueueDB にジョブを積む
2. `CheckThumbAsync` が queue processor を回す
3. `ThumbnailQueueProcessor` がジョブを取得する
4. `IThumbnailCreationService` で生成する
5. 失敗は FailureDb に記録する
6. RescueWorker が救済し、結果を UI に戻す

## 6. 最初に追うファイル

全部を一気に読むより、この順がおすすめです。

1. `Views/Main/MainWindow.xaml.cs`
2. `ViewModels/MainWindowViewModel.cs`
3. `Watcher/MainWindow.Watcher.cs`
4. `Watcher/MainWindow.WatchScanCoordinator.cs`
5. `Thumbnail/MainWindow.ThumbnailCreation.cs`
6. `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`

## 7. 壊しやすい境界

### 7.1 サムネイル生成

守ること

- `Factory + Interface + Args` の入口を守る
- `ThumbnailCreationService` を直接 new しない
- validator / coordinator の責務を service 本体へ戻さない

### 7.2 Watcher

守ること

- watch 起点の重い処理を UI スレッドへ戻さない
- 監視イベント処理と UI 反映を混ぜすぎない

### 7.3 MainDB 読み取り

守ること

- 読み取り hot path を無秩序に増やさない
- `DB/SQLite.cs` 直叩きだけで解決しようとしない

## 8. 症状別の入口

### 8.1 一覧が重い

見る場所

- `Views/Main/MainWindow.xaml.cs`
- `ViewModels/MainWindowViewModel.cs`
- `Data/MainDbMovieReadFacade.cs`

### 8.2 監視がおかしい

見る場所

- `Watcher/README.md`
- `Watcher/MainWindow.Watcher.cs`
- `Watcher/MainWindow.WatchScanCoordinator.cs`

### 8.3 サムネイルが出ない

見る場所

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`
- `src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs`
- `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`

## 9. 次に読む文書

この文書の次は、目的別に次を読むとつながります。

- 環境構築を確認したい
  - **[DevelopmentSetup_2026-02-28.md](DevelopmentSetup_2026-02-28.md)**
- DB の役割を理解したい
  - **[DatabaseSpec_2026-02-28.md](DatabaseSpec_2026-02-28.md)**
- 今の優先テーマを知りたい
  - **[AI向け_現在の全体プラン_workthree_2026-03-20.md](../../AI向け_現在の全体プラン_workthree_2026-03-20.md)**

## 10. 最後に

最初から設計全体を完全理解しようとしなくて大丈夫です。

まずは

- 起動
- 一覧
- 監視
- サムネイル

の順に 1 本ずつ流れを追ってください。

このリポジトリは大きいですが、入口を間違えなければかなり読みやすくなっています。
