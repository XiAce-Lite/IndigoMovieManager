# AI向け レビュー結果 Q5b TestCompileDrift小口補正 2026-03-23

最終更新日: 2026-03-23

変更概要:
- test compile drift を source 正本へ寄せる小口補正を 4 ファイルへ閉じて実施した
- `WatcherRenameBridgePolicyTests` から現行 source に存在しない旧 API 依存を外し、rename bridge 専用テストへ戻した
- clean worktree では tests project build 成功まで確認した

## 1. 対象

- `Tests/IndigoMovieManager_fork.Tests/ManualPlayerResizeHookPolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/StartupUiHangActivitySourceTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs`
- `Tests/IndigoMovieManager_fork.Tests/WatcherRenameBridgePolicyTests.cs`

## 2. 実行場所

- 実装 clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q5b-test-drift`
- review synth worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q5b-review-synth`

## 3. 着地

- `ManualPlayerResizeHookPolicyTests.cs`
  - `MainWindow` 解決のため `using IndigoMovieManager;` を追加
- `StartupUiHangActivitySourceTests.cs`
  - source path 解決と `StringAssert` 依存を現行テスト環境へ寄せた
- `MissingThumbnailRescuePolicyTests.cs`
  - `ResolveManualPlayerViewportSize` の 2 テストだけ `System.Windows.Size` を明示し、`Size` あいまいさを解消した
- `WatcherRenameBridgePolicyTests.cs`
  - 現行 source に存在しない stale test を削除
    - `ResolveWatchEventQueueGuardAction_DB切替後やscope更新後はstaleをdropする`
    - `ProcessCreatedWatchEventDirectAsync_*` の連続ブロック
  - 未使用になった `CreateMovieInfo` helper も削除
  - `TryEnterRenameBridgeForWatchScope...` と `ProcessRenamedWatchEventDirect...` 系は維持した

## 4. 検証

- `git diff --check -- Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs Tests/IndigoMovieManager_fork.Tests/WatcherRenameBridgePolicyTests.cs`
  - 通過
- `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /nologo /verbosity:minimal`
  - 成功
  - warnings only
    - `AutogenRegressionTests.cs` nullability warning
    - `NETSDK1206` (`SQLitePCLRaw.lib.e_sqlite3` / `alpine-x64`)

## 5. レビュー結果

- `Q5b` 専用 review は `q5b-review-synth` で取り直した
- synth review の最終所見は
  - この 4 ファイル差分単体では既存動作を壊す明確な問題は見当たらない
- `q5b-test-drift` 全体 review に混ざった `WatcherRegistrationDirectPipelineTests.cs` への指摘は `Q5a` 対象なので acceptance 根拠から除外した

## 6. 判定

- 実装判定
  - 受け入れ
- レビュー判定
  - accepted
- commit
  - 未実施
  - `Q5a` を含めず、4 ファイルだけで別 commit にする

## 7. 次アクション

- `Q5b` 対象 4 ファイルだけで clean commit synth を作る
- `Q5a` と `Q5b` は別 commit 専用サブエージェントへ渡す
