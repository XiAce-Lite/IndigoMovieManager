# Implementation Plan master向けPR 保存場所名称整合 2026-03-27

## 目的
- `IndigoMovieManager_fork_workthree` を、`IndigoMovieManager-master` へ出す PR 用ブランチとして整える。
- `workthree` / `fork` 固有の保存場所名、識別子、テスト名を、本家前提の名前へ戻す。
- 速度改善や機能差分は維持しつつ、「本家へ入れる時に違和感になる fork 固有名」だけを先に外す。

## この計画の前提
- 比較元は `C:\Users\na6ce\source\repos\IndigoMovieManager-master\IndigoMovieManager-master` とする。
- 作業対象は `C:\Users\na6ce\source\repos\IndigoMovieManager_fork_workthree` とする。
- 今回のテーマは「保存場所、名称、PR 向け整合」であり、性能改善ロジックそのものの採否は別計画とする。
- `workthree` で同時起動分離のために入れた識別子のうち、本家 PR で不要なものを戻す。

## ゴール
- 実行時の既定保存先名が本家前提になる。
- アプリ識別子、launch profile、テスト識別子が `fork` / `workthree` 名を引きずらない。
- 最低限の README / 設定 / テストが本家向け名称に揃う。
- PR 説明で「今回戻したのは名称・保存場所であり、機能差分ではない」と明確に言える。

## 非ゴール
- `workthree` の高速化ロジックを丸ごと upstream へ入れることの妥当性判断
- docs 全量の rename
- GitHub Actions や配布資料の全面掃除
- `WhiteBrowser` 互換仕様の変更

## 現状の主なズレ

### 1. LocalAppData ルートが workthree 専用
- `src\IndigoMovieManager.Thumbnail.Runtime\AppLocalDataPaths.cs`
  - `RootFolderName = "IndigoMovieManager_fork_workthree"`
- 本家へ寄せるなら、既定保存先は `IndigoMovieManager` に戻す必要がある。

### 2. 設定解決先アセンブリ名に workthree 固有名が残る
- `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailLaneClassifier.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailParallelController.cs`
- `Thumbnail\ThumbnailEnvConfig.cs`
- ここが `IndigoMovieManager_fork_workthree` を参照したままだと、設定解決や実行時の識別が本家名とズレる。

### 3. trace / rescue worker 系の識別子に workthree 固有名が残る
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailMovieTraceLog.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailRescueTraceLog.cs`
- `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`
- ローカル mutex 名、イベント名、パス名などが本家 PR としてはノイズになる。

### 4. テストアセンブリ名が fork 名のまま
- `Properties\InternalsVisibleTo.Tests.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\Properties\InternalsVisibleTo.Tests.cs`
- `src\IndigoMovieManager.Thumbnail.RescueWorker\Properties\InternalsVisibleTo.Tests.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\Properties\InternalsVisibleTo.cs`
- `Tests\IndigoMovieManager.Tests\*.cs`
  - namespace が `IndigoMovieManager_fork.Tests`
  - temp path や fixture 名に `IndigoMovieManager_fork_tests` が残る

### 5. launch / README に fork/workthree 名が残る
- `Properties\launchSettings.json`
  - profile 名が `IndigoMovieManager_fork_workthree`
- `README.md`
  - タイトルが `IndigoMovieManager_fork`

### 6. docs / workflow に fork 名が大量に残る
- `.github\workflows\*.yml`
- `Docs\**\*.md`
- ただしここは PR の目的に対して広すぎるため、今回は優先順位を下げる。

## 方針

### 方針1: まず実行時に効く識別子を戻す
- LocalAppData ルート
- 設定解決先アセンブリ名
- trace / rescue worker の識別子
- launch profile

### 方針2: 次にビルド・テスト系の識別子を戻す
- `InternalsVisibleTo`
- テスト namespace
- テスト内の temp path / fixture 名

### 方針3: docs / workflow は「最小限」に留める
- README の先頭だけは本家前提に戻す
- workflow や過去 docs は、PR 目的に直接効くものだけ触る
- 過去履歴説明書まで全面 rename しない

## 実装ステップ

### Phase 1. ランタイム保存先名の整合
対象:
- `src\IndigoMovieManager.Thumbnail.Runtime\AppLocalDataPaths.cs`
- `src\IndigoMovieManager.Thumbnail.RescueWorker\RescueWorkerApplication.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailMovieTraceLog.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\ThumbnailRescueTraceLog.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailLaneClassifier.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\ThumbnailParallelController.cs`
- `Thumbnail\ThumbnailEnvConfig.cs`

やること:
1. `IndigoMovieManager_fork_workthree` を本家前提名へ戻す
2. 設定解決先文字列を `IndigoMovieManager` に統一する
3. trace / rescue worker のローカル名も本家前提名へ戻す

完了条件:
- `%LOCALAPPDATA%\IndigoMovieManager\...` 前提で動く
- settings 解決が `IndigoMovieManager` 名で一貫する

### Phase 2. テスト識別子の整合
対象:
- `Properties\InternalsVisibleTo.Tests.cs`
- `src\IndigoMovieManager.Thumbnail.Engine\Properties\InternalsVisibleTo.Tests.cs`
- `src\IndigoMovieManager.Thumbnail.RescueWorker\Properties\InternalsVisibleTo.Tests.cs`
- `src\IndigoMovieManager.Thumbnail.Queue\Properties\InternalsVisibleTo.cs`
- `Tests\IndigoMovieManager.Tests\*.cs`
- `Thumbnail\Test\*.cs`

やること:
1. `InternalsVisibleTo("IndigoMovieManager_fork.Tests")` を本家前提名へ戻す
2. テスト namespace を `IndigoMovieManager.Tests` へ揃える
3. temp path / bench 名 / isolated live 名に残る `fork` 名を必要最小限で戻す
4. 文字列比較系テストがある場合は期待値も追従する

完了条件:
- テスト project が本家前提の識別子で通る
- `fork` 名がテスト assembly 名に残らない

### Phase 3. 入口名の整合
対象:
- `Properties\launchSettings.json`
- `README.md`

やること:
1. launch profile 名を `IndigoMovieManager` に戻す
2. README タイトルと冒頭説明を本家前提へ戻す

完了条件:
- Visual Studio / `dotnet run` 時に fork 名が前面へ出ない
- リポジトリ入口の見た目が本家 PR として自然になる

### Phase 4. PR ノイズ削減
対象候補:
- `.github\workflows\*.yml`
- `Docs\**\*.md`

やること:
1. CI でビルドパスや artifact 名に fork 固有名が残っていて PR の説明を邪魔するなら最小限だけ修正する
2. 過去の作業記録 docs は原則そのまま残す
3. ただし README と今回の計画書から見て誤解を招くものだけ追加修正する

完了条件:
- PR 本文で「docs 全量 rename はしていない」と説明できる
- それでも利用者が最初に見る導線は本家前提になる

## 推奨コミット分割
1. `保存先と設定解決先の名称を本家前提へ戻す`
2. `テスト識別子を本家前提へ揃える`
3. `launchSettings と README の名称を整える`
4. 必要なら `CI と最小ドキュメントの名称ノイズを削る`

## リスク

### リスク1: user.config の読取先が変わる
- `IndigoMovieManager_fork_workthree` の LocalAppData を見ていた既存環境では、設定の継続読込が切れる。
- ただし本家 PR 向けとしては正しい挙動である。

### リスク2: trace / mutex 名の変更で既存手動確認手順がズレる
- rescue worker 実動画確認系の手順書が一時的に古くなる。
- 今回は README と計画書で「本家 PR 用の整合」と明記して吸収する。

### リスク3: テストの namespace rename が広い
- 置換対象が多く、architecture test や文字列比較に波及する。
- 一括置換後に `rg "IndigoMovieManager_fork.Tests"` で残件ゼロ確認を必須にする。

### リスク4: CI/workflow まで広げると PR が重くなる
- 今回の本筋は runtime と test の識別子整合である。
- workflow は必要最小限に留める。

## 検証
1. `rg -n "IndigoMovieManager_fork_workthree|IndigoMovieManager_fork.Tests|IndigoMovieManager_fork_tests" .`
   - 対象を絞った残件確認に使う
2. `dotnet build IndigoMovieManager.sln -c Debug -p:Platform=x64`
3. `dotnet test Tests\IndigoMovieManager.Tests\IndigoMovieManager.Tests.csproj -c Debug -p:Platform=x64 --no-build`
4. 必要なら実行して `%LOCALAPPDATA%\IndigoMovieManager\` 側へ保存されることを確認する

## PR ブランチ運用案
- ブランチ名候補:
  - `codex/pr-align-upstream-storage-and-names`
  - `codex/pr-upstream-identifier-normalize`
- PR では「保存場所と識別子を本家前提へ戻した」ことを主題にし、性能改善そのものとは切り分ける。

## 受け入れ条件
- 実行時保存先の既定ルートが `IndigoMovieManager` へ戻る
- `launchSettings`、`InternalsVisibleTo`、テスト namespace が本家前提へ揃う
- `fork_workthree` 固有名が runtime code に残らない
- README 先頭で本家 PR として不自然な表現が消える

## メモ
- 2026-03-27 時点では `IndigoMovieManager.csproj` 自体の `AssemblyName` / `Product` / `Company` は既に `IndigoMovieManager` である。
- つまり今回は「project 名を変える」よりも、「周辺に残った fork/workthree 名を剥がす」作業が中心になる。
