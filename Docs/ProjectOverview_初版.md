# 🚀 IndigoMovieManager プロジェクト完全理解ガイド！ 🚀

やっほー！このドキュメントは、`IndigoMovieManager` の巨大な海に飛び込むための「最強のダイビングガイド」だよ！🤿✨
ここさえ読めば、次にどこを開発すればいいか一瞬で見えるようになるぜ！

## 1. 🎯 今回の目的
ただ一つ！「このアプリの全体像を**爆速**で理解して、すぐに開発のスタートダッシュを切る」こと！🏃💨

## 2. 🗺️ 最短ルート！まずはここを読め！
1. [README.md](../README.md)（そもそもコレ何？テンションMAXの解説書！）
2. [../IndigoMovieManager_fork.csproj](../IndigoMovieManager_fork.csproj)（技術スタックと依存関係！武器庫ね！）
3. [../MainWindow.xaml](../MainWindow.xaml)（顔となる最強の画面構造！）
4. [../MainWindow.xaml.cs](../MainWindow.xaml.cs)（神ロジックの集合体！心臓部！）
5. [../DB/SQLite.cs](../DB/SQLite.cs)（DBと会話する魔導書！）
6. [../Thumbnail/Tools.cs](../Thumbnail/Tools.cs) と [../Thumbnail/TabInfo.cs](../Thumbnail/TabInfo.cs)（サムネ職人の七つ道具！）
7. [../ModelViews/MainWindowViewModel.cs](../ModelViews/MainWindowViewModel.cs)（画面と裏側を繋ぐ架け橋！）

## 3. 🌍 プロジェクトの全貌
- **正体**: 超堅牢 WPFデスクトップアプリ（`net8.0-windows`）💪
- **使命**: 動画のすべてを制覇する（一覧、タグ付け、検索、スコア、再生、そして最強のサムネ表示！）
- **リスペクト**: 伝説の「WhiteBrowser」のDBとサムネ仕様を限界まで受け継ぐ！
- **記憶装置**: 頼れる相棒・SQLiteちゃん（`movie`, `bookmark`, `history`, `watch`, `system` など）

## 4. 🛠️ これができちゃう！主要機能一覧
- 新しい管理DBの爆誕＆読み込み！
- 気分で変える5つの顔！表示タブ（Small / Big / Grid / List / 5x2）
- タグをいじり、スコアを決め、ファイルを自在に操る神の力（コピー・移動・削除・リネーム）✨
- 素早い検索履歴と華麗なソート機能！
- フォルダ監視システムによる動画の「全自動お出迎え」！📥
- **サムネイル爆速作成**（等間隔 / 狙い撃ち手動）！🔥
- ブックマーク作成で最高の瞬間を保存！

## 5. 📁 直感で分かる！機能ベースのフォルダ紹介
「MVVMみたいな綺麗なアーキテクチャもいいけど、コードを探す時は直感的なフォルダ分けが一番わかりやすいだろ！」ってことで、主要な機能ごとにフォルダが分かれてるぞ！🔥

- **`Thumbnail/` (爆速サムネ職人の工房)**
  - サムネイル生成に関するすべてが詰まってる心臓部！FFMediaToolkitの呼び出しからキューDBの制御、画像の結合まで全部ここだ！🖼️
- **`Watcher/` (不眠不休の監視部隊)**
  - フォルダ変更の検知（FileSystemWatcher）から、Everythingを使った爆速差分抽出まで！新しい動画を捕まえる番人たち！👀
- **`DB/` (魔法の書庫)**
  - SQLiteデータベースの作成、SQLの発行など、データの保存や吸い出しに関わる処理の集まり！📖
- **`Models/` & `ModelViews/` (データと画面の架け橋)**
  - `MovieInfo` で動画から情報を引っこ抜いたり、ViewModelとしてUI側（xaml）にデータを流し込むためのコンシェルジュたち！🤵
- **`UserControls/` (フロントのUI職人たち)**
  - 各タブ（Small / Big / Grid / List）ごとの見た目を彩る再利用可能な画面パーツ群！🎨
- **ルート直下 (`/`)**
  - アプリの総司令官たる `MainWindow.xaml` とその裏側（`.cs`）たちが鎮座している！👑

### 👑 主要ファイルたちの役割（ピックアップ）
- `MainWindow.xaml.cs`: 総司令官。イベントから検索、再生まで全部束ねる！
- `DB/SQLite.cs`: SQLを叩き込む「DB職人」！⚒️
- `Thumbnail/Tools.cs`: ハッシュ計算や画像結合をこなす「錬金術師」！🧪

## 6. 🧐 次、どこ理解する？（優先ミッション）
1. 圧倒的ボリュームの `../MainWindow.xaml.cs` を解読し、機能の境界線を見極めよ！
2. `../DB/SQLite.cs` で、どうやってSQLが作られてるのか探れ！
3. `CreateThumbAsync` と `CheckThumbAsync` の「超絶非同期キュー処理」の動きをマスターせよ！
4. `system` テーブルと `Properties.Settings` の役割分担（どっちが何を担当するか）を完全に理解しろ！

## 7. 📖 仲間のドキュメントたち（最新進化版！🔥）
これらは超絶進化を遂げた現在のアーキテクチャや仕様を表した最新のバイブルだ！必ず目を通せ！✨
- [DevelopmentSetup_2026-02-28.md](DevelopmentSetup_2026-02-28.md)（開発を始めるための最新儀式！）
- [Architecture_2026-02-28.md](Architecture_2026-02-28.md)（どうやって動いてるの？2026年最新の裏側！）
- [DatabaseSpec_2026-02-28.md](DatabaseSpec_2026-02-28.md)（二刀流に進化したデータベースの全貌！）
- [Implementation Plan_2026-02-28.md](Implementation%20Plan_2026-02-28.md)（これからの夢と希望、最新進捗版！）

## 8. 📜 歴史の遺産（フォーク版初期のドキュメント）
これらはフォーク直後、カオスだったコードを紐解くために作られた「歴史の始まり」の遺産だ。黎明期の熱量を感じたい時に読んでくれ！
- [DevelopmentSetup_初版.md](DevelopmentSetup_初版.md)
- [Architecture_初版.md](Architecture_初版.md)
- [DatabaseSpec_初版.md](DatabaseSpec_初版.md)
- [Implementation Plan_初版.md](Implementation%20Plan_初版.md)

さぁ、準備はいいか！？コーディングの海へ飛び込もうぜ！！🌊🔥
