# AI向け 作業指示 Codex T10d DispatcherTimer起動縮退復元 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- `StartupUri` 継続下で、起動直後の `DispatcherTimer` 失敗を縮退前に落とさないよう戻す
- `TryStartDispatcherTimer(...)` の `Win32Exception` で fault 状態が立つ契約を戻す

## 2. 対象ファイル

- `App.xaml.cs`
- `Views/Main/MainWindow.DispatcherTimerSafety.cs`
- `Tests/IndigoMovieManager_fork.Tests/AppDispatcherTimerExceptionPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/UiHangNotificationPolicyTests.cs`

## 3. 必達

- `DispatcherUnhandledException` 登録が `base.OnStartup(e)` より前に来る
- `TryStartDispatcherTimer(...)` の `Win32Exception` が fault 状態を立てる
- fault 後の cleanup で追加例外を出さず、縮退停止が最後まで走る
- 上の契約がテストで見える

## 4. 禁止

- `Watcher` 系ファイルを触らない
- `MainWindow.xaml.cs` の unrelated change を混ぜない
- docs 更新をこのレーンへ混ぜない

## 5. 受け入れ条件

- `dispatcher timer` 縮退だけでレビュー専任役が `findings なし`
- 対象外ファイルへ差分が広がらない
- startup registration 順と start fault 伝播がテストで固定される

## 6. 返却時に必要な報告

- 変更ファイル一覧
- 追加 / 修正テスト一覧
- 実行した確認コマンドと結果
- 残リスク
