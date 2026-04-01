# Host Chrome Minimal 初期実装メモ 2026-04-02

## 目的

- 外部 WhiteBrowser スキン表示中に、既存の重いメインヘッダーをそのまま見せず、最小シェルへ切り替える。
- 「外部スキンを一回動かす」ために必要な戻り道と再読込だけを最短で提供する。

## 今回入れる範囲

- `Views/Main/MainWindow.xaml`
  - 既存メインヘッダーを `MainHeaderStandardChromePanel` として包む
  - 外部スキン時だけ見せる `ExternalSkinMinimalChromePanel` を追加する
- `Views/Main/MainWindow.WebViewSkin.Chrome.cs`
  - Minimal host chrome の表示切替
  - `Gridへ戻る`
  - `再読込`
  - `設定`
- `Views/Main/MainWindow.WebViewSkin.cs`
  - host ready 時に minimal chrome へ切り替える配線
- `Tests/IndigoMovieManager.Tests/MainWindowWebViewSkinIntegrationTests.cs`
  - minimal chrome の可視状態と `Gridへ戻る` を確認する

## Minimal の仕様

- 外部スキン表示中は、既存の検索 / ソート / 件数 / DB パスの標準ヘッダーを隠す
- 代わりに次だけを出す
  - 現在 DB 名
  - 現在スキン名
  - `再読込`
  - `Gridへ戻る`
  - `設定`
- 左上のメニュートグルは共通導線として残す

## いま入れないもの

- host chrome mode のユーザー切替 UI
- スキン側検索 bridge
- skin ごとの host chrome on/off 指定
- `Small / Big / List / 5x2` の内蔵スキン化

## 一言で言うと

今回は「外部スキン時に MainHeader を最小シェルへ差し替える」だけを先に通す。
