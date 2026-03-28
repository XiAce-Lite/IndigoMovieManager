# AI向け レビュー結果 LaneB MainWindowMovieReadFacade Phase1 2026-03-20

最終更新日: 2026-03-20

## 1. 対象

- T4: LaneB MainWindow movie read facade Phase1

## 2. 結論

- blocking な finding は無い
- `MainWindow` の read-only DB 入口は、今回対象にした 4 口について facade へ寄った
- 初回レビューで出た `Data -> Startup` 逆依存と `ReadRegisteredMovieCount` テスト不足は解消した
- T4 は受け入れとする

## 3. レビュー結果

- findingsなし

## 4. 残留リスク

- `MainDbMovieReadFacadeTests` は正常系中心で、sort マップの `"28"` や未知の sortId の既定動作までは固定していない
- `MainWindow` 側で facade 配線が維持されることを保証する統合テストはまだ無い

## 5. 主な確認点

- `Data/MainDbMovieReadFacade.cs`
  - `Data -> Startup` 依存が消えている
  - read-only 契約が `Data` 専用 DTO に閉じている
- `Views/Main/MainWindow.Startup.cs`
  - startup page 読みが facade 呼び出しへ寄っている
- `Views/Main/MainWindow.xaml.cs`
  - registered count / `GetSystemTable` / full reload の read が facade 経由へ寄っている
- `Tests/IndigoMovieManager_fork.Tests/MainDbMovieReadFacadeTests.cs`
  - `ReadRegisteredMovieCount` を含む 4 本のテストがある

## 6. 調整役判断

- T4 は受け入れ
- 次は Lane B の 2 位候補である watcher の movie read/write 入口へ進む
- T4 の残留リスクは後続の guard / 統合テスト補強として別タスクへ切る
