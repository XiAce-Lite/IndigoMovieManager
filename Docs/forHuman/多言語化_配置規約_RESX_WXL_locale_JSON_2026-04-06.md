# 配置規約 RESX / WXL / locale JSON

最終更新日: 2026-04-06

## 1. この文書の目的

IndigoMovieManager の多言語化を進めるにあたり、次の 3 種類のリソース配置規約を固定する。

- WPF / .NET 向け `RESX`
- WiX v6 向け `WXL`
- WebView2 向け `locale JSON`

この文書は、実装着手時に保存場所と責務分界で迷わないための正本とする。

## 2. 先に結論

配置の原則は次のとおり。

1. WPF と .NET の表示文言は `RESX` に置く
2. MSI / Bundle / Burn engine まわりの文言は `WXL` に置く
3. WebView2 で描画する HTML / JS の文言は `locale JSON` に置く
4. 同じ言葉でも、実行境界が違うなら保存形式を混ぜない
5. 第一段階の正本言語は `ja-JP` と `en-US` にする

## 3. 言語と source of truth

### 3.1 対象言語

第一段階で管理する言語は次の 2 つ。

- `ja-JP`
- `en-US`

### 3.2 authoring の基準

当面の authoring 基準は `ja-JP` とする。

理由:

- 現行の UI 文言と文書資産が日本語中心である
- 初回の migration コストを抑えやすい
- 既存コードからの文字列移行を段階的に進めやすい

補足:

- 将来、英語を source language に切り替える余地は残す
- ただし今は切替コストより段階移行のしやすさを優先する

## 4. RESX 配置規約

### 4.1 対象範囲

`RESX` は次の用途に限る。

- WPF 画面文言
- ダイアログ文言
- メニュー、コマンド名、ツールチップ
- app 側 update UI
- managed Custom BA の画面文言

次の用途には使わない。

- WiX authoring string
- WebView2 HTML / JS の文言
- Core / Application 層の永続化 key や protocol 名

### 4.2 置き場所

WPF 本体の RESX は、アプリ直下に `Localization/Resx/` を新設して集約する。

想定構成:

```text
IndigoMovieManager/
├── Localization/
│   └── Resx/
│       ├── Common/
│       │   ├── CommonStrings.resx
│       │   ├── CommonStrings.en-US.resx
│       │   ├── CommandStrings.resx
│       │   └── CommandStrings.en-US.resx
│       ├── MainWindow/
│       │   ├── MainWindowStrings.resx
│       │   └── MainWindowStrings.en-US.resx
│       ├── Settings/
│       │   ├── SettingsStrings.resx
│       │   └── SettingsStrings.en-US.resx
│       ├── Dialogs/
│       │   ├── DialogStrings.resx
│       │   └── DialogStrings.en-US.resx
│       └── Update/
│           ├── UpdateStrings.resx
│           └── UpdateStrings.en-US.resx
```

Custom BA を managed .NET UI で実装する場合は、そのプロジェクト配下にも同じ規約を適用する。

想定構成:

```text
src/IndigoMovieManager.Bootstrapper/
└── Localization/
    └── Resx/
        ├── BootstrapperStrings.resx
        └── BootstrapperStrings.en-US.resx
```

### 4.3 ファイル命名

規約:

- neutral 文字列は `*.resx`
- 英語は `*.en-US.resx`
- 1 ファイル 1 surface または 1 feature に寄せる
- `Resources.resx` のような巨大共通ファイルは作らない

推奨単位:

- `CommonStrings`
- `MainWindowStrings`
- `SettingsStrings`
- `DialogStrings`
- `UpdateStrings`

### 4.4 キー命名

RESX のキーは `Surface_Element_Purpose` 形式を基本とする。

例:

- `MainWindow_SearchBox_Placeholder`
- `Settings_SkinSection_Title`
- `Dialog_DeleteConfirm_Message`
- `Update_RestartRequired_Button`

禁止事項:

- `Title1`
- `LabelText`
- `Message001`

### 4.5 code / XAML からの参照方針

方針:

- code-behind からは型付き resource accessor を使う
- XAML からは markup extension または binding wrapper を通す
- `x:Static` 直参照の乱用で差し替え不能にしない

### 4.6 層境界ルール

Core / Application / Infrastructure では、ユーザー表示用の完成文言を返さない。

返してよいもの:

- message key
- reason code
- payload

表示文言への変換は Host.Wpf または Bootstrapper UI 側で行う。

## 5. WXL 配置規約

### 5.1 対象範囲

`WXL` は次の用途に限る。

- `Product.wxs` の文言
- `Bundle.wxs` の文言
- Burn engine / bootstrapper に渡す localized string

注意:

- managed Custom BA 自体の WPF 画面文言は `RESX` に置く
- WXL は WiX authoring と Burn 周辺の文言だけに使う

### 5.2 置き場所

`installer/wix/Localization/` 配下に surface ごとに分けて置く。

想定構成:

```text
installer/wix/
└── Localization/
    ├── Product/
    │   ├── ja-JP.wxl
    │   └── en-US.wxl
    ├── Bundle/
    │   ├── ja-JP.wxl
    │   └── en-US.wxl
    └── Burn/
        ├── ja-JP.wxl
        └── en-US.wxl
```

補足:

- `Burn/` は WixStandardBootstrapperApplication や engine 寄りの文言を分けたい時に使う
- まずは `Product/` と `Bundle/` の 2 面から始めてよい

### 5.3 ファイル命名

規約:

- ファイル名は culture 名だけにする
- 配置ディレクトリで責務を表す

例:

- `installer/wix/Localization/Product/ja-JP.wxl`
- `installer/wix/Localization/Bundle/en-US.wxl`

### 5.4 string id 命名

`WXL` の string id は `Surface_Purpose` 形式とする。

例:

- `Product_DowngradeError`
- `Product_InstallDirDescription`
- `Bundle_PrereqMissingMessage`
- `Bundle_LaunchButtonText`

禁止事項:

- `Text1`
- `MsgA`
- `String01`

### 5.5 wixproj との接続

`IndigoMovieManager.Product.wixproj` と `IndigoMovieManager.Bundle.wixproj` は、各 surface に対応する `WixLocalization` を明示的に読む。

規約:

- wildcard 乱用で何でも拾わない
- Product build では Product 用の WXL を読む
- Bundle build では Bundle 用の WXL を読む
- culture 指定は `Cultures` プロパティで統一する

### 5.6 Custom BA との境界

Custom BA を導入する場合の原則:

- Burn / bundle engine に属する文字列は `WXL`
- BA の managed UI 画面文言は `RESX`

この 2 つを混ぜない。

## 6. locale JSON 配置規約

### 6.1 対象範囲

`locale JSON` は次の用途に限る。

- WebView2 で描画する HTML の文言
- `wblib-compat.js` や補助 JS の UI 文言
- Browser Companion や補助 panel の文言

次の用途には使わない。

- WPF 側の文言
- WiX 側の文言
- protocol 名、command 名、reason code の定数

### 6.2 置き場所

web 向けローカライズ資産は、WPF や skin runtime のコードと混ぜず、`WebAssets/locales/` に集約する。

想定構成:

```text
IndigoMovieManager/
└── WebAssets/
    └── locales/
        ├── shared/
        │   ├── ja-JP.json
        │   └── en-US.json
        ├── trusted-skin/
        │   ├── ja-JP.json
        │   └── en-US.json
        └── browser-host/
            ├── ja-JP.json
            └── en-US.json
```

規約:

- `shared/` は trusted / untrusted の両方で共通利用する語彙だけを置く
- `trusted-skin/` は WB 互換 runtime 専用
- `browser-host/` は第三者 HTML や companion surface 専用

### 6.3 JSON キー命名

JSON key は `surface.section.item` 形式を基本とする。

例:

- `shared.command.open`
- `trustedSkin.notice.runtimeMissing`
- `trustedSkin.error.thumbnailForbidden`
- `browserHost.dialog.externalUrlApproval.title`

禁止事項:

- `title`
- `message1`
- `okText`

### 6.4 取得ルール

locale JSON は host から locale を渡して読み込む。

方針:

- query string や bootstrap payload で culture を渡す
- web 側は `ja-JP` -> `en-US` の順で fallback する
- JSON 自体に business rule を持ち込まない

### 6.5 trusted / untrusted 分離ルール

trusted skin runtime と untrusted browser host では、同じ日本語文言があってもファイルを安易に共用しない。

共用してよいもの:

- 汎用ボタン文言
- 汎用状態名

分けるべきもの:

- 権限説明
- 承認ダイアログ文言
- エラー理由
- skin runtime 専用の互換説明

## 7. ディレクトリ責務まとめ

| 形式 | 正本ディレクトリ | 主な責務 | 実行境界 |
| --- | --- | --- | --- |
| RESX | `Localization/Resx/` | WPF / managed UI 文言 | .NET / WPF |
| WXL | `installer/wix/Localization/` | MSI / Bundle / Burn 文言 | WiX / Burn |
| locale JSON | `WebAssets/locales/` | HTML / JS 文言 | WebView2 |

## 8. 変換してはいけないもの

次のものはローカライズ資産に入れない。

- SQLite の column 名
- JSON schema key
- web message protocol の method 名
- command id
- log event id
- settings key
- DB に保存する enum 名

これらは表示文言ではなく契約である。

## 9. レビュー時のチェックポイント

レビューでは次を確認する。

1. WPF 文言が locale JSON や WXL に紛れていないか
2. WiX 文言が RESX に入っていないか
3. trusted / untrusted の locale JSON が無秩序に混ざっていないか
4. キー命名が連番や曖昧名になっていないか
5. Core / Application が localized string を返していないか

## 10. 最初の導入順

1. `Localization/Resx/` を作り、`CommonStrings` と `MainWindowStrings` から始める
2. `installer/wix/Localization/Product/` と `Bundle/` を作る
3. `WebAssets/locales/shared/` と `trusted-skin/` を作る
4. その後に `browser-host/` を追加する

## 11. 今回の固定事項

今回この文書で固定することは次のとおり。

1. WPF は `RESX`
2. WiX は `WXL`
3. WebView2 は `locale JSON`
4. authoring 基準は当面 `ja-JP`
5. 第一段階の対象言語は `ja-JP` と `en-US`
6. trusted skin runtime と untrusted browser host の locale は分離する