# MovieInfo情報取得ベンチ (FFMediaToolkit vs Sinku.dll)

## 目的
- MovieInfo相当のメタ情報取得について、`FFMediaToolkit` と `Sinku.dll` の処理時間を比較する。

## 実装方針
- `Sinku.dll` は 32bit DLL のため、x86 ヘルパー (`IMM_Lab/SinkuMetaBenchRunner`) で直接呼び出し。
- メインベンチ (`IMM_Lab/MovieInfoSinkuBench`) は FFMediaToolkit 計測とヘルパー結果を統合して比較する。

## 入力と条件
- 入力(ASCII): `IMM_Lab/MovieInfoSinkuBenchOutput/meta_source.mp4`
- 入力(Emoji): `IMM_Lab/MovieInfoSinkuBenchOutput/📁入力😀/動画🎬メタ情報.mp4`
- 反復回数: 300
- 実行コマンド:
  - `dotnet run --project "c:\Users\%USERNAME%\source\repos\IMM_Lab\MovieInfoSinkuBench\MovieInfoSinkuBench.csproj"`

## 結果
| Scenario | Method | PerCallMs | DurationSec | Frames | FPS | 備考 |
|---|---|---:|---:|---:|---:|---|
| ASCII | Sinku.dll(Unicode) | 0.3422 | 24.0000 | 0 | 0.0000 | container/video/audio/extra を取得 |
| ASCII | FFMediaToolkit | 1.5088 | 24.0000 | 720 | 30.0000 | 動画ストリーム情報を取得 |
| Emoji | Sinku.dll(Unicode) | 0.3253 | 24.0000 | 0 | 0.0000 | 絵文字パスでも取得成功 |
| Emoji | FFMediaToolkit | 1.5358 | 24.0000 | 720 | 30.0000 | 絵文字パスでも取得成功 |

## 読み取り
- この計測条件では、呼び出しコストは `Sinku.dll` の方が速い。
- ただし取得項目は同一ではない。
  - `FFMediaToolkit`: FPS / FrameCount / Duration を取得
  - `Sinku.dll`: Container / Video / Audio / Extra / Playtime を取得
- 比較時は「必要なメタ情報がどちらで取れるか」を先に決めるのが安全。

## 生成物
- 統合ログ: `IMM_Lab/MovieInfoSinkuBenchOutput/movieinfo_sinku_bench_log.txt`
