# スキン機能進捗メモ (2026-04-07 / 2026-04-10 / 2026-04-11 更新)

## 現在地

- 目標: WhiteBrowser 由来スキン機能を WebView2 で安定表示し、検索・サムネ契約・旧 WB 互換 callback を実運用できる形まで押し上げる。
- 進捗評価: **Phase 1/2 の中核に加え、callback 互換の第一段と 第1段階の操作互換仕上げまで成立**。
- 現在の意味: `SimpleGridWB` を動かすための最小互換から、`TutorialCallbackGrid` や `WhiteBrowserDefault*` fixture を視野に入れた legacy 互換層へ一段進み、選択 / lifecycle / scroll の土台も実運用寄りになった。

## 今回の実装反映 (2026-04-10 / 2026-04-11)

### 1. callback 互換を一段厚くした

- `onCreateThum` を起点に、旧 WB スキンが前提にしている callback の呼び口を bridge 側へ寄せた。
- `onSetFocus` / `onSetSelect` は、旧 WB スキン側が扱いやすい引数形へ寄せた。
- `onSkinEnter` とスクロール初期化導線を追加し、skin 起動直後の初期化フローを揃えた。
- これにより「`onUpdate` だけ返せる段階」から、「既存 WB skin fixture の描画 callback を順に受け始める段階」へ進んだ。

### 2. 検索以外の操作 API を前進させた

- skin 側からの並び替え要求を、本体の既存検索 / 並び替え導線へ流せる形にした。
- `wb.getProfile` / `wb.writeProfile` / `wb.changeSkin` を MainWindow 側へ接続した。
- `wb.selectThum` は `focusThum` から分離し、WPF 側の現在選択状態と複数選択反映へ接続した。
- `focusedMovieId` を返すようにして、選択解除後に WPF 側で移った実フォーカスへ compat runtime が追従できるようにした。
- これで `find` に続き、一覧 UI が必要とする基本操作を skin 側から段階的に触れるようになった。

### 3. lifecycle / scroll の第1段階を仕上げた

- `onSkinLeave` / `onClearAll` の意味論と発火順を compat runtime 側で固定した。
- `scroll-id` を読んでスクロール対象を解決し、`wb.scrollTo` を inner pane 前提 skin でも扱いやすい形へ寄せた。
- `multi-select` / `scroll-id` 設定を compat runtime が読めるようにし、旧 WB skin の設定依存挙動を一段吸収した。

### 4. legacy alias を追加した

- DTO / callback payload に、旧 WB スキンがそのまま参照しやすい別名を追加した。
- 代表例:
  - `id`
  - `title`
  - `thum`
  - `exist`
  - `select`
- 既に導入済みの新契約 `recordKey` / `thumbRevision` / `thumbUrl` / 寸法情報は維持し、互換と拡張の両立を優先した。

### 5. テストを追加 / 補強した

- callback 互換
- `wb.sort`
- `wb.getProfile` / `wb.writeProfile` / `wb.changeSkin`
- legacy alias
- 複数選択反映
- `focusedMovieId`
- lifecycle の発火順
- `scroll-id` 経路
- compat script の callback 回数確認

を API service テストと compat script 統合テストで押さえる構成へ進めた。

## 実装済み主要事項

### 基盤

- `skin` 資産とスキン実装ソースを分離済み。
  - 実行資産: `skin\` (`Compat`, `DefaultGridWB`, `SimpleGridWB`)
  - 実装ソース: `WhiteBrowserSkin\`
- 外部 skin の catalog / config 読み込みと、`system.skin` / `profile` を使った skin 永続化を成立済み。

### Host / UI 統合

- `MainWindow` 側の WebView2 外部スキン初期化を安定化。
- host を `Hidden` で仮マウントしてから WebView2 初期化する実機安定化策を導入済み。
- refresh scheduler で skin / DB 切替の揺れを畳む構成を導入済み。
- runtime 未導入時の fallback 分岐を導入済み。
- 外部スキン表示中の最小ヘッダー (`Host Chrome Minimal`) を導入済み。

### Bridge / API

- `wb.update`
- `wb.find`
- `wb.sort`
- `wb.getInfo`
- `wb.getInfos`
- `wb.getProfile`
- `wb.writeProfile`
- `wb.changeSkin`
- `wb.focusThum`
- `wb.selectThum` (focus から分離、複数選択反映)
- `wb.scrollTo`
- `wb.getSkinName`
- `wb.getDBName`
- `wb.getThumDir`
- `wb.trace`

までを bridge 対象に含めた。

### 旧 WB 互換

- `onUpdate` だけでなく、`onCreateThum` を軸に callback 互換を拡張した。
- `onSetFocus` / `onSetSelect` / `onSkinEnter` / `onSkinLeave` / `onClearAll` 側の接続を進めた。
- 旧 WB スキンが参照する alias を追加し、既存 fixture を無修正に近い形で通しやすくした。

### サムネ契約

- `dbIdentity`, `recordKey`, `thumbRevision`, `?rev=` を使う契約を固定済み。
- `thumbUrl`, `thumbSourceKind`, 寸法 DTO を含む正本 service を導入済み。
- `thum.local` 経由の managed / external / placeholder サムネ配信を実機で確認済み。
- GDI 枯渇を避けるためのサイズ情報キャッシュまで反映済み。

### 検索 / 並び替え

- 検索 bridge を最小実装し、完了待ちを確実化済み。
- skin 側からの `wb.sort` を本体の並び替え導線へ接続済み。

### テスト

- UI 統合テスト
- runtime bridge 統合テスト
- compat script 統合テスト
- API service テスト
- サムネ契約テスト
- callback 互換 / legacy alias / sort / profile / skin 切替 / 複数選択 / lifecycle / scroll 回帰テスト

まで含めて検証面を強化した。

## 到達点の見立て

- 4/7 時点では「次の山」だった callback 互換強化と `wb.sort` が、今回の作業系列で実装ラインへ上がった。
- これで外部 skin は「表示できる」だけではなく、「既存 WB skin の JavaScript がどこまで無修正で通るか」を現実的に押し上げる段階へ入った。
- 一方で、まだ **旧 WB 完全互換が終わったわけではない**。残件は明確に残っている。

## 未完 (次に着手する候補)

1. tag 系 API の拡張
   - `wb.addTag`
   - `wb.removeTag`
   - `wb.flipTag`
2. callback 互換の残件整理
   - `onUpdateThum`
   - 実ホストの DB 切替 / skin 切替経路を含む lifecycle 検証の強化
3. 大量件数対策
   - 差分更新
   - 仮想スクロール
   - 可視範囲優先ロード
   - DOM 膨張抑制
4. runtime 未導入時の案内と診断導線の強化

## 更新メモ

- 2026-04-07: Phase 1/2 の中核成立を確認。
- 2026-04-09: サムネサイズ情報キャッシュで GDI 枯渇抑制を反映。
- 2026-04-10: callback 互換強化、`wb.sort` 追加、legacy alias 対応、テスト追加に追随。
- 2026-04-11: `selectThum` 分離、複数選択反映、`onSkinLeave` / `onClearAll` 固定、`scroll-id` / `wb.scrollTo` 強化、関連テスト 45 件通過を反映。

## 参考ドキュメント

- `WhiteBrowserSkin/Docs/Implementation Plan_WebView2によるWhiteBrowserスキン完全互換_2026-04-01.md`
- `WhiteBrowserSkin/Docs/ExternalSkinApiUsageSummary_2026-04-07.md`
- `WhiteBrowserSkin/Docs/調査結果_WebView2_WBskin再調査_実装進捗反映_2026-04-02.md`
- `WhiteBrowserSkin/Docs/Implementation Note_WebView2実機起動成功知見_2026-04-02.md`
- `WhiteBrowserSkin/Docs/障害対応_WebView2サムネ契約_GDI枯渇抑制_2026-04-09.md`
