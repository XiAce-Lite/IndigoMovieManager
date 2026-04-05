# PM Handoff: WebView2 P2 WebView側フック点 2026-04-01

最終更新日: 2026-04-01

変更概要:
- 元ツリーの WebView 側から見た P2 実装の最小フック点を整理した
- `MovieRecords` / `MainVM.FilteredMovieRecs` / 既存選択 helper を前提に、`wb.update` 系の受け口をどこへ置くべきかを明文化した
- dirty な `Views/Main/MainWindow.xaml.cs` を避けるための partial 分割方針を固定した
- 統合前に先行実装できる部分と、サムネ側合流待ちの論点を切り分けた

## 1. 目的

本書は、元ツリー側で進める WebView2 P2 実装に対し、
「どこへフックすれば最小変更で前に進めるか」を実装班へ渡すための PM メモである。

対象は次の 3 点である。

1. `MovieRecords` と `MainVM.FilteredMovieRecs` から `wb.update / getInfo / getInfos` 用 DTO を作る入口
2. `focusThum` で WPF 側選択を更新する最小経路
3. dirty な `Views/Main/MainWindow.xaml.cs` を避けて new partial へ逃がす連携点

## 2. 調査で確認した事実

### 2.1 一覧データの正本

- 一覧表示の現在順は `MainVM.FilteredMovieRecs` が持っている
- 元データ全体は `MainVM.MovieRecs` にある
- `MainWindowViewModel.ReplaceFilteredMovieRecs(...)` / `ReplaceMovieRecs(...)` で更新される

したがって、

- `wb.update` は **`MainVM.FilteredMovieRecs` の現在順** をそのまま DTO 化する
- `wb.getInfo / wb.getInfos` は **同じ `FilteredMovieRecs` スナップショット上の lookup** をまず正本にする

のが最も自然である。

### 2.2 選択状態の正本

- 現在選択 1 件の取得は `GetSelectedItemByTabIndex()` で取れる
- 現在前面にいる通常タブへ選択を返す入口は `SelectCurrentUpperTabMovieRecord(MovieRecords record)` がある
- その内部は `SelectUpperTabMovieRecord(...)` -> 各タブ helper へ流れている

したがって `focusThum` は、

- UI スレッドで対象 `MovieRecords` を引く
- `SelectCurrentUpperTabMovieRecord(record)` を呼ぶ

だけで最小成立する。

### 2.3 dirty 回避

- `Views/Main/MainWindow.xaml.cs` は既に他件差分が多い
- `Views/Main/MainWindow.WebViewSkin.cs` は WebView 側の partial として既に存在する
- `WhiteBrowserSkin/Host/WhiteBrowserSkinHostControl.xaml.cs` と `WhiteBrowserSkin/Runtime/WhiteBrowserSkinRuntimeBridge.cs` は P2 の自然な拡張点である

したがって P2 の WebView 側は、

- `Views/Main/MainWindow.WebViewSkin.Api.cs`
- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinApiService.cs`
- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinMovieDto*.cs`

のような **new partial / new runtime file** 追加で逃がすのが安全である。

## 3. 推奨フック点

### 3.1 DTO 生成入口

推奨入口は **`WhiteBrowserSkinApiService`** 新設である。

責務は次に絞る。

- `MainVM.FilteredMovieRecs` を読む
- `MainVM.DbInfo.DBFullPath` から `dbIdentity` を作る
- `MovieRecords.Movie_Id` から `recordKey` を作る
- 現在タブに応じた表示用サムネパスを選ぶ
- `wb.update / getInfo / getInfos` の戻り DTO を返す

依存は C# の delegate 注入で薄く持つ。

- `Func<IReadOnlyList<MovieRecords>> getVisibleMovies`
- `Func<int> getCurrentTabIndex`
- `Func<string> getCurrentDbFullPath`
- `Func<string> getCurrentDbName`
- `Func<MovieRecords> getCurrentSelectedMovie`
- `Action<MovieRecords> selectMovie`

この形なら `MainWindow` 直結を最小にできる。

### 3.2 `focusThum` の最小経路

`focusThum` の実装は **`movieId` から `MovieRecords` を引いて既存 helper を呼ぶ** だけでよい。

推奨手順:

1. `FilteredMovieRecs` から `Movie_Id == movieId` のレコードを探す
2. `Dispatcher` 上で `SelectCurrentUpperTabMovieRecord(record)` を呼ぶ
3. 成功時は `selected=true` 相当の応答を返す
4. 失敗時は reject ではなく `found=false` でもよい

ここで独自に選択状態を保持してはいけない。
選択の正本は WPF 側のままにする。

### 3.3 Host / MainWindow の連携

既存 `InitializeWebViewSkinIntegration()` から伸ばすのが自然である。

推奨は次の分離:

- `WhiteBrowserSkin/Host/WhiteBrowserSkinHostControl.xaml.cs`
  - runtime bridge の message event を外へ出す
- `Views/Main/MainWindow.WebViewSkin.Api.cs`
  - host event を購読し、`WhiteBrowserSkinApiService` へ委譲する
- `WhiteBrowserSkin/Runtime/WhiteBrowserSkinRuntimeBridge.cs`
  - resolve / reject / callback dispatch の transport に徹する

この形なら `Views/Main/MainWindow.xaml.cs` を触らずに済む。

## 4. 実装班への具体指示

### 4.1 WebView 側だけで先行実装してよい範囲

- `wb.update`
- `wb.getInfo`
- `wb.getInfos`
- `wb.focusThum`
- `wb.getSkinName`
- `wb.getDBName`
- `wb.getThumDir`
- `wb.trace`

加えて、

- `recordKey`
- `dbIdentity`
- `thumbRevision`
- 寸法 DTO 項目

を DTO 契約としてコードに落としてよい。

### 4.2 dirty 回避のため触らない方がよいファイル

- `Views/Main/MainWindow.xaml.cs`

やむを得ず連携が必要でも、まず new partial を追加して逃がすこと。

## 5. 注意点

### 5.1 `ThumbPath*` はそのまま `thum.local` 化できない場合がある

`CreateMovieRecordFromDataRow(...)` のサムネ解決は、

- 管理サムネ
- 旧命名サムネ
- same-name source image fallback

を混在で返している。

このため `MovieRecords.ThumbPathSmall / Big / Grid / List / Big10` は、
**thumb root 外の source image を指すことがある。**

つまり、

- `thum.local` の単純な folder mapping だけでは足りない可能性が高い

ということだ。

P2 の WebView 側では、

- DTO 契約だけ先に固める
- 実 `thumbUrl` の最終供給方式はサムネ側との統合論点として切り出す

のが安全である。

### 5.2 寸法 DTO は `MovieRecords` だけでは埋まらない

現状 `MovieRecords` は、

- `thumbNaturalWidth`
- `thumbNaturalHeight`
- `thumbSheetColumns`
- `thumbSheetRows`

を持っていない。

したがって WebView 側だけで前倒しする場合は、

- 既存 `ThumbInfo` 相当を使う軽い metadata resolver を API service 側へ足す
- もしくは統合まで placeholder 値を返す

の判断が必要である。

契約上は必須なので、**無言で欠落させてはいけない。**

### 5.3 `thumbRevision` は WebView 側だけでは正本にならない

現状 `MovieRecords` に改訂番号は載っていない。

したがって P2 WebView 側でできるのは、

- フィールドと更新フローの受け口を先に作ること
- 必要なら暫定値を明示的に扱うこと

までである。

`thumbRevision` の正本供給は、サムネ側統合時の確認項目として残る。

## 6. PM 結論

元ツリーの WebView 側だけでも、P2 はかなり進められる。

先に進めるべきなのは次である。

1. `WhiteBrowserSkinApiService` と DTO 契約
2. `MainWindow.WebViewSkin.Api.cs` の new partial
3. `wblib-compat.js` の Promise API と callback 受け口整理

統合待ちに回すべきなのは次である。

1. `thumbUrl` の最終供給方式
2. `thumbRevision` の正本生成
3. source image fallback を含む URL 解決
4. 寸法 DTO の正本供給

この切り分けなら、統合までに WebView 側 70〜80% は前進できる。
