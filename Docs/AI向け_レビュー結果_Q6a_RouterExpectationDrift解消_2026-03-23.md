# AI向け レビュー結果 Q6a RouterExpectationDrift解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `AutogenRegressionTests` の `router expectation drift` 1 件を、現行 ultra-large 契約へ再整合した
- source ではなく test を修正する方針を守り、`AutogenRegressionTests.cs` 1 ファイルだけで閉じた
- clean worktree 検証、レビュー専任役 `findings なし`、本線 commit まで完了した

## 1. 対象

- `Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs`

## 2. 実行場所

- clean worktree
  - `C:\Users\na6ce\source\repos\_imm_agents\q6a-router-drift`

## 3. 着地

- failing test 名を
  - `Router_100枚かつ400Gb超でもUltraLargeならAutogenを先頭にする`
  へ更新した
- コメントを
  - `100 枚かつ 400GB 超でも、先に ultra-large 判定へ入るため autogen を維持する。`
  へ更新した
- 期待値を `ffmpeg1pass` から `autogen` へ変更した

## 4. 根拠

- `Thumbnail/ThumbnailEnvConfig.cs`
  - `DefaultUltraLargeFileThresholdGb = 32.0d`
- `Thumbnail/Engines/ThumbnailEngineRouter.cs`
  - `ResolveForThumbnail(...)` は `IsUltraLargeMovie(...)` を `100 panel + large file` より先に評価する
- `400GB` は既定 `32GB` を超えるため、現行 source では `autogen` が正しい

## 5. 検証

- build
  - `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /nologo /verbosity:minimal`
  - 成功
- test
  - `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AutogenRegressionTests"`
  - 成功
  - `20` 件合格
- diff check
  - `git diff --check`
  - 成功

## 6. レビュー結果

- レビュー専任役
  - `findings なし`
- 調整役判断
  - 受け入れ

## 7. コミット

- docs 配布記録
  - `fd43be4`
  - `Q6a配布記録を追加`
- 本線 commit
  - `8064e7ff43149321991f97f62dfcb06719e5b411`
  - `超巨大動画router回帰テストを現仕様へ寄せる`

## 8. 残留

- `Thumbnail/Docs/Implementation Plan_通常キュー超巨大動画timeout実効化_2026-03-18.md` には、現行 router 契約とずれる旧記述が残る可能性がある
- 今回は `Q6a` を test 1 件に閉じるため、docs 本文の棚卸しは次レーンへ持ち越した
