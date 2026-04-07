# IndigoMovieManager 総合アーキ方針

最終更新日: 2026-04-06

## 1. この文書の目的

IndigoMovieManager について、次の 3 要素を同時に満たすための大粒度アーキテクチャ方針を固定する。

- WPF アプリの多言語化
- WiX v6 インストーラーと自己更新
- WebView2 でユーザーのカスタム HTML を読み込み、外部 web 接続の可能性も扱うこと

この文書は、全面リライトではなく、現行 IndigoMovieManager を活かした段階移行の正本方針として使う。

## 2. 結論

最適な構成は、`WPF shell + Application/Infrastructure 分離 + WebView2 二面構成 + WiX v6 Bundle/MSI/Custom BA 分離` である。

要点は次の 4 つ。

1. WPF は shell とネイティブ導線の正本に留め、業務ロジックは段階的に Application 層へ逃がす
2. WebView2 は 1 つにまとめず、`Trusted Skin Runtime` と `Untrusted Browser Host` に分離する
3. 多言語化は WPF、WiX、WebView2 で同一方式に無理に統一せず、それぞれに適した仕組みを使う
4. 配布と更新は `WiX v6` 側と app 側で責務分離し、install と update と uninstall の境界を先に固定する

## 3. 採用する全体像

### 3.1 レイヤー構成

推奨レイヤーは次のとおり。

- `Core`
  - ドメインモデル、契約、列挙、永続化に依存しないルール
- `Application`
  - Catalog、Search、Watcher、Thumbnail、Skin、Update、Settings のユースケース
- `Infrastructure`
  - SQLite、ファイルシステム、Everything、WebView2 橋渡し、更新取得、ログ、外部ツール
- `Host.Wpf`
  - MainWindow、Dialog、Command binding、リソース切替、ネイティブ導線
- `Installer`
  - WiX v6 Bundle、MSI、Custom BA

補足:

- これは maimai_MovieAssetManager 系の層構成を到達形として参照する
- ただし今すぐ全面移行せず、現行 IndigoMovieManager に Composition Root と service 契約を足して寄せる

### 3.2 Bounded Context

最初に責務境界を固定する対象は次のとおり。

- Catalog / Search
- Watcher / Ingest
- Thumbnail / Rescue
- WhiteBrowser Skin Runtime
- Browser Companion / External Web
- Settings / Localization
- Update / Installer
- Diagnostics / Logging

## 4. UI ホスト方針

### 4.1 WPF の役割

WPF は次の責務を持つ。

- メインウィンドウとダイアログ
- キーボードショートカット、メニュー、右クリックなどのネイティブ導線
- built-in タブの高速表示
- 状態の正本管理
- ローカライズ済み UI 文言の反映

WPF 側に業務ロジックや WebView2 固有ロジックを抱え込み続けない。

### 4.2 built-in タブの扱い

既存の `DefaultSmall / DefaultBig / DefaultGrid / DefaultList / DefaultBig10` は WPF のまま維持する。

理由:

- 既存のユーザー体感テンポを守りやすい
- WhiteBrowser 完全互換とは別の最適化軸を保てる
- 外部スキン対応のために標準タブまで巻き込まなくて済む

## 5. WebView2 方針

### 5.1 二面構成を採用する理由

第三者配布 HTML と外部 web 接続を許容するなら、WebView2 を 1 面で済ませるのは危険である。

そのため、次の 2 面構成を採用する。

- `Trusted Skin Runtime`
  - WhiteBrowser 互換スキン専用
  - `wb.*` 互換 API を持つ
  - 外部スキン互換を重視
  - 既定ではネットワーク接続を禁止または極小化する
- `Untrusted Browser Host`
  - 第三者 HTML、一般 web、補助 UI、Browser Companion 用
  - 特権 API を持たない
  - 外部接続を許容する場合でも承認付き導線に限定する

### 5.2 Trusted Skin Runtime の原則

Trusted Skin Runtime では次を守る。

- 既存の `WhiteBrowserSkinOrchestrator` を skin feature の入口として使う
- `WhiteBrowserSkinRuntimeBridge` と `WhiteBrowserSkinApiService` を allowlist 方式で拡張する
- `skin.local` や `thum.local` のような仮想ホストまたは custom scheme でローカル資産を配る
- `file:///` 直接参照は許さない
- host object の直接露出はしない
- 選択状態の正本は常に WPF 側 ViewModel に置く
- UserDataFolder は専用領域に隔離する
- 任意外部サイトへの unrestricted navigation は許さない

### 5.3 Untrusted Browser Host の原則

Untrusted Browser Host では次を守る。

- privileged bridge を持たせない
- ローカルファイル読書き、外部コマンド実行、更新適用は直接させない
- coarse-grained な command のみ host 側へ要求できるようにする
- 重要操作は承認 UI を挟む
- 可能なら別プロセス executable に分離する

推奨 command 例:

- `open_item`
- `open_folder`
- `import_request`
- `open_external_url_pending_approval`

## 6. 多言語化方針

### 6.1 第一段階の対象言語

第一段階の正本言語は次の 2 つとする。

- `ja-JP`
- `en-US`

### 6.2 WPF の多言語化

WPF 側は `RESX + satellite assembly` を基本とする。

方針:

- 画面文言は型付き resource access を通す
- 直接文字列の散在を減らす
- Core / Application にはローカライズ済み文言を持ち込まない
- ログや診断は key + payload ベースにして、表示時に言語化する

### 6.3 WebView2 側の多言語化

WebView2 側は WPF と resource 形式を共有しない。

方針:

- host から locale bootstrap payload を渡す
- Web 側は `locale JSON` を読む
- trusted skin と untrusted browser で同一辞書を使う場合も、配布形式は web 向けに独立させる

### 6.4 WiX 側の多言語化

WiX v6 では `WXL` を使う。

方針:

- `Product.wxs`
- `Bundle.wxs`
- `Custom BA`

これらの user-facing 文字列を `!(loc.Xxx)` へ外出しする。

配置規約の詳細は `Docs/forHuman/配置規約_RESX_WXL_locale_JSON_2026-04-06.md` を参照する。

## 7. インストーラーと更新方針

### 7.1 採用構成

採用構成は次のとおり。

- `v1`: `WiX v6 Bundle + MSI`
- `v2`: app 側自己更新サービス + `UpdateApplyBridge`
- `v3`: `Custom BA` による保持項目付き uninstall

### 7.2 責務分離

分離方針は次のとおり。

- WiX 側
  - install / upgrade / uninstall の実行管理
  - prerequisite 判定
  - Bundle UI
- app 側
  - GitHub Releases API による更新確認
  - manifest 解決
  - download
  - 終了後適用の橋渡し
- Custom BA 側
  - explicit uninstall 時の保持項目 UI
  - runtime-generated data の cleanup 判断

### 7.3 データ配置の原則

先に区別するべき保存場所は次のとおり。

- install-managed files
- mutable local data
- WebView2 cache
- update staging
- logs
- user settings

AppIdentity ベースの local data 規約は維持する。

## 8. 段階移行プラン

### Phase 1: 境界固定

- Bounded Context を固定する
- Trusted / Untrusted の二面構成を固定する
- 多言語化と installer の責務境界を固定する

### Phase 2: Composition Root 導入

- 現行 MainWindow 周辺から service 契約を切り出す
- coordinator と application service を追加する
- 既存 partial class の責務を薄くする

### Phase 3: 多言語化骨格

- WPF の RESX 化を始める
- WiX の WXL 化を始める
- Web 側 locale bootstrap を入れる

### Phase 4: Trusted Skin Runtime 強化

- allowlist API を固定する
- path traversal、message validation、timeout を補強する
- built-in WPF タブの体感を悪化させない

### Phase 5: Untrusted Browser Host 導入

- 外部 web と第三者 HTML を privileged runtime から切り離す
- approval flow を実装する

### Phase 6: WiX v1 -> v2 -> v3

- v1 の install / upgrade / uninstall を固める
- v2 の自己更新を追加する
- v3 の保持項目 UI を Custom BA で追加する

## 9. 既存コードで活かすもの

- `WhiteBrowserSkinOrchestrator`
- `WhiteBrowserSkinRuntimeBridge`
- `WhiteBrowserSkinApiService`
- `WhiteBrowserSkinEncodingNormalizer`
- `AppIdentityRuntime`
- WiX v6 の既存 skeleton
- maimai_MovieAssetManager の `Core / Application / Infrastructure / Host.Wpf` 分離パターン

## 10. 今回の判断

今回の総合判断は次のとおり。

1. 全面再編を先にやらず、IndigoMovieManager への段階移行を優先する
2. 第三者 HTML を許容するため、trusted skin runtime と untrusted browser host は分離する
3. 第一段階の UI 言語は `ja-JP` と `en-US` にする
4. WiX は `v3` まで見据えた責務分離を前提にする
5. built-in 高速タブは WPF 維持、外部スキン互換だけを WebView2 に載せる

## 11. 受け入れ条件

- built-in WPF タブの体感テンポが悪化しない
- 外部 WhiteBrowser 互換スキンが trusted runtime で動く
- 第三者 HTML が privileged API へ直接到達できない
- `ja-JP / en-US` の切替が WPF / WiX / Web で破綻しない
- install / major upgrade / explicit uninstall / self-update の責務が混線しない
- WebView2 Runtime 不在や offline 時に安全に縮退できる

## 12. 次に着手する順番

1. Composition Root の導入方針を 1 枚に切る
2. RESX / WXL / locale JSON の配置規約を決める
3. Trusted Skin Runtime と Untrusted Browser Host の API 境界を列挙する
4. WiX v1 / v2 / v3 の責務とデータ削除境界を表にする