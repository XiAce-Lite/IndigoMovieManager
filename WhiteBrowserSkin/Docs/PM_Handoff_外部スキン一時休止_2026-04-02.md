# PM Handoff 外部スキン一時休止 2026-04-02

## 目的

- WebView2 外部スキン実装の現在地を、次回すぐ再開できる形で固定する。
- 今回の実害、修正済み範囲、確認済み結果、次の優先候補を 1 枚へ集約する。

## 現在地

- 外部スキンの表示経路は、実アプリ起動で動作確認済み。
- `SimpleGridWB` は実 DB 復元経路でも表示される。
- `Host Chrome Minimal` は有効。
- 検索 bridge は最小実装まで入っている。
- `skin` は runtime asset 専用、source は `WhiteBrowserSkin` へ分離済み。

## 今回の主修正

### 1. 起動時外部 skin host の初期化安定化

- コミット: `25b8829`
- 要点:
  - `Views/Main/MainWindow.WebViewSkin.cs`
    - 外部 skin host を `ExternalSkinHostPresenter` へ `Hidden` で仮マウントしてから WebView2 初期化するよう変更
    - `skin-webview` の追跡ログを追加
  - `Views/Main/MainWindow.xaml.cs`
    - DB 起動完了時に `boot-new-db` refresh を明示
  - `WhiteBrowserSkin/MainWindow.Skin.cs`
    - `ApplySkinByName` 成功時に `apply-skin` refresh を明示
  - `Tests/IndigoMovieManager.Tests/MainWindowWebViewSkinIntegrationTests.cs`
    - 仮マウント順と起動経路の表示切替を固定

### 2. 外部 skin 動作確認用 sample 追加

- コミット: `1c4ef0c`
- 要点:
  - `skin/SimpleGridWB/SimpleGridWB.htm`
  - `skin/SimpleGridWB/SimpleGridWB.css`
  - 今の compat ランタイムで確実に動く最小 sample skin

### 3. sample skin の文字化け修正

- コミット: `7e3c928`
- 要点:
  - repo 内 UTF-8 sample skin が `charset=Shift_JIS` のままだったため、`SimpleGridWB` が文字化けしていた
  - `skin/SimpleGridWB/SimpleGridWB.htm`
  - `skin/DefaultGridWB/DefaultGridWB.htm`
  - を `<meta charset="utf-8">` へ修正
  - `Tests/IndigoMovieManager.Tests/WhiteBrowserSkinEncodingNormalizerTests.cs`
    - repo 内 UTF-8 sample skin を文字化けさせず読めるテストを追加

## 実害と原因

- 実アプリでは、外部 skin host を visual tree へ載せる前に `TryNavigateAsync` を呼んでいた
- そのため WebView2 初期化待ちが止まり、`system.skin=SimpleGridWB` でも画面が変わらないことがあった
- さらに sample skin 側は UTF-8 実ファイルなのに `Shift_JIS` 宣言のままで、日本語が文字化けしていた

## 確認済み

### 実アプリログ

- `C:\Users\na6ce\AppData\Local\IndigoMovieManager\logs\debug-runtime.log`
- 実起動ログで次を確認済み
  - `host presentation: active=True ready=True skinRaw='SimpleGridWB' ... reason=boot-new-db`

### テスト

- `MainWindowWebViewSkinIntegrationTests | WhiteBrowserSkin`
  - `43/43` 合格
- `WhiteBrowserSkinEncodingNormalizerTests | WhiteBrowserSkinRenderCoordinatorTests`
  - `4/4` 合格

### ビルド

- `dotnet build IndigoMovieManager.csproj -c Debug -p:Platform=x64`
  - 成功

## いま使える sample

- repo 正本:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager\skin\SimpleGridWB\SimpleGridWB.htm`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager\skin\SimpleGridWB\SimpleGridWB.css`
- 出力先:
  - `C:\Users\na6ce\source\repos\IndigoMovieManager\bin\x64\Debug\net8.0-windows10.0.19041.0\skin\SimpleGridWB\SimpleGridWB.htm`
  - `C:\Users\na6ce\source\repos\IndigoMovieManager\bin\x64\Debug\net8.0-windows10.0.19041.0\skin\SimpleGridWB\SimpleGridWB.css`

## 次にやる候補

1. `wb.sort` を追加する
2. `SimpleGridWB` に並び替え UI を足す
3. `onCreateThum` 互換へ進み、旧 WB 普及スキンの受け皿を広げる
4. `Grid` 以外の built-in 一覧を段階的に内蔵スキン化する

## 注意点

- 作業ツリーには今回と無関係の未コミット差分がまだ多い
- 次回も 1 コミット 1 目的で、対象ファイルだけを選択的に扱うこと
- runtime asset は `skin`
- source / docs は `WhiteBrowserSkin`
- `DefaultSmall` など built-in 予約名は外部 skin 名として使わない

