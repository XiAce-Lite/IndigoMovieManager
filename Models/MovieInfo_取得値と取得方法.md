# MovieInfo 取得値と取得方法

## 対象
- 実装: `Models/MovieInfo.cs`
- クラス: `MovieInfo : MovieCore`
- コンストラクタ: `MovieInfo(string fileFullPath, bool noHash = false)`

## 処理フロー（要約）
1. 入力パスを生パス(`rawPath`)と正規化パス(`normalizedPath`)に分ける。
2. メタ情報を `FFMediaToolkit` 優先で取得し、失敗時は `OpenCvSharp` にフォールバックする。
3. 取得結果から `FPS` / `TotalFrames` / `MovieLength` を確定する。
4. `FileInfo` からサイズ・更新日時を取得する。
5. `MovieCore` の各プロパティへ値を設定する。
6. `noHash == false` の場合のみ CRC32 ハッシュを計算する。

## 取得・設定される主な値
| プロパティ | 取得方法 | 備考 |
|---|---|---|
| `MoviePath` | `rawPath` を代入 | setter 内で `MoviePathNormalized` も更新される |
| `MoviePathNormalized` | `MoviePath` setter 内の `NormalizeMoviePath` | 外部ライブラリ向け正規化パス |
| `MovieName` | `Path.GetFileNameWithoutExtension(rawPath)` | 拡張子なしファイル名 |
| `MovieSize` | `new FileInfo(rawPath).Length` | byte |
| `FPS` | `FFMediaToolkit` または `OpenCV` の値を正規化 | 不正値時は `30` |
| `TotalFrames` | `FFMediaToolkit` または `OpenCV` の値を正規化 | 不正値時は `0` |
| `MovieLength` | 秒を算出して `long` へキャスト | 秒未満は切り捨て |
| `LastDate` | `DateTime.Now` を秒単位に丸めて設定 | DB互換のため秒未満切り捨て |
| `RegistDate` | `DateTime.Now` を秒単位に丸めて設定 | `LastDate` と同値 |
| `FileDate` | `FileInfo.LastWriteTime` を秒単位に丸めて設定 | 秒未満切り捨て |
| `Hash` | `Tools.GetHashCRC32(rawPath)` | `noHash == true` の時は未設定 |
| `Tag` | `Tags` の別名プロパティ | 旧実装互換 |

## メタ情報取得の詳細

### 1. FFMediaToolkit 経路（第一候補）
- 初回のみ `EnsureFfMediaToolkitLoaded()` を実行し、以下を満たす場合に有効化する。
  - `AppContext.BaseDirectory/tools/ffmpeg-shared` が存在
  - 必須 DLL セットが存在
    - `avcodec*.dll`
    - `avformat*.dll`
    - `avutil*.dll`
    - `swscale*.dll`
    - `swresample*.dll`
- 読み取り処理:
  - `MediaFile.Open(inputPath, new MediaOptions { StreamsToLoad = MediaMode.Video })`
  - `fps = videoInfo.AvgFrameRate`
  - `totalFrames = videoInfo.NumberOfFrames ?? Truncate(videoInfo.Duration.TotalSeconds * fps)`
  - `durationSec = videoInfo.Duration.TotalSeconds`
- 失敗時は `DebugRuntimeLog` に記録し、OpenCV 経路へフォールバックする。

### 2. OpenCvSharp 経路（フォールバック）
- `using var capture = new VideoCapture(inputPath)`
- 取得値:
  - `totalFrames = capture.Get(FrameCount)`
  - `fps = capture.Get(Fps)`
  - `durationSec = totalFrames / fps`
- `capture.IsOpened() == false` や例外時は失敗扱い。

## 正規化ルール
- `NormalizeFps(double fps)`
  - 正の有限値のみ採用
  - それ以外は `DefaultFps(30)`
- `NormalizeTotalFrames(double totalFrames)`
  - 正の有限値のみ採用
  - `Math.Truncate` で整数化
  - それ以外は `0`
- `NormalizeDurationSec(double durationSec, double totalFrames, double fps)`
  - `durationSec` が正の有限値なら採用
  - そうでなければ `totalFrames / fps` を採用
  - どちらも無効なら `0`

## 取得しない（この実装では未設定の）値
以下は `MovieCore` に定義はあるが、`MovieInfo` コンストラクタでは設定していない。
- `Container`
- `Video`
- `Audio`
- `Extra`
- そのほかタグ系(`Title`, `Artist`, `Album` など)

## 注意点
- `FFMediaToolkit` は `MediaMode.Video` 前提で開いているため、動画ストリームが無い入力では FFMediaToolkit 経路が失敗しやすい。
- その場合は OpenCV フォールバック結果、または最終的な既定値（`FPS=30`, `TotalFrames=0`, `MovieLength=0`）になる可能性がある。
