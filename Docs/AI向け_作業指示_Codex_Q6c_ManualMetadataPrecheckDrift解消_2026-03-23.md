# AI向け 作業指示 Codex Q6c ManualMetadataPrecheckDrift解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `ThumbnailCreateWorkflowCoordinatorTests` の manual metadata ケースが、旧「precheck 即失敗」契約を期待して落ちている
- 現行 source では manual metadata 欠落は `ThumbnailJobContextBuilder` の fallback で吸収しており、precheck 即失敗へは流れていない
- runtime を戻す前に、現仕様がどこにあるかを確認し、test を正本へ寄せるかだけを最小帯で判断する

## 1. 目的

- failing test 1 件を、現行 thumbnail engine 契約に沿って解消する
- `ThumbnailPrecheckCoordinator` へ manual metadata 即失敗を戻さず、必要最小限の修正に閉じる

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/ThumbnailCreateWorkflowCoordinatorTests.cs`
- 参照だけ
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateWorkflowCoordinator.cs`
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailPrecheckCoordinator.cs`
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailJobContextBuilder.cs`
  - `Views/Main/MainWindow.Player.cs`
  - `Thumbnail/MainWindow.ThumbnailRescueLane.cs`
  - `src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerApplication.cs`

## 3. 現状の根拠

- `ThumbnailCreateWorkflowCoordinator.ExecuteAsync(...)`
  - `precheckCoordinator.Run(...)` が `HasImmediateResult == true` の時だけ `precheck` で即返す
- `ThumbnailPrecheckCoordinator.Run(...)`
  - manual metadata 欠落を即失敗にする分岐は存在しない
- `ThumbnailJobContextBuilder.ResolveThumbInfo(...)`
  - manual かつ WB 互換メタ欠落時は `ThumbnailAutoThumbInfoBuilder.Build(...)` へ fallback し、作り直し用 `ThumbInfo` を返す
  - ここで `manual metadata fallback` を runtime log へ書いている
- `ThumbnailJobContextBuilderTests`
  - `Build_manual生成でWB互換メタが無ければ現在位置を使って作り直し用contextを返す`
  - 既に現仕様を固定している
- `MainWindow.Player.cs`
  - `"manual source thumbnail metadata is missing"` 文言は残っているが、workflow precheck の現在仕様とは一致していない可能性が高い
- `ThumbnailRescueLane` / `RescueWorkerApplication`
  - WB 互換メタ欠落時に「再生成対象へ残す」判断は rescue 側に残っている

## 4. 守ること

1. `ThumbnailPrecheckCoordinator` に manual metadata 即失敗分岐を戻さない
2. `ThumbnailCreateWorkflowCoordinator` / `ThumbnailPrecheckCoordinator` / `ThumbnailJobContextBuilder` の source は、根拠が無い限り触らない
3. 変更は最小帯に閉じる
4. もし source 修正が必要だと判断した場合は、manual metadata 契約がどこで使われているかを明記する

## 5. 着地イメージ

- 第一候補
  - `ThumbnailCreateWorkflowCoordinatorTests.ExecuteAsync_manualでWB互換メタが無ければprecheck失敗を返す`
  - を現仕様に合わせて成功系へ更新する
  - `ProcessEngineId == "autogen"` と `autogen.CreateCallCount == 1` を見る
- 代替候補
  - test 名と期待値だけでなく、manual metadata 欠落が runtime のどこで扱われるかを補足コメントで固定する
- source を戻す案は、明確な runtime 回帰根拠がある時だけ許可

## 6. 検証

- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ThumbnailCreateWorkflowCoordinatorTests"`
- `git diff --check`

## 7. 禁止

- test を通すためだけに manual metadata 即失敗を workflow precheck へ戻すこと
- rescue 側の再生成条件と workflow precheck を混同すること
- unrelated change を `ThumbnailCreateWorkflowCoordinatorTests.cs` 以外へ広げること
