# Implementation Plan 通常キュー超巨大動画timeout実効化 2026-03-18

最終更新日: 2026-03-22

変更概要:
- 通常キューの timeout 秒数を `IMM_THUMB_NORMAL_TIMEOUT_SEC` で上書きできるようにした
- 超巨大動画は通常キューでも `autogen` を維持し、秒位置だけ先頭 `300秒` 以内へ寄せる方針へ更新した
- env 指定の live テスト `NormalLaneTimeoutLiveTests` を追加し、`sango72GB.mkv` で 15 秒 timeout を確認した
- 既定の通常レーン timeout を `10秒` から `40秒` へ更新した

## 1. 背景

- 対象は通常キューから流れる超巨大動画であり、救済worker は対象外とする。
- 既存の通常キュー timeout は 10 秒固定だったため、検証用に 15 秒へ差し替える導線がなかった。
- 2026-03-22 時点で、通常動画でも余裕を持って試せるよう既定値は 40 秒へ見直した。
- 超巨大動画は終盤シークで貼り付きやすいケースがあり、既定 engine を崩さず秒位置だけ寄せる導線が必要だった。

## 2. 方針

1. 通常キュー timeout の秒数は環境変数で変更可能にする。
2. 通常動画の既定経路は維持し、超巨大動画も `autogen` のまま処理する。
3. 超巨大動画だけ、`autogen` の capture 秒を再生時間 `300秒` 以内へ再配置する。
4. timeout の検証導線は別途維持する。

## 3. 実装内容

### 3.1 通常キュー timeout 解決

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `ResolveThumbnailNormalLaneTimeout()` を追加
  - `IMM_THUMB_NORMAL_TIMEOUT_SEC` を読み、未指定・不正値は既定 40 秒へ戻す
  - timeout 例外メッセージも解決済み秒数を使う

### 3.2 超巨大動画 routing

- `Thumbnail/Engines/ThumbnailEngineRouter.cs`
  - `IMM_THUMB_ULTRA_LARGE_FILE_GB` を追加
  - 既定値は `32GB`
  - 非 manual の通常キューで、この閾値以上の動画も `autogen` を維持する
  - 進捗上の `Slow lane` 表示は `BigMovie` へ変更した

### 3.3 超巨大動画の capture 秒再配置

- `Thumbnail/Engines/FfmpegAutoGenThumbnailGenerationEngine.cs`
  - 超巨大動画かつ通常キュー時だけ、capture 秒を先頭 `300秒` 以内へ均等再配置する
  - manual や明示ヒント付き実行では既存秒位置を維持する
  - ログに `autogen ultra-large capture window` を残し、実際に使った秒位置を追えるようにする

### 3.4 テスト

- `Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs`
  - timeout 秒数解決の単体テストを追加
- `Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs`
  - panel=1 でも超巨大動画は `autogen` を選ぶ回帰テストへ更新
  - 超巨大動画の capture 秒が先頭 `300秒` へ寄る単体テストを追加
- `Tests/IndigoMovieManager_fork.Tests/NormalLaneTimeoutLiveTests.cs`
  - env 指定時だけ動く live テストを追加
- `Tests/IndigoMovieManager_fork.Tests/FfmpegOnePassThumbnailGenerationEngineTests.cs`
  - `TaskCanceledException` でも通るよう catch を緩めた

## 4. live 確認結果

- 入力:
  - `ローカル検証用の超巨大動画 (sango72GB.mkv)`
- 環境変数:
  - `IMM_THUMB_NORMAL_TIMEOUT_SEC=15`
  - `IMM_NORMAL_TIMEOUT_LIVE_INPUT=<上記パス>`
- 実行:
  - `dotnet test Tests\IndigoMovieManager_fork.Tests\IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~NormalLaneTimeoutLiveTests" --logger "console;verbosity=detailed"`
- 結果:
  - `elapsed_ms=15081`
  - `thumbnail normal lane timeout ... timeout_sec=15`

## 5. 影響

- 通常動画の既定 engine は従来どおり `autogen`
- 超巨大動画も engine 自体は変えず、秒位置だけ寄せるため通常テンポへの影響は限定的
- timeout 秒数を検証用途で差し替えても、既定運用は 40 秒を基準に維持される
