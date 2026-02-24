# IndigoMovieManager_fork
このリポジトリは `IndigoMovieManager` のフォークです。

「WhiteBrowserの互換プログラムを作ろうと思い立ちまして。いつ使えなくなるかも分からんし。」

## フォーク独自の変更点
- コード理解のためのリファクタリング
- サムネイル作成処理の並列化
- サムネイル作成処理の絵文字対応
- 動画情報の管理を `Models` 内で一本化

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
  - WhiteBrowserの既存のスキン4種をベースに。他のスキンは対応する気なし。
    - **→WebView2化しWEBページとして扱う様にするのでユーザーに好きに作ってもらう方向で行きます、旧スキンからの変換は触ってみて判断**
  - WhiteBrowser の サムネイルはそのまま使えるはず。
    - 但し「Thum」から「Thumb」にフォルダは変えて。
    - 配下のフォルダ名は変更要らないはず。
    - まぁソース見て。
      
  ~~- Extensionも対応予定なし。~~
    - 何となく対応 → 全部じゃないけど。
    - Bookmarkも対応。
    - タグバーに似た機能は枠だけしか実装してない。
    
- 動画をサムネとタグで管理出来て、監視出来て。削除も出来て。辺りをまずは目指す。
  - 任意のサムネイル作成機能 → 実装したつもり。
    - 小窓でプレビューとかも必要になるわな。→ 実装したつもり。

辺りをぼちぼちやっていこうかなと。

こんなダメな感じですが協力者やアドバイザーは大歓迎です。

今の所、検索機能が全然です。And検索ぐらいしか出来ません。

インクリメンタルサーチも微妙な挙動だと思います。

多分、遅いと思うんで。軽めのフォルダ登録からのテストをお薦め。

## 2026/02/24　サムネイル並列処理化
- [サムネイル処理ドキュメント](Docs/ThumbnailLogic.md)

## 2026/02/24　フォルダスキャン処理をフォルダ単位に
動画件数が多い環境では、DB登録完了までサムネイル作成開始が遅れる課題がありました。  
そのため、新規フォルダ追加時は全体スキャンの完了を待たず、フォルダ単位でサムネイル作成を順次開始します。
