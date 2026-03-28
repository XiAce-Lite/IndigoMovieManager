# AI向け 作業指示 Codex LaneB MainWindowMovieReadFacade Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. あなたの役割

- あなたは実装役である
- 今回は `Lane B: Data 入口集約` の最初の 1 本だけを担当する
- 対象は `MainWindow movie read` の facade 化に限定する

## 2. 目的

- `MainWindow` と `StartupDbPageReader` に散っている MainDB read 入口を、後で `Data DLL` へ移しやすい 1 本の facade へ寄せる
- UI が SQL 文字列と read-only 接続詳細を直接握る箇所を減らす
- ただし今回は project 分離までは進めない

## 3. 今回の対象

- `Startup\StartupDbPageReader.cs`
- `Views\Main\MainWindow.xaml.cs`
- `Views\Main\MainWindow.Startup.cs`
- 必要なら新規 facade ファイル
- 関連テスト

対象 read 入口は次の 4 口に固定する。

1. 登録件数ヘッダー取得
2. `system` テーブル読取
3. 一覧フル再読込
4. 起動時 first-page / append-page 読取

## 4. 今回やること

1. `Data DLL` へ移しやすい名前と責務で read facade を新設する
2. 上記 4 口の read を、その facade 経由へ寄せる
3. `MainWindow` 側には UI 状態反映だけを残す
4. `StartupDbPageReader` は facade 配下へ寄せるか、facade の内部実装として隠す

## 5. 今回やらないこと

- 新しい `.csproj` や `Data DLL` の実 project 作成
- `history` / `watch` / `bookmark` / `system write` の facade 化
- `watch` 専用 read/write facade への着手
- MainDB schema 変更
- `GetData(...)` 全廃

## 6. 実装の方向

- 今は実 project が無いので、`App` 側に「後で `Data DLL` へそのまま移しやすい facade」を仮置きする
- namespace や folder 名は `Data` を意識してよい
- ただし UI 専用の状態変換まで facade 側へ入れない
- facade は read-only に限定し、write を混ぜない

想定する叩き台:

- `IMainDbMovieReadFacade`
- `MainDbMovieReadFacade`
- `ReadRegisteredMovieCount(...)`
- `LoadSystemTable(...)`
- `LoadMovieTableForSort(...)`
- `ReadStartupPage(...)`

名前は多少変えてよいが、4 口が 1 本の read facade に見えることを優先する。

## 7. 触ってはいけないこと

- `Watcher` の `visible-only gate` / deferred batch / UI 抑制
- `ThumbnailCreationService` の `Factory + Interface + Args`
- `RescueWorkerApplication` の MainDB read-only 例
- `DB/SQLite.cs` の大整理や責務全面移動

## 8. 最低限の確認

- 関連テストの追加または更新
- 可能なら対象 read facade の軽いテスト
- `IndigoMovieManager_fork.csproj` の build
- `git diff --check`

## 9. 完了条件

1. 4 口の MainDB read が facade 経由へ寄っている
2. `MainWindow` 直下から read-only 接続と SQL 直書きが減っている
3. UI 状態反映は `MainWindow` 側に残っている
4. 今後 `Data DLL` へ移す時に、まずこの facade を動かせばよい形になっている

## 10. 次へ渡す相手

- レビュー専任役 Claude / Opus
