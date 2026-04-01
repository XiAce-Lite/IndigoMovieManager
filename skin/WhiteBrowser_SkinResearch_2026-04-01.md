# WhiteBrowser スキン仕様調査 2026-04-01

## 1. 調査対象
- https://w.atwiki.jp/whitebrowser/pages/17.html
- https://w.atwiki.jp/whitebrowser/pages/21.html
- https://w.atwiki.jp/whitebrowser/pages/22.html
- https://w.atwiki.jp/whitebrowser/pages/23.html

## 2. WhiteBrowser 側の事実

### 2.1 スキンテンプレート
- `スキンテンプレート` では、スキン HTML の最低構成として `テンプレート1` と `テンプレート2` が案内されている。
- `テンプレート2` では、次が最低限必要だった。
  - `<script src="../prototype.js">`
  - `<script src="../wblib.js">`
  - `wb.update(0, 10)` などの更新呼び出し
  - `wb.onUpdate = function(mvs) { ... }`
  - `<div id="view"></div>`
  - `<div id="config"> ... </div>`

### 2.2 config の仕様
- `config` は HTML 内の `<div id="config">...</div>` に書く。
- 書式は CSS 風の `key : value;`。
- 必須は `skin-version`。
- オプションとして、今回関係が深いのは次。
  - `thum-width`
  - `thum-height`
  - `thum-column`
  - `thum-row`
  - `seamless-scroll`
  - `scroll-id`
  - `multi-select`

### 2.3 メソッド群
- スキン JavaScript から `wb.*` メソッドを呼べる。
- 今回の移植で重要だったのは次。
  - `update`
  - `focusThum`
  - `getInfo`
  - `getInfos`
  - `getSkinName`
  - `getThumDir`
  - `writeProfile`
  - `getProfile`
  - `changeSkin`
- コールバックとして次が定義されている。
  - `onUpdate`
  - `onCreateThum`
  - `onClearAll`
  - `onSetFocus`
  - `onSetSelect`
  - `onUpdateThum`
  - `onSkinEnter`
  - `onSkinLeave`

## 3. IndigoMovieManager へ持ち込む範囲

### 3.1 今回実装した範囲
- 新しい `skin` フォルダを正式な読み込み対象にした。
- `skin\<SkinName>\<SkinName>.htm` または `.html` を走査対象にした。
- `div#config` を読み、既存の上側タブへ安全にマップする基盤を追加した。
- `system.skin` に外部スキン名を保存できるようにした。
- 外部スキン利用時だけ、`profile` テーブルへ `LastUpperTab` を保存し、再表示タブを補助保存するようにした。
- 設定画面から現在 DB のスキンを切り替えられるようにした。

### 3.2 今回あえて未実装にした範囲
- HTML/CSS/JavaScript をそのまま描画する実行系
- `wb.*` API の実行環境
- `onUpdate` などの JS コールバック呼び出し
- シームレススクロールの HTML DOM 実装

## 4. 現在の互換方針
- WhiteBrowser の完全互換ではなく、まずは「配置規約」と「config 読み取り」を先に移植する。
- 読み取った `thum-width` / `thum-height` / `thum-column` / `thum-row` は、現行の既存タブへ次のように寄せる。
  - `120x90x3x1` に近いもの: `DefaultSmall`
  - `200x150x3x1` に近いもの: `DefaultBig`
  - `160x120x1x1` に近いもの: `DefaultGrid`
  - `56x42x5x1` に近いもの: `DefaultList`
  - `120x90x5x2` に近いもの: `DefaultBig10`
- これで既存の仮想化 UI とサムネパス規約を壊さず、将来の本格移植へつながる足場を先に作る。

## 5. 実装判断
- このブランチの最優先はユーザー体感テンポなので、WebView や独自 DOM 実行系は今回入れていない。
- まずは既存 WPF 一覧の高速性を維持したまま、WhiteBrowser スキン資産を「認識して保存できる」段階まで持っていく方が安全と判断した。
- 次段で本格互換をやるなら、`skin` 走査と `config` パース基盤はそのまま再利用できる。

## 6. 追加したサンプル
- `skin\DefaultGridWB\DefaultGridWB.htm`
- `skin\DefaultGridWB\DefaultGridWB.css`

このサンプルは WhiteBrowser の `テンプレート2` を元にした最小構成で、現状の移植基盤が config を読めることを確認するための土台として置いている。
