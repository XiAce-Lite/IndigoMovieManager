# AI向け レビュー結果 Q8a StartupActivity帯 no-op 判定 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `Q8a` の最小候補として `Tests/IndigoMovieManager_fork.Tests/StartupUiHangActivitySourceTests.cs` だけを clean worktree へ再構成した
- `StringAssert` 由来の compile blocker は fix1 で解消し、対象テスト `2 pass` まで確認した
- ただし review で `CallerFilePath` フォールバック削除が回帰と判定されたため、この帯は no-op / 凍結扱いにした

## 1. 対象

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q8a-startup-activity`
- 対象ファイル
  - `Tests/IndigoMovieManager_fork.Tests/StartupUiHangActivitySourceTests.cs`

## 2. 実施内容

1. main worktree の dirty 版を clean worktree へ移植
2. `StringAssert` 未解決を `Assert.That(..., Does.Contain(...))` へ戻して compile blocker を解消
3. `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter StartupUiHangActivitySourceTests`
4. review 専任役で差分を再確認

## 3. 確認結果

- 対象テスト
  - `2 pass / 0 fail / 0 skip`
- review 判定
  - 受け入れ非推奨
- 主 finding
  - `GetMainWindowStartupSourcePath()` が `TestContext.CurrentContext.TestDirectory` 起点だけへ縮退しており、従来の `[CallerFilePath]` フォールバックを失っていた
  - そのため repo 外へテスト DLL をコピーする runner では false negative を起こし得る

## 4. 調整役判断

- `Q8a startup activity` の差分は受け入れない
- compile を通すだけでは価値が足りず、残る差分は実行環境許容幅を狭める回帰だけだった
- よってこの帯は `no-op / 凍結` とし、`Q8c` を先に進める

## 5. 次アクション

1. `Q8a` は commit 不要で閉じる
2. `UI hang residual` のうち次に進めるなら、`manual player resize` ではなく別帯へ再分解する
