# AI向け 作業指示 Codex Q6a RouterExpectationDrift解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `AutogenRegressionTests.Router_100枚かつ400Gb超ならFfmpegOnePassを先頭にする` が、現行 router 契約と食い違って失敗している
- `workthree` 本線の方針と thumbnail docs を正本にし、source を戻さず test 期待値と記録を再整合する

## 1. 目的

- failing test 1 件を、現行 source 契約に沿って解消する
- `ThumbnailEngineRouter` を `ffmpeg1pass` 優先へ戻さず、超巨大動画は `autogen` 維持の方針を守る

## 2. 対象

- `Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs`
- 必要なら参照だけ
  - `Thumbnail/Engines/ThumbnailEngineRouter.cs`
  - `Thumbnail/ThumbnailEnvConfig.cs`
  - `Thumbnail/Docs/Implementation Plan_通常キュー超巨大動画timeout実効化_2026-03-18.md`
  - `Thumbnail/Docs/調査結果_低速Thread現状まとめ_2026-03-18.md`

## 3. 現状の根拠

- `ThumbnailEnvConfig.DefaultUltraLargeFileThresholdGb` は既定 `32GB`
- `ThumbnailEngineRouter.ResolveForThumbnail(...)` は
  1. manual なら `autogen`
  2. 超巨大動画なら `autogen`
  3. その後で `PanelCount >= 100 && IsLargeFile(...)` なら `ffmpeg1pass`
  の順で判定している
- `400GB` は既定 `32GB` を超えるため、現行 source では `autogen` が正しい
- 関連 docs でも「超巨大動画は engine 自体は変えず `autogen` を維持する」と明記されている

## 4. 守ること

1. source を test に合わせて戻さない
2. `ThumbnailEngineRouter` の分岐順や既定閾値を変えない
3. 変更は `Q6a` の 1 件に閉じる
4. unrelated な thumbnail engine / queue / watcher 差分を混ぜない

## 5. 着地イメージ

- failing test 名や期待値を、現行 router 契約へ寄せる
- 必要なら test 名は
  - `100枚かつ400GB超でも超巨大動画はAutogenを維持する`
  のように、期待の意味が誤解されない形へ直す
- 期待値は `autogen`
- 可能なら同 test 内で「panel 100 でも ultra-large 判定が優先する」意図が読めるようにする

## 6. 検証

- `MSBuild.exe Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj /restore /t:Build /p:Configuration=Debug /p:Platform=x64 /nologo /verbosity:minimal`
- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AutogenRegressionTests"`

## 7. 禁止

- `ThumbnailEngineRouter` の順序変更
- `DefaultUltraLargeFileThresholdGb` の変更
- test 都合の env var 依存導入
- `manual` / `ultra-large` / `100 panel` 契約を曖昧化する命名
