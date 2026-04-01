# Implementation Note: スキン検索 bridge 最小実装 2026-04-02

最終更新日: 2026-04-02

変更概要:
- WhiteBrowser 互換スキンから `wb.find(...)` で本体検索を呼べる最小 bridge を追加した。
- 検索ロジックは増やさず、既存の `FilterAndSort(...)` と `SearchKeyword` 更新導線を再利用する形にした。
- `SearchBox` 表示と外部スキン検索要求の状態ズレを避けるため、検索文字列適用入口を `MainWindow.Search.cs` へ 1 か所に寄せた。

## 1. 今回の境界

- スキン側:
  - `skin/Compat/wblib-compat.js`
  - `wb.find(keyword, startIndex, count)`
- 本体側:
  - `WhiteBrowserSkinApiService`
  - `MainWindow.WebViewSkin.Api.cs`
  - `MainWindow.Search.cs`

検索仕様の正本は依然として本体側にあり、スキン側は検索 UI と要求送信だけを持つ。

## 2. 実装方針

### 2.1 `wb.find(...)`

- WebView2 bridge へ `find` メソッドを追加
- 実行後は `update` と同じ `WhiteBrowserSkinUpdateResponse` を返す
- callback は `onUpdate` を再利用する

### 2.2 本体検索の共通入口

`MainWindow.Search.cs` に検索文字列適用入口を追加し、次を 1 か所で行う。

- `SearchBox.Text` 同期
- `MainVM.DbInfo.SearchKeyword` 更新
- `RestartThumbnailTask()`
- `FilterAndSort(MainVM.DbInfo.Sort, true)`
- `SelectFirstItem()`

これにより、WPF の検索ボックス起点と外部スキン起点が同じ本体検索へ収束する。

## 3. 注意点

- 検索履歴の保存は今回まだ WPF イベント系のまま
- タグ専用構文や saved search 統合は未着手
- 今回は「検索 bridge の最小成立」が目的であり、検索仕様の再設計は次段で扱う

## 4. 確認項目

- `wb.find(...)` で `FilteredMovieRecs` が更新される
- 検索後の戻り payload が `update` 相当になっている
- `SearchBox` 表示と `SearchKeyword` がズレない
