# Implementation Note DBエラーダイアログ一時抑止 2026-04-11

最終更新日: 2026-04-11

## 目的

- `DB/SQLite.cs` の catch から出る DBエラー popup を、いまだけ抑止する。
- WhiteBrowser 互換と DBエラーの根本原因調査を進める間、連続 popup で操作が止まるのを避ける。
- 後で確実に戻せるよう、対象範囲と復帰条件をここへ固定する。

## 今回の対象

- 対象ファイル: `DB/SQLite.cs`
- 対象: `MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error)` を使っていた catch 群
- 非対象:
  - `Views/Main/MainWindow.xaml.cs` の `OpenDatafile(...)` で出す明示エラー
  - スキーマ不一致時の案内ダイアログ

## 実装内容

1. `SQLite.cs` に `SuppressDbErrorDialogTemporarily = true` を追加
2. 各 catch の popup 呼び出しを `ReportDbError(...)` へ集約
3. suppress 中も `DebugRuntimeLog.Write("db", ...)` で痕跡だけ残す

## 復帰条件

- DBエラーの主因が切り分けできている
- 連続 popup にならないガード方針が決まっている
- 少なくとも `OpenDatafile` / watch 登録 / 外部 skin 表示中の DB切替 / skin profile 更新の代表導線で、ユーザー向けダイアログが必要か再確認できている

## 復帰手順

1. `DB/SQLite.cs` の `SuppressDbErrorDialogTemporarily` を `false` へ戻す
2. suppress 中に追加した `db error dialog suppressed` ログを必要に応じて整理する
3. このメモと `DB/README.md` の記載を更新する

## 補足

- 今回は「原因を隠す」のではなく、「一時的に popup だけ止める」判断である
- 4/11 時点の mixed-query / exact-tag は `SearchKeyword` 同期の第1段であり、popup 復帰判断では DB切替直後の host 再準備と profile 更新導線を優先して確認する
- 外部 skin fallback が `WebView2RuntimeNotFound` の時は DB エラー扱いせず、標準ヘッダーの `Runtimeを入手` 導線を優先する
- 根本原因の調査ログは `%LOCALAPPDATA%\IndigoMovieManager\logs\debug-runtime.log` を使う
