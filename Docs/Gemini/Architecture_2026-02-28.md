# 🏰 アーキテクチャ爆速概要！ (2026-03-28 深淵の覚醒版) 🏰

クハハ……このアプリがどう進化し、どう組み上がっているのか！？
我、**【星辰を統べし双生子（ジェミニ）】** が、その全貌を解き明かす「深淵のアーキテクチャ」を解き明かしてやろう！🚀✨
どこに何があるか分かれば、改造もリファクタリングも全宇宙最速で自由自在だぜ！⚡

## 1. 🧱 コンポーネント・ブロック（疎結合という名の自由）
我々の戦力（コード）は、もはや単一の巨大な塊ではない。役割ごとに「魂（DLL）」が切り離されているのだ！🛡️

- **華麗なるUI層（聖域）**: `MainWindow.xaml`（ユーザーが見るすべて！）
- **司令塔（MainWindow）**: `MainWindow.xaml.cs` (partial class 群)
  - `MainWindow.ThumbnailPaths`: パス解決の理を一元管理する。
  - `MainWindow.ThumbnailFailureSync`: 救済されたサムネイルを UI へ同期する監視役。
- **UIのバランサー(ViewModel層)**: `ModelViews/MainWindowViewModel.cs`
- **データアクセス層（DBの守護者）**: `DB/SQLite.cs`
- **🚀 独立機能部隊（Service層の覚醒）**:
  - `Thumbnail/ThumbnailCreationService.cs`: OpenCV や FFMediaToolkit と格闘する、サムネイル生成の達人！
  - **`IndigoMovieManager.Thumbnail.Queue` (DLL)**: サムネイル作成オーダーを管理する深淵のキュー・マスター！
  - **`IndigoMovieManager.Thumbnail.RescueWorker` (EXE)**: 【NEW】通常生成に失敗した、奈落の底に落ちた動画を救済する最後の希望！
  - **`Watcher/EverythingFolderSyncService`**: Everything の IPC 通信を操り、爆速でファイル照合を行う特務機関！

## 2. 📂 聖戦の軍勢（プロジェクト一覧：src / tests）
この宇宙（ソリューション）を構成する、精鋭部隊（プロジェクト）たちの真の名前とその役割をここに記そう！🛡️

### ☄️ **src/** 配下：主戦力（Core Units）
- **`IndigoMovieManager`**: 全ての中心たる旗艦アプリ（WPF/Net8）。司令塔にして器だ！
- **`IndigoMovieManager.Thumbnail.Queue` (DLL)**: サムネイル作成オーダーを管理する深淵のキュー・マスター。
- **`IndigoMovieManager.Thumbnail.RescueWorker` (EXE)**: 通常の生成に失敗した動画を救い出す「最後の聖騎士（ホーリーナイト）」。
- **`IndigoMovieManager.Thumbnail.Engine` (DLL)**: OpenCV や FFMediaToolkit の力を直接行使する「禁忌の魔術師」。
- **`IndigoMovieManager.Thumbnail.Contracts` (DLL)**: 異なる部隊（プロジェクト）を繋ぐ不変の理（インターフェース定義）。
- **`IndigoMovieManager.Thumbnail.Runtime` (DLL)**: 実行時の様々な理（ユーティリティ、ヘルパー）を司る補助部隊。
- **`IndigoMovieManager.FileIndex.UsnMft` (DLL)**: OS の深淵（USN ジャーナル / MFT）からファイル情報を高速に吸い上げる特務分隊！

### 🧪 **tests/** 配下：真理の検証者（Verification Units）
- **`IndigoMovieManager.Tests`**: 全ての術式（コード）が正しく動作するかを神の視点で裁く、絶対的な審判所（Unit/Integration Tests）だ！

## 3. 🌊 データと処理の流れ（驚異的な進化の軌跡）

### 🎇 起動 〜 一覧表示まで
1. `MainWindow` 顕現！初期化の術式を起動！
2. `Properties.Settings` から「刻まれた記憶（前回DBパス）」を呼び起こす！
3. `OpenDatafile` で DB の中身を一気に吸い上げ、`MemoryCache` という名の星空に展開！✨
4. `MainVM.MovieRecs` にデータが反映され、一瞬にして全動画の「姿（サムネイル）」が描画される！完成！

### 📥 【超進化】監視部隊の爆速ハイブリッドスキャン
1. アプリ内での「更新」時、**Everything IPC/EverythingLite** が火を噴く！🔥
2. サムネイルフォルダから「作成済みサムネイル」を Everything 経由で一瞬にして取得！
3. 動画と突き合わせ、「新顔」だけを抽出し、DB 登録と同時にサムネキューへ直行させる！
4. **完璧な自己修復**: DB に登録されていてもサムネがないものは、再びキューへ投げ込まれる……逃げ場はないぜ！🤖✨

### 🖼️ 【超進化】サムネ救済ロード（Rescue Lane）
1. 通常の `ThumbnailCreationService` が生成に失敗すると、その魂は **`FailureDb`** へと送られる……。
2. 背景で静かに息づく **`RescueWorker`** が、失敗した動画を再び手に取り、禁忌の術式で再生成を試みる！🛡️
3. **`ThumbnailFailureSync`** が救済の成功を検知し、UI はリアルタイムで「希望（サムネイル）」へと書き換わるのだ！🎊

## 3. 🤔 今のアーキテクチャの「オイシイところ」と「ヤバいところ」
- **オイシイ**:
  - 激重な処理（サムネ、監視）が DLL や別プロセスに切り離され、UI スレッドは常に「絶望を追い越す速度」でサクサク動く！💨
  - `EverythingLite` の導入により、外部ツールなしでも爆速検索が可能になった！
  - 失敗した動画を見捨てない「救済レーン」により、生成率はもはや 100% へ肉薄している！
- **ヤバい**:
  - アプリ本体にはまだ COM 参照 (Shell32 等) という名の「古の呪い」が残っており、ビルドには MSBuild の力が必要だ……💀 (対策済み：[DLL分離プラン](../Architecture_DLL_Separation_Plan_2026-03-02.md) 参照！)

## 4. 🚀 未来への発展計画（次なる野望）
1. **完全なるDLL化の完遂**: コアロジックを UI から完全に引き剥がし、CLI や Web サービスとしても使い回せる「最強の器」を完成させる！🗡️
2. **スキン機能の革命**: WebView2 を導入し、UI 自体をもはや「一つの宇宙（スキン）」として自在に書き換えられるようにする！
3. **自動テストという名の鉄壁**: この爆速アーキテクチャを未来永劫守るため、あらゆる術式をテストコードで固めるぜ！🌎

UI とロジックを切り離し、永遠の爆速開発ループへ……共に至ろうぞ！🎉🔥
