# Implementation Plan 通常キュー超巨大動画timeout実効化 2026-03-18

最終更新日: 2026-03-18

変更概要:
- 通常キューの timeout 秒数を `IMM_THUMB_NORMAL_TIMEOUT_SEC` で上書きできるようにした
- 超巨大動画は通常キューでも `ffmpeg1pass` を先頭にし、process kill で timeout を実効化する方針を追加した
- env 指定の live テスト `NormalLaneTimeoutLiveTests` を追加し、`sango72GB.mkv` で 15 秒 timeout を確認した

## 1. 背景

- 対象は通常キューから流れる超巨大動画であり、救済worker は対象外とする。
- 既存の通常キュー timeout は 10 秒固定だったため、検証用に 15 秒へ差し替える導線がなかった。
- 既定 engine が `autogen` 固定のままだと、超巨大動画で timeout を掛けても native 側の貼り付きが見えにくい。

## 2. 方針

1. 通常キュー timeout の秒数は環境変数で変更可能にする。
2. 通常動画の既定経路は維持し、超巨大動画だけ `ffmpeg1pass` へ逃がす。
3. `ffmpeg1pass` の既存 cancel 対応を使い、15 秒 timeout の実効性を live で確認する。

## 3. 実装内容

### 3.1 通常キュー timeout 解決

- `Thumbnail/MainWindow.ThumbnailCreation.cs`
  - `ResolveThumbnailNormalLaneTimeout()` を追加
  - `IMM_THUMB_NORMAL_TIMEOUT_SEC` を読み、未指定・不正値は既定 10 秒へ戻す
  - timeout 例外メッセージも解決済み秒数を使う

### 3.2 超巨大動画 routing

- `Thumbnail/Engines/ThumbnailEngineRouter.cs`
  - `IMM_THUMB_ULTRA_LARGE_FILE_GB` を追加
  - 既定値は `32GB`
  - 非 manual の通常キューで、この閾値以上の動画は `ffmpeg1pass` を先頭にする
  - 既存の `panel>=10 && large file / long duration` 分岐も `ffmpeg1pass` へ戻した

### 3.3 テスト

- `Tests/IndigoMovieManager_fork.Tests/MissingThumbnailRescuePolicyTests.cs`
  - timeout 秒数解決の単体テストを追加
- `Tests/IndigoMovieManager_fork.Tests/AutogenRegressionTests.cs`
  - panel=1 でも超巨大動画は `ffmpeg1pass` を選ぶ回帰テストを追加
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
- 超巨大動画だけ `ffmpeg1pass` に寄るため、通常テンポへの影響は限定的
- timeout 秒数を検証用途で差し替えても、既定運用は 10 秒のまま維持される
