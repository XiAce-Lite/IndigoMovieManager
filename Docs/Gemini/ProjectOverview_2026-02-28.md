# 🚀 IndigoMovieManager プロジェクト完全理解ガイド！ (2026-02-28 最新版) 🚀

やっほー！このドキュメントは、超絶進化を遂げた `IndigoMovieManager` の海に飛び込むための「最新のダイビングガイド」だよ！🤿✨
これさえ読めば、次にどこを開発すればいいか一瞬で見えるようになるぜ！

## 1. 🎯 今回の目的
ただ一つ！「この進化したアプリの全体像を**爆速**で理解して、すぐに開発のスタートダッシュを切る」こと！🏃💨

## 2. 🗺️ 最短ルート！まずはここを読め！（2026年版）
1. [README.md](../README.md)（そもそもコレ何？テンションMAXの解説書！）
2. [../IndigoMovieManager.csproj](../../IndigoMovieManager.csproj)（技術スタックと依存関係！武器庫ね！）
3. [../MainWindow.xaml](../../Views/Main/MainWindow.xaml)（顔となる最強の画面構造！）
4. **[../MainWindow.xaml.cs](../../Views/Main/MainWindow.xaml.cs)（神ロジックの集合体！だけど最近ちょっとずつ分離されて楽になってきた！）**
5. [../DB/SQLite.cs](../../DB/SQLite.cs)（メインDBと会話する魔導書！）
6. **[../Thumbnail/ThumbnailQueueProcessor.cs](../../src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs)（新たに生まれた非同期サムネ処理の守護神！）**
7. [../ModelViews/MainWindowViewModel.cs](../../ViewModels/MainWindowViewModel.cs)（画面と裏側を繋ぐ架け橋！）

## 3. 🌍 プロジェクトの全貌
- **正体**: 超堅牢 WPFデスクトップアプリ（`net8.0-windows`）💪
- **使命**: 動画のすべてを制覇する（非同期対応で最強になったサムネ、爆速検索、ファイル操作！）
- **リスペクト**: 伝説の「WhiteBrowser」を受け継ぐが、設計は令和バージョンへ超絶進化！
- **記憶装置**: 二刀流の SQLiteちゃん！⚔️
  - **メインDB (`*.bw`)**: `movie`, `bookmark`, `history`, `system` 等の歴史ある永続データ！
  - **キューDB (`*.queue.imm`)**: サムネ生成をバックグラウンドで回すための**最新の非同期キュー**！

## 4. 🛠️ これができちゃう！主要機能一覧
- 気分で変える5つの顔！表示タブ（Small / Big / Grid / List / 5x2）
- タグをいじり、スコアを決め、ファイルを自在に操る神の力（コピー・移動・削除・リネーム）✨
- フォルダ監視システムによる動画の「全自動お出迎え」！（Everything連携でDBへのI/Oが激減！）📥
- **完全非同期・画面フリーズゼロのサムネイル爆速作成**（専用キューDBとFFmpeg.AutoGenの奇跡のコラボ）！🔥
- ブックマーク作成で最高の瞬間を保存！

## 5. 📁 直感で分かる！機能ベースのフォルダ紹介（Service分離後）
「MVVMみたいな綺麗なアーキテクチャもいいけど、コードを探す時は直感的なフォルダ分けが一番わかりやすいだろ！」ってことで、主要な機能ごとにフォルダが分かれてるぞ！🔥

- **`Thumbnail/` (爆速サムネ職人の工房とキューの住処)**
  - サムネイル生成に関するすべてが詰まってる！キューDB(`ThumbnailQueueProcessor`)の制御や、最凶の黒魔術(`FFmpeg.AutoGen`)を使った画像合成まで全部ここだ！🖼️
- **`Watcher/` (不眠不休の監視部隊と特務機関)**
  - `FileSystemWatcher` での検知に加え、**`EverythingFolderSyncService`** が Everything API と通信して爆速の差分抽出を行っている最前線！👀
- **`DB/` (魔法の書庫)**
  - SQLiteデータベースの作成、SQLの発行など、データの保存に関わる処理！ここがメインDBの入り口だ！📖
- **`Models/` & `ModelViews/` (データと画面の架け橋)**
  - `MovieInfo` でメタ情報を引っこ抜いたり、ViewModelとしてUI側（xaml）にデータを流し込むためのコンシェルジュたち！🤵
- **`UserControls/` (フロントのUI職人たち)**
  - 各タブごとの見た目を彩る再利用可能な画面パーツ群！🎨
- **ルート直下 (`/`)**
  - アプリの総司令官たる `MainWindow.xaml` とその裏側たちが鎮座している！👑

### 👑 2026年版・主要ファイルたちの役割（ピックアップ）
- `MainWindow.xaml.cs`: 総司令官。でも最近は「監視」や「サムネ」をServiceクラスに丸投げして、少し身軽になった！
- `DB/SQLite.cs`: 古き良きメインDBを支える「DB職人」！⚒️
- `Thumbnail/ThumbnailCreationService.cs`: 動画から極限速度でフレームをぶっこ抜く「新世代の錬金術師」！🧪

## 6. 🧐 次、どこ理解する？（優先ミッション）
1. 圧倒的ボリュームの `../MainWindow.xaml.cs` を解読し、**新しく切り出された「Service呼び出し」**の境界線を見極めよ！
2. `CheckThumbAsync` の「超絶非同期（キューDB）処理」と「リース排他制御」の動きをマスターせよ！
3. `../DB/SQLite.cs` で、どうやってSQLが作られてるのか探れ！（バインド化作戦進行中！）

## 7. 📖 仲間のドキュメントたち（最新進化版！🔥）
これらは超絶進化を遂げた現在のアーキテクチャや仕様を表した最新のバイブルだ！必ず目を通せ！✨
- [../AI向け_現在の全体プラン_workthree_2026-03-20.md](../../AI向け_現在の全体プラン_workthree_2026-03-20.md)（AI向けの現在の全体計画。大粒度優先順位はこれを正本とする！）
- [DevelopmentSetup_2026-02-28.md](DevelopmentSetup_2026-02-28.md)（開発を始めるための最新儀式！）
- [Architecture_2026-02-28.md](Architecture_2026-02-28.md)（どうやって動いてるの？2026年最新の裏側！）
- [DatabaseSpec_2026-02-28.md](DatabaseSpec_2026-02-28.md)（二刀流に進化したデータベースの全貌！）
- [Implementation Plan_2026-02-28.md](../Implementation%20Plan_2026-02-28.md)（これからの夢と希望、最新進捗版！）

## 8. 📜 歴史の遺産（フォーク版初期のドキュメント）
これらはフォーク直後、カオスだったコードを紐解くために作られた「歴史の始まり」の遺産だ。黎明期の熱量を感じたい時に読んでくれ！
- [DevelopmentSetup_初版.md](../DevelopmentSetup_初版.md)
- [Architecture_初版.md](../Architecture_初版.md)
- [DatabaseSpec_初版.md](../DatabaseSpec_初版.md)
- [Implementation Plan_初版.md](../Implementation%20Plan_初版.md)

さぁ、準備はいいか！？コーディングの海へ飛び込もうぜ！！🌊🔥
