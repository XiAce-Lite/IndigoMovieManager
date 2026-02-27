# IndigoMovieManager_fork
このリポジトリは `IndigoMovieManager` のフォークです。

「WhiteBrowserの互換プログラムを作ろうと思い立ちまして。いつ使えなくなるかも分からんし。」

「WhiteBrowserでは多くの人がソースコードの公開を願ったが叶わなかった」

この２点から、オープンソースで開発する必要性を感じ、協力しようと考えています。

## フォークのコンセプト
- 常用出来るアプリになるように開発スピード重視で機能を搭載していく

## フォーク独自の変更点
- コード理解のために大幅にリファクタリング
- サムネイル作成処理の高速化
- サムネイル作成処理の絵文字対応
 
## その他オリジナルとの違い
- ソリューション/プロジェクト/実行ファイル名を `_fork` に統一
  - `IndigoMovieManager_fork.sln`
  - `IndigoMovieManager_fork.csproj`
  - `IndigoMovieManager_fork.exe`
- サムネイルの絵文字パス対策ドキュメントを追加
  - `Thumbnail/EmojiPathMitigation.md`
  - `Thumbnail/EmojiPathMitigationDetailDesign.md`
- 開発・運用ドキュメントを拡充（`Docs` 配下）

## ドキュメント
- `ProjectOverview.md` : 全体理解の入口
- `DevelopmentSetup.md` : 開発環境と実行手順
- `Architecture.md` : 構成と責務
- `DatabaseSpec.md` : DB仕様メモ
- `Implementation Plan.md` : 発展計画
- `RegressionChecklist.md` : 回帰チェック手順
- `SearchSpec.md` : 検索仕様（現行実装）
- `EncodingIncidentReport.md` : 文字化けインシデント報告と再発防止策

- WhiteBrowserのSQLiteデータベースファイルは、そのまま使えるように。（重要）
  - WhiteBrowserの既存のスキン4種をベースに。他のスキンは？
    - **→WebView2化しWEBページとして扱う様にするのでユーザーに好きに作ってもらう方向で行きます、旧スキンからの変換は触ってみて判断**
  - WhiteBrowser の サムネイルはそのまま使えるはず。
    - 但し「Thum」から「Thumb」にフォルダは変えて。
    - 配下のフォルダ名は変更要らないはず。
      
  ~~- Extensionも対応予定なし。~~
    - 何となく対応 → 全部じゃないけど。
    - Bookmarkも対応。
    - タグバーに似た機能は枠だけしか実装してない。
    
- 動画をサムネとタグで管理出来て、監視出来て。削除も出来て。辺りをまずは目指す。
  - 任意のサムネイル作成機能 → 実装
    - 小窓でプレビューとかも必要になるわな。→ 実装

## 開発者向けルール (For Developers)
本リポジトリでのコード変更・実行の際は、以下の環境・フォーマットを**必ず**厳守してください。

- **開発環境**: Visual Studio 2026 (VS2026) を推奨
- **文字コード・改行コード**: 全てのファイルで `UTF-8 BOMなし`、改行コードは `LF` に統一すること
- **PowerShell バージョン**: **PowerShell 7.x.x 必須**
  - （※Windows標準の古いPowerShell 5.xは、出力の際に勝手にUTF-16化して文字化けを大量生産する原因となるため絶対に使用しないでください）

## Fork版更新履歴

## 2026/02/24  大規模リファクタリング
- コードを理解するため

## サムネイル並列処理化
- [サムネイル処理ドキュメント](Docs/ThumbnailLogic.md)
- NvidiaのGPU使用）(CUDA)：気持ちCPU負荷が下がる程度

## フォルダスキャン処理をフォルダ単位に
動画件数が多い環境では、DB登録完了までサムネイル作成開始が遅れる課題がありました。  
そのため、新規フォルダ追加時は全体スキャンの完了を待たず、フォルダ単位でサムネイル作成を順次開始します。

## 絵文字対応化開始
- [絵文字問題まとめ](Thumbnail/EmojiPathMitigation_絵文字問題%20症状と対策.md)
- OpeecCV,ffmpegCLIそれぞれで絵文字が使えません
- 基本的には、一時的な名前を付ける事で回避
- フォルダに絵文字が使用されている場合は未対応（対応試行中）

## サムネイルキュー専用DB＆非同期処理アーキテクチャ
- [アーキテクチャ設計ファイル](Thumbnail/plan_AsyncQueueDbArchitecture_サムネイルキュー専用DB_非同期処理アーキテクチャ最終設計.md)
- フォルダ監視・サムネイル作成を非同期化
- サムネイルキュー専用DB追加
- 既知の問題：大規模DBで処理が止まる

## 高速化：Everything連携
- `Everything` は、Windows向けの超高速ファイル検索ツールです。
- 本アプリでは、監視フォルダ内の動画候補収集を高速化するために利用します。
- 動作仕様:
  - `Everything` が利用可能な場合: Everything連携で高速スキャン
  - 利用不可または対象外の場合: 通常のファイルシステム走査へ自動フォールバック
- 設定場所: `共通設定 > Everything連携モード`
- 連携モード:
  - `OFF`: 連携しない
  - `AUTO`（推奨）: 利用可能なときだけ連携
- 高速経路の対象:
  - ローカル固定ドライブ
  - NTFS
  - 上記以外（UNC/NAS、リムーバブル、非NTFS）は通常走査
- 公式サイト: https://www.voidtools.com/
- 解説（窓の杜）: https://forest.watch.impress.co.jp/library/software/everything/

## 高速化：ハッシュ生成
- ハッシュ用ライブラリをCrc32.NETから System.IO.Hashing ベースに変更
