# AI向け 作業指示 Codex Q5b TestCompileDrift小口補正 2026-03-23

最終更新日: 2026-03-23

変更概要:
- test project build の残 blocker から、小口で直る test drift を 1 レーンへまとめる

## 1. 目的

- source 契約の変更に追従できていない test を、現行 source へ再整合して test build を前進させる

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/ManualPlayerResizeHookPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/StartupUiHangActivitySourceTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailLayoutProfileTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/RescueWorkerApplicationTests.cs`
- 必要なら参照だけ
  - `Views/Main/MainWindow.Player.cs`
  - `Views/Main/MainWindow.Startup.cs`
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailDetailModeRuntime.cs`
  - `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`

## 3. 現状の停止点

- `MainWindow` helper 名 drift
- `StringAssert` 未解決
- `ThumbnailDetailModeRuntime.Standard` / `WhiteBrowserCompatible` 未存在
- `RescueWorkerApplication.TryParseArguments(...)` の引数数 drift

## 4. 守ること

1. helper 名 drift は test を現行 source に寄せる
2. `ThumbnailDetailModeRuntime` に旧 alias 定数を安易に戻さない
3. `TryParseArguments` は現行の `requestedFailureId` まで含む契約へ test を寄せる
4. source 変更が不要なら test のみで閉じる

## 5. 着地イメージ

- manual player
  - `ResolveManualPlayerViewportSize(...)` と現行 hook 方針に沿う test へ整理
- startup ui hang
  - `StringAssert` 依存を NUnit 現行 API で解消
- detail mode
  - `Grid` / `WhiteBrowser` など現行 runtime 名へ寄せる
- rescue worker args
  - `requestedFailureId` の out を受ける

## 6. 検証

- `MSBuild.exe Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj /t:Build /p:Configuration=Debug /p:Platform=x64`
- 可能なら対象テスト絞り込み
  - `dotnet test ... --filter "ManualPlayerResizeHookPolicyTests|StartupUiHangActivitySourceTests|ThumbnailLayoutProfileTests|RescueWorkerApplicationTests"`

## 7. 禁止

- `Views/Main/MainWindow.Player.cs` へ不要な test 専用 helper を足すこと
- `ThumbnailDetailModeRuntime` に legacy alias を追加して場当たりで合わせること
- worker 本体ロジックの変更を混ぜること
