# AI向け レビュー結果 Q6c ManualMetadataPrecheckDrift解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `ThumbnailCreateWorkflowCoordinatorTests` の manual metadata ケースを、旧 `precheck` 即失敗期待から現行 `ThumbnailJobContextBuilder` fallback 契約へ寄せた
- clean worktree では対象周辺テストが通過し、レビュー専任役 `findings なし` を確認した
- main 側の同一ファイルは未汚染だったため、本線 commit `bb70b966b9f4153cc21dda80dddef676c4ef4044` で取り込んだ

## 1. 対象

- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreateWorkflowCoordinatorTests.cs`

## 2. 実行場所

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q6c-manual-metadata-precheck`

## 3. 着地

- 旧テスト
  - `ExecuteAsync_manualでWB互換メタが無ければprecheck失敗を返す`
- 新テスト
  - `ExecuteAsync_manualでWB互換メタが無ければfallback後にautogenで続行する`
- 期待値
  - `IsSuccess == true`
  - `ProcessEngineId == "autogen"`
  - `writer.Entries[0].EngineId == "autogen"`
  - `autogen.CreateCallCount == 1`

## 4. 根拠

- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateWorkflowCoordinator.cs`
  - `precheck` 即返却は `HasImmediateResult` の時だけ
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailPrecheckCoordinator.cs`
  - manual で即失敗にしているのは `manual target thumbnail does not exist` だけ
  - WB 互換メタ欠落の即失敗分岐は無い
- `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailJobContextBuilder.cs`
  - manual metadata 欠落時は `ThumbnailAutoThumbInfoBuilder.Build(...)` へ fallback して続行する
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailJobContextBuilderTests.cs`
  - fallback 契約を既に固定済み

## 5. 検証

- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ThumbnailCreateWorkflowCoordinatorTests"`
  - 成功
  - `2` 件合格
- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj --filter "FullyQualifiedName~ThumbnailJobContextBuilderTests|FullyQualifiedName~ThumbnailPrecheckCoordinatorTests"`
  - 成功
  - `11` 件合格
- `git diff --check`
  - 成功

## 6. レビュー結果

- レビュー専任役
  - `findings なし`
- 調整役判断
  - 受け入れ

## 7. 残留リスク

- `Views/Main/MainWindow.Player.cs` と rescue 系テストには旧 `"manual source thumbnail metadata is missing"` 文言互換が残っている
- ただし今回の workflow test drift と責務は別であり、Q6c では扱わない

## 8. 本線取り込み結果

- 本線 commit
  - `bb70b966b9f4153cc21dda80dddef676c4ef4044`
  - `manual metadata workflowテストを現仕様へ寄せる`
- 取り込み対象
  - `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreateWorkflowCoordinatorTests.cs`
