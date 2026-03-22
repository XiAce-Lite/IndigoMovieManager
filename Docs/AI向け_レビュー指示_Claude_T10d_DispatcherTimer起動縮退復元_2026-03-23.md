# AI向け レビュー指示 Claude T10d DispatcherTimer起動縮退復元 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- 起動直後の `DispatcherTimer` 失敗を縮退で吸収できるか、`TryStartDispatcherTimer(...)` の fault 伝播が戻っているかを判定する

## 2. レビュー対象

- `App.xaml.cs`
- `Views/Main/MainWindow.DispatcherTimerSafety.cs`
- `Tests/IndigoMovieManager_fork.Tests/AppDispatcherTimerExceptionPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/UiHangNotificationPolicyTests.cs`

## 3. 必ず見る観点

- `DispatcherUnhandledException` 登録順が `StartupUri` 前提で正しいか
- start fault で `HasDispatcherTimerInfrastructureFault` が立つか
- fault 後 cleanup が stop 例外で中断しないか
- テストが runtime 契約を固定しているか

## 4. 今回レビューしないもの

- `Watcher`
- `RenameBridge`
- `MainWindow.xaml.cs` の unrelated change

## 5. 出力形式

- findings を severity 順で列挙
- なければ `findings なし`
- 最後に受け入れ可否を 1 行で明記
