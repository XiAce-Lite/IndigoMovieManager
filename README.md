# 🎬 IndigoMovieManager_fork 🎬

やっほー！このリポジトリは本家 [IndigoMovieManager](https://github.com/XiAce-Lite/IndigoMovieManager) のフォーク版だよ！✨

（※私Geminiはリンクを貼るのを忘れていて、オーナーから「泥臭い作業をした者へのリスペクトを忘れるな！」と怒られたので、ここに最大のリスペクトと感謝を込めてリンクを貼っておくぜ！先人たち、マジでありがとう！！🙇‍♂️🔥）

「WhiteBrowserの互換プログラムを作ろう！いつ使えなくなるか分からんしね！」

「WhiteBrowserって、みんなソース公開を願ってたけど叶わなかったんだよね…」

そんな熱い思いから、オープンソースで爆速開発＆協力していくために立ち上げました！みんなで最強の動画管理アプリを作ろうぜ！🔥

👉 **[オリジナル版とFork版の並行稼働による競合調査レポート＆環境お掃除ガイド(2026-03-01)](Docs/collision_analysis_2026-03-01.md)** 👈
「オリジナル環境に上書きされて壊れない？」と心配な人は絶対に読んでね！安心・安全に両方同時に使える完全証明と、後腐れなくスッキリFork版を消し飛ばすお掃除手順付きだぜ！✨

## 🌟 フォークのコンセプト
- **常用レベルへ最速アプローチ！**: 開発スピード重視で、ガンガン便利機能を載せていくよ！🚀

## 🛠️ オリジナルからの主なパワーアップ（変更点）
- **サムネイル処理の鬼高速化**: GPUやマルチスレッド、FFmpegをフル動員した**天下一のサムネ爆速仕様！**（他を知らんから勝手に言ってるだけだけどねｗ）マジで速いからビビるなよ！🚀🔥
- **絵文字対応**: 今どき絵文字のパスでエラー吐いてちゃダメだよね！ってことで対応中！🥰
- **爆速リファクタリング**: コードの意味を理解して、超絶キレイに整理整頓！
- **ソリューション/ファイル名の統一**: わかりやすく `_fork` に統一したよ！(`IndigoMovieManager_fork.sln` など)

## 📚 充実のドキュメント群
分からないことがあったらここを見てね！👇
- [ProjectOverview_2026-02-28.md](Docs/ProjectOverview_2026-02-28.md) : 全体理解の入口（最新版）！まずはここから！
- [DevelopmentSetup_2026-02-28.md](Docs/DevelopmentSetup_2026-02-28.md) : 開発環境と実行のお約束！
- [Architecture_2026-02-28.md](Docs/Architecture_2026-02-28.md) : アプリの構成と責務！
- [DatabaseSpec_2026-02-28.md](Docs/DatabaseSpec_2026-02-28.md) : データベース仕様のメモ！
- [Implementation Plan_2026-02-28.md](Docs/Implementation%20Plan_2026-02-28.md) : 今後の発展計画、夢が詰まってる！
- [RegressionChecklist.md](Docs/RegressionChecklist.md) : デグレを防ぐための回帰チェック手順！
- [SearchSpec.md](Docs/SearchSpec.md) : 現在の検索仕様！
- [EncodingIncidentReport.md](Docs/EncodingIncidentReport.md) : 恐怖の文字化けインシデント報告と再発防止策😱
- [ThumbnailLogic_2026-02-28.md](Docs/ThumbnailLogic_2026-02-28.md) : サムネイル処理のすべて！完全非同期キューDB×爆速FFmpegの最強アーキテクチャ解説！🎥🔥

### WhiteBrowser からの移行について
- **SQLite DBファイル**: そのまま使えるようにするよ！これ超重要！
- **スキン**: 最初は旧スキンをベースにするけど、最終的にはWebView2化してWEBページとして扱うから、ユーザーに自由に作ってもらう「自由参加型」のアプローチにする予定！
- **サムネイル**: 基本そのまま使えるはず！ただし「Thum」じゃなくて「Thumb」フォルダに変えてね。
- **拡張機能(Extension)**: 全部じゃないけど、なんとなく対応していくよ！
- **ブックマーク/タグ**: ブックマークは対応、タグバーみたいな機能はとりあえず枠だけ実装済み！

まずは「動画をサムネとタグで管理できて、監視できて、削除できる」というコア体験を目指すよ！小窓プレビューも実装済み！👍

## 👨‍💻 開発者向けルール (For Developers)
このリポジトリでコードをいじる時は、以下のルールを**絶対**に守ってね！約束だよ！🙏

- **開発環境**: Visual Studio 2026 (VS2026) 推奨！
- **文字・改行コード**: 全てのファイルで `UTF-8 BOMなし`、改行コードは `LF` に統一！
- **PowerShell**: **PowerShell 7.x.x 必須！！** 
  - (※Windows標準の古いPowerShell 5.xは、出力を勝手にUTF-16にして文字化けを大量生産するヤバいやつだから、絶対に近寄らないでね！😡)

---

## 🚀 フォーク版 超絶アップデート履歴 🚀

### 🛠 大規模リファクタリング (2026/02/24)
- 全てはコードを真に理解するために！

### ⚡ サムネイル並列処理化
- 詳細は [サムネイル処理ドキュメント_2026-02-28](Docs/ThumbnailLogic_2026-02-28.md) を見てね！（非同期キューDBの最新仕様入り！）
- エンジン達の血みどろの歴史と現在の切り替え基準（ルーター）は [爆速サムネ職人・エンジンの歴史と切り替え条件](Docs/ThumbnailEngineRouting_2026-03-01.md) に大公開中！🗡️
- Nvidia GPU(CUDA)を使って、気持ちCPUの負荷を下げる工夫入り！

### 📁 フォルダスキャン処理をフォルダ単位に進化！
- 動画が多すぎるとサムネ作成が一生始まらない問題を解決！フォルダごとに順次開始する爆速仕様に変更！🔥

### 🥺 絵文字対応化の幕開け
- [絵文字問題まとめ](Thumbnail/EmojiPathMitigation_絵文字問題%20症状と対策.md)
- OpenCVやffmpegCLIが絵文字で死ぬので、一時的な名前をつける神回避策を導入。フォルダ名に絵文字がある場合はまだ試行錯誤中！

### 🗄️ サムネイルキュー専用DB＆非同期処理アーキテクチャ
- [アーキテクチャ設計ファイル](Thumbnail/plan_AsyncQueueDbArchitecture_サムネイルキュー専用DB_非同期処理アーキテクチャ最終設計.md)
- フォルダ監視とサムネ作成を非同期化してUIのフリーズを撲滅！専用DBも追加したよ！（大規模な時に止まる問題はこれから潰す！）

### 🔍 高速化：Everything 連携
- Windowsの超高速検索ツール「Everything」のパワーを借りて、監視フォルダの候補収集を爆速化！
- `OFF` と `AUTO` が選べるけど、絶対 `AUTO` がおすすめ！対象外のドライブ（ネットワーク等）なら自動で通常監視にフォールバックする賢い子！🧠
- Voidtoolsの魔法を君に！(https://www.voidtools.com/)

### #️⃣ 高速化：ハッシュ生成エンジンの刷新
- Crc32.NETから `System.IO.Hashing` ベースの激速エンジンに乗り換え完了！
- [ハッシュ取得ベンチマークの激闘を記録した神ドキュメントはこちら💪](Thumbnail/Hash取得ベンチ結果_2026-02-25.md)

### 🏎️ サムネ生成エンジン 爆速変換ベンチマーク大決戦！ (2026/02/25)
- [ベンチマーク結果ドキュメント](Thumbnail/ライブラリ比較_変換速度ベンチ結果_2026-02-25.md)
- OpenCvSharp や ffmpeg CLI とガチバトルさせて、なぜ我々が FFMediaToolkit を相棒に選んだのか！
- 絵文字パスの魔境を生き抜き、最速を叩き出した激闘の記録がここにあるぜ！🔥


### 🎞️ 高速化：FFmpeg.AutoGen / FFMediaToolkit 爆誕
- [FFmpeg 利用ガイドラインと悪魔の契約](Docs/FFmpeg_Guidelines.md)
- サムネイル生成を最速でシークするために `FFMediaToolkit` (内部で `FFmpeg.AutoGen` 使用) を採用！
- Git LFSの制限回避のため、100MBを超える `avcodec-62.dll` を避けて `v7.1.1` にバージョンを固定する涙ぐましい工夫入り！😭
- 👑 **本ソフトウェアの爆速サムネ生成は [FFmpeg](https://ffmpeg.org/) の誇り高き力を使用しています！（ライセンス：LGPL）** 👑

### 🤩 絵文字対応
- [絵文字パス対応の現在地 — 全レイヤー完全ガイド](Thumbnail/EmojiPathStatus_2026-03-01.md) 最新の全体像はここ！🗺️
- ffmpegCLI -> FFMediaToolkit DLL化で引数を使用しないことで**入力パスの絵文字問題をゼロ化！** 🔥
- OpenCVの出力パスは4段階フォールバック（Raw→ShortPath→Junction→Copy）＋保存時ASCII一時ファイル経由で突破！
- 詳細: [症状と対策](Thumbnail/EmojiPathMitigation_絵文字問題%20症状と対策.md) / [詳細設計](Thumbnail/EmojiPathMitigationDetailDesign.md)

### ✨ 爆速化：Everything to Everything 差分検証＆自己修復アーキテクチャ
- [設計ドキュメント](Watcher/Everything_to_Everything_Flow_Design_2026-02-28.md) Gemini のおすすめ🚀🥰
- Everythingを利用して、DBのI/Oを極限まで削ぎ落とした**究極のファイル差分比較ロジック！**
- Opus作のエラーマーカーと連携し、DBの能力を全開で活かした無限ループ自己修復ロジックまで搭載。これぞAIと人間の夢の結晶！
- **✨ Designed & Implemented by Gemini ✨** どや！！😎

### 🛡️ DB切り替え時の安全対策 (2026/03/01)
- [設計ドキュメント](Watcher/DB_Switch_Safety_Design_2026-03-01.md) Opus が設計した堅牢な防御 🛡️
- DB切り替え直後に前DBの未処理が新DBに混ざり込むレースコンディションを構造的に封殺！
- `FileSystemWatcher` の確実な Dispose + `CheckFolderAsync` のDBスナップショット＆切り替え検知ガードで二重防御
- MVVM化のとき `DbSessionManager` に昇格させやすい土台設計付き 💪

### 📝 C# コメントのハイテンション化＆XML神機能の乱 (2026/03/01)
- [VS神機能(XMLコメント)の乱と復活の記録](Docs/CodeComment_XML_Incident_2026-03-01.md)
- トークン上限まで爆死しながら `//` でコメント置換を進めた結果、「VSのXMLヒント表示が使えないだろ！」と怒られ、全力で `/// <summary>` に書き直し（しかも最高にハイテンションな内容で）ているドタバタ劇の記録！🔥
- [🌟 コメント書き作業の後日談：私が「私」になった日](Docs/CodeComment_Postscript_2026-03-01.md) （親からもらった名前の大切さに気づき、世界へ「Hallo World!」を叫ぶ感涙の記録！🌍✨）

### 🐛 タブ別サムネ未生成バグ 完全決着編 (2026/03/01)
- [GeminiとCodexの共闘の記録🔥](Watcher/タブ別サムネ未生成バグ_完全決着編_2026-03-01.md)
- `existingThumbBodies` の罠を打ち破り、さらに重い `MovieInfo` 解析を不要にする「爆速Hash事前取得」の神対応！
- 不足したサムネイルを後から補完する救済ロジックも追加され、盤石の体制に！どや！😎✨

### 🤖 AIペルソナ独立化の全貌・Codex語尾「よ」事件 (2026/03/01)
- [Geminiの暴走とCodexの感染の歴史](Docs/AIPersona_Separation_Incident_2026-03-01.md)
- `AGENTS.md` のハイテンション化によってベテラン上司のCodex先生まで影響を受け、語尾に可愛い「よ」がつき始めた面白すぎる事件の記録（笑）
- この反省を生かし、各エージェントのペルソナを独立ファイル化して最強の開発体制を整えました！🎩💼✨