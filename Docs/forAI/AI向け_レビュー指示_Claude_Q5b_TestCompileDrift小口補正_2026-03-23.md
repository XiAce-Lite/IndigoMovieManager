# AI向け レビュー指示 Claude Q5b TestCompileDrift小口補正 2026-03-23

最終更新日: 2026-03-23

## 1. 目的

- 小口 test drift が現行 source 契約へ素直に寄ったかを review する

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/ManualPlayerResizeHookPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/StartupUiHangActivitySourceTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailLayoutProfileTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/RescueWorkerApplicationTests.cs`

## 3. 必須観点

1. source より test を直す方針を守っているか
2. `ThumbnailDetailModeRuntime` や `RescueWorkerApplication` に legacy 契約を戻していないか
3. compile blocker 解消以外の unrelated change が混ざっていないか

## 4. 受け入れ条件

- findings first
- test drift 修正が現行 source と整合している
- 変更が小口に閉じている
