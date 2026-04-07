# 🚀 IndigoMovieManager プロジェクト完全理解ガイド！ (2026-03-28 最新版) 🚀

やっほー！このドキュメントは、超絶進化を遂げた `IndigoMovieManager` の海に飛び込むための「最新のダイビングガイド」だよ！🤿✨
これさえ読めば、次にどこを開発すればいいか一瞬で見えるようになるぜ！最強の名に恥じないコードを一緒に作っていこうぜ！👊🔥

まじめな解説はこちら： **[README_2026-03-28.md](../../README_2026-03-28.md)**



## 1. 🎯 この文書の目的
ただ一つ！**「この進化したアプリの全体像を爆速で理解して、すぐに開発のスタートダッシュを切る」**こと！🏃💨
- このアプリ、結局何してくれるの？
- 大事なコードはどこに眠ってるの？
- 最初にどこを叩けばいいの？
その答えを全部ここに詰め込んだぞ！

## 2. 🗺️ 最初に読むべき「勝利へのロードマップ」
このコードベースに新しく入る君は、この順番でバイブルを読み解いてくれ！✨

1. **[DevelopmentSetup_2026-02-28.md](../forHuman/DevelopmentSetup_2026-02-28.md)**
   - 開発の「儀式」！環境構築とビルドの作法を叩き込め！⚒️
2. **[ProjectFilesAndFolders_2026-03-28.md](ProjectFilesAndFolders_2026-03-28.md)**
   - 迷宮の地図！どこに何があるか一発で把握しよう！🧭
3. **[ProjectOverview_2026-03-28.md](ProjectOverview_2026-03-28.md)** (今読んでるコレ！)
   - 全体の「魂」と「読み進め方」を掴むんだ！魂を燃やせ！🔥
4. **[Architecture_2026-02-28.md](../forHuman/Architecture_2026-02-28.md)**
   - 各プロジェクトの「責務」！誰が何をやるのか、裏側の連携を理解せよ！🏠
5. **[DatabaseSpec_2026-02-28.md](../forHuman/DatabaseSpec_2026-02-28.md)**
   - **三刀流**の SQLite ちゃん！データの保存先と役割をマスターせよ！⚔️

> [!IMPORTANT]
> AIや実装ゴリゴリ担当者は、リポジトリ直下の **[AI向け_現在の全体プラン_2026-04-07.md](../../AI向け_現在の全体プラン_2026-04-07.md)** を最優先でチェックだ！ここが現在の「正本」だぞ！

## 3. 🌍 IndigoMovieManager の正体とは？
伝説の「WhiteBrowser」をリスペクトしつつ、令和の最新技術で超絶進化した WPF デスクトップアプリだ！💪✨

- **DBの守護**: WhiteBrowser 互換の `*.wb` を魂の正本として扱う！📖
- **爆速サムネイル**: UIを一切止めない！QueueDB による完全非同期生成！🖼️
- **不屈の救済**: 生成に失敗しても諦めない！`FailureDb` と `RescueWorker` が何度でも立ち上がる！🚑
- **不眠不休の監視**: `Watcher` と高速索引（USN/MFT）が、ファイルの誕生を瞬時に検知する！👀

## 4. 📁 直感で分かる！主要機能フォルダ紹介
「どのプロジェクトが何？」って迷ったらここを見ろ！

まず「本体 EXE」と「補助プロジェクト群」に分けて考えると分かりやすいよ！💡

### 4.1 👑 アプリの心臓部（本体 EXE: `IndigoMovieManager/`）
WPFアプリ本体！画面、DB切り替え、監視の号令、サムネ生成の起動まで、すべての総司令部だ！
「ユーザーが触る部分」は全部ここに集約されているぞ！

### 4.2 🛠️ 頼れる仲間たち（補助プロジェクト群）
本体の司令を受けて、専門的な仕事を爆速でこなすプロフェッショナル集団だ！

#### 🖼️ 爆速サムネイル軍団 (`src/Thumbnail.../`)
- **`Engine`**: 生成の中核！錬金術師の工房だ！🧪
- **`Queue`**: **QueueDB**、**FailureDb**、そして守護神 `ThumbnailQueueProcessor` が住んでいる、キュー制御本体だ！
- **`RescueWorker`**: 失敗動画を救い出す特務部隊！別EXEとして暗躍するぞ！🥷
- **`Runtime`**: ローカルの保存先や、実行時の共通ルールを司る「現場監督」だ！🏗️
- **`Contracts`**: プロジェクト間で共有する「黄金の契約書（型定義）」が眠っているぞ！📜

#### ⚡ 監視・高速索引ユニット (`Watcher/` & `src/FileIndex.../`)
- `Watcher/`: 変化を検知し、MainDB・UIへ電光石火で叩き込む最前線！
- `src/FileIndex.UsnMft`: OSの深淵から情報を引っこ抜く爆速ファイル索引エンジン！🚀



#### 🎨 画面とロジック (`Views/` & `ViewModels/`)
- **`Views/`**: WPF の華やかな画面たち！メイン画面は `Views/Main/` に鎮座しているぞ！✨
- **`ViewModels/`**: 画面の裏で糸を引く黒幕！UIの状態管理や一覧更新のロジックを牛耳っている！🧠

#### 💾 データの要 (`DB/`)
- メインDBへの接続や基本操作を担当する、データの守護聖人だ！🛡️

---

## 5. ⚔️ 記憶装置：三刀流の SQLite ちゃん！
このアプリは3つのDBを使いこなす「三刀流」だ！

1. **メインDB (`*.wb`)**: 動画、履歴、設定を司る「真実の書」！互換性は絶対だ！💎
2. **キューDB (`*.queue.imm`)**: サムネ生成待ちジョブを管理する「最新の待機リスト」！📥
3. **失敗DB (`*.failure.imm`)**: 失敗を記録し、救済Workerが再挑戦するための「リベンジリスト」！🚑

> [!TIP]
> ログは `%LOCALAPPDATA%\IndigoMovieManager\logs\` に眠っている。何かあったらここを掘り起こせ！🔍

## 6. 🧐 どこからコードを読む？（おすすめエントリー）
迷ったらここを叩け！

1. **`Views/Main/MainWindow.xaml.cs`**: 総司令官。身の回りの世話を焼く。
2. **`ViewModels/MainWindowViewModel.cs`**: 画面と裏側を繋ぐ最強の架け橋！
3. **`Watcher/MainWindow.Watcher.cs`**: 監視の目！👀
4. **`Watcher/MainWindow.WatchScanCoordinator.cs`**: 監視（watch）起点の処理を華麗にさばく司令塔！📡
5. **`Thumbnail/MainWindow.ThumbnailCreation.cs`**: サムネ生成の呼び出し口！
6. **`ThumbnailQueueProcessor.cs`**: Queueからジョブをぶっこ抜く実務担当者！🔥

> [!TIP]
> **「サムネイル生成の設計」を極めたい強者へ！💎**
> 次の3つを順に追うと、生成の魔法の正体が見えてくるぞ！
> - **`src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationServiceFactory.cs`**
> - **`src/IndigoMovieManager.Thumbnail.Engine/IThumbnailCreationService.cs`**
> - **`Thumbnail/ThumbnailCreationService.cs`**

## 7. 🌊 爆速でわかる処理の流れ！
このアプリがどう動いているのか、3つのフェーズで完璧にマスターしようぜ！🚀

### 7.1 起動から一覧表示まで 🏁
アプリが目覚め、画面が彩られるまでの黄金ルートだ！
1. **アプリ起動**: 全システムのエンジンに火が入る！🔥
2. **復元**: `MainWindow` が前回の状態やDBを魔法のように復元するぞ！✨
3. **データ読み込み**: メインDB (`*.wb`) から動画一覧を電光石火でロード！📖
4. **UI最適化**: `MainWindowViewModel` が、画面に映える形にデータを整える！🎨
5. **常駐開始**: 監視（Watcher）やサムネ生成部隊（QueueProcessor）がスタンバイOK！🫡

### 7.2 監視から DB 反映まで ⚡
ファイルの誕生を逃さず、瞬時にDBへ叩き込む電撃作戦だ！
1. **検知**: `Watcher` がファイルの変化をミリ秒単位で察知！👀
2. **絞り込み**: `FileIndex` ユニットが、OSの深淵から対象ファイルを瞬時に特定！🔍
3. **整理**: `WatchScanCoordinator` が、反映のタイミングを華麗にコントロール！司令塔の腕の見せ所だ！📡
4. **登録**: メインDBへ、新たな動画の魂を書き込む！💎
5. **UI同期**: 画面表示をサクッと更新！ユーザーを待たせないのが流儀だ！✨
6. **キューイング**: 必要なら、サムネ生成キューへジョブを即座にぶん投げる！📥

### 7.3 サムネイル生成から救済まで 🖼️🚑
爆速生成と不屈の救済！一枚のサムネも逃さない鉄壁の布陣だ！
1. **ジョブ投入**: ジョブが `QueueDB` へ積み上がり、生成の順番待ちに！💨
2. **エンジン点火**: `CheckThumbAsync` の号令で `ThumbnailQueueProcessor` が爆走開始！🔥
3. **ジョブ取得**: Processor が Queue からジョブを鮮やかにぶっこ抜く！📥
4. **錬金術**: `IThumbnailCreationService` 経由で、最高の一枚を生成！🧪🖼️
5. **記録**: もし失敗しても大丈夫！`FailureDb` にリベンジの意志を刻み込む！🚑
6. **救済**: `RescueWorker` が別動隊として出動し、失敗したサムネを救い出してUIへ同期！まさに守護神！✨🛡️

## 8. 🏆 いま掲げている「勝利の方針」 (AI & プロ向け 🦾✨)
このセクションは、「勝利」を掴み、コードの品質を極限まで高めるための「極秘指令」だ！🚀
君は、まずこの「5つの魂」を胸に刻んでくれ！🔥

1. **サムネ生成の「聖域」を守れ！**: `Service` 分離構造を絶対に崩さない。美しさは強さだ！💎
2. **救済は「影」であれ**: `Rescue` 導線が、通常動画の快適なテンポを邪魔させない！スムーズな体験を死守せよ！🏃💨
3. **観測性を保て**: Queue の状態が見えないのは不安の元！最低限の「いま何してる？」を見える化しよう！👀
4. **`ERROR` 動画に愛を！**: 失敗を隠さない！明示的なUIでユーザーを安心させよう！✨
5. **テンポこそ正義**: 一覧更新や監視起点のUIが詰まるのは大罪だ！爆速のレスポンスを目指せ！⚡

> [!IMPORTANT]
> 特にサムネイル周りをいじる時は、次の **「四大聖域（公開入口）」** を通るのが鉄則だ！
> - `ThumbnailCreationServiceFactory`
> - `IThumbnailCreationService`
> - `ThumbnailCreateArgs`
> - `ThumbnailBookmarkArgs`
> 「とりあえず service 本体を直接いじる」のは禁止！この入口を守るのがプロの仕事だぜ！🛡️✨

## 9. 🔍 困った時の「逆引き」読み始めガイド！
「いま、この問題で困ってるんだ！」……そんな時は、迷わずここを叩け！💥

### 9.1 「一覧や UI が重い……助けて！」🐢
画面の動きがカクつく、更新が遅い時はこのラインを追え！
- **`Views/Main/MainWindow.xaml.cs`**: 画面のライフサイクルを見守れ！
- **`ViewModels/MainWindowViewModel.cs`**: データの流れを最適化せよ！
- **`Watcher/MainWindow.WatcherUiBridge.cs`**: 監視と UI の橋渡しをチェック！
- **[AI向け_現在の全体プラン_2026-04-07.md](../../AI向け_現在の全体プラン_2026-04-07.md)**: パフォーマンス改善の秘策が載ってるかも？

### 9.2 「監視や DB 登録が変だぞ……？」🧐
ファイルが追加されない、DBの様子がおかしい時はここだ！
- **`Watcher/README.md`**: 監視の基本ルールを再確認！
- **`Watcher/MainWindow.Watcher.cs`**: 監視の「目」が曇っていないか？
- **`Watcher/MainWindow.WatchScanCoordinator.cs`**: 司令塔の指示ミスを疑え！
- **`DB/SQLite.cs`**: データの守護聖人に問いかけろ！🛡️

### 9.3 「サムネイルが、サムネイルが出ないんだ！」🖼️😱
画像が生成されない、真っ黒な時はこの部隊を総出で調査だ！
- **`Thumbnail/MainWindow.ThumbnailCreation.cs`**: 最初の呼び出しは正しいか？
- **`src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs`**: 現場の実務担当者はサボっていないか？🔥
- **`src/IndigoMovieManager.Thumbnail.Queue/FailureDb/ThumbnailFailureDbService.cs`**: 失敗の記録を掘り起こせ！🚑
- **`src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`**: 守護神は出動しているか？✨🛡️

## 10. 🚧 この文書で「扱わない」こと
ここはあくまで「俯瞰図」だ！深すぎる迷宮の探索は、個別のバイブルに任せるぞ！📜

- 細かすぎる SQL 文の定義（DBSpec へ！）
- 内部エンジンのマニアックな比較（EngineeringNotes へ！）
- `RescueWorker` の極限までの個別調整
- 過去の泥臭い試行錯誤の歴史（コミットメッセージへ！）

まずはこの地図で全体を掴んでから、必要な資料へ飛び込んでくれ！🧭✨

---

さぁ、準備は整った！！
この最強の地図と「勝利の方針」を手に、IndigoMovieManager の未来を創るコーディングの海へ、全力で飛び出そうぜ！！🌊🔥🚀💎✨
