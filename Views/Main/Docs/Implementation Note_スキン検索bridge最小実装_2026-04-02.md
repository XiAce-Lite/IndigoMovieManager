# Implementation Note: スキン検索 bridge 最小実装 2026-04-02

最終更新日: 2026-04-07

変更概要:
- WhiteBrowser 互換スキンから `wb.find(...)` で本体検索を呼べる最小 bridge を追加した。
- 検索ロジックは増やさず、既存の `FilterAndSort(...)` と `SearchKeyword` 更新導線を再利用する形にした。
- `SearchBox` 表示と外部スキン検索要求の状態ズレを避けるため、検索文字列適用入口を `MainWindow.Search.cs` へ 1 か所に寄せた。
- `SearchBox` の Enter も同じ共通入口へ寄せ、既定 `Reload` ボタンへ流れずに検索確定できるようにした。
- Enter 後の検索履歴再読込では、編集中テキストを壊さないよう `SearchBox` の副作用を抑えて同期する形へ直した。

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
- resolve は本体検索完了後に返し、古い `FilteredMovieRecs` を即返さない

### 2.2 本体検索の共通入口

`MainWindow.Search.cs` に検索文字列適用入口を追加し、次を 1 か所で行う。

- `SearchBox.Text` 同期
- `MainVM.DbInfo.SearchKeyword` 更新
- `RestartThumbnailTask()`
- `FilterAndSortAsync(MainVM.DbInfo.Sort, true)` の完了待ち
- `SelectFirstItem()`

これにより、WPF の検索ボックス起点と外部スキン起点が同じ本体検索へ収束する。

2026-04-07 時点では、WPF 検索ボックスの Enter 確定もこの入口へ揃えている。
以前のような `PreviewKeyDown` 内の既定ボタン遷移や、その場での危うい履歴再読込には戻さない。

## 3. 注意点

- 検索履歴の保存はまだ WPF イベント系だが、Enter 確定は共通検索完了後に同期する
- タグ専用構文や saved search 統合は未着手
- 今回は「検索 bridge の最小成立」が目的であり、検索仕様の再設計は次段で扱う

## 4. 確認項目

- `wb.find(...)` で `FilteredMovieRecs` が更新される
- 検索後の戻り payload が `update` 相当になっている
- `SearchBox` 表示と `SearchKeyword` がズレない
