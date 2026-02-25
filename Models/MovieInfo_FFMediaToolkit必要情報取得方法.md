# MovieInfo 必要情報を FFMediaToolkit で取得する方法

## 対象
- `Models/MovieInfo.cs` で必要になる基本情報
  - `MovieLength`（秒）
  - `MovieSize`（byte）
  - `FPS`
  - `TotalFrames`

## 基本方針
- `MediaMode.AudioVideo` で開いて、先に `HasVideo` / `HasAudio` を確認する。
- 再生時間は `mediaFile.Info.Duration` を使う。
- ファイルサイズは `FileInfo(path).Length` を使う。
- `FPS` / `TotalFrames` は `hasVideo == true` のときだけ動画ストリームから取得する。

## 最小コード例
```csharp
using System.IO;
using FFMediaToolkit.Decoding;

var options = new MediaOptions { StreamsToLoad = MediaMode.AudioVideo };
using var mediaFile = MediaFile.Open(path, options);

bool hasVideo = mediaFile.HasVideo;
bool hasAudio = mediaFile.HasAudio;

double durationSec = mediaFile.Info.Duration.TotalSeconds;
long fileSize = new FileInfo(path).Length;
```

## MovieInfo向けの実用コード例
```csharp
using System;
using System.IO;
using System.Linq;
using FFMediaToolkit.Decoding;

double fps = 30;
double totalFrames = 0;

var options = new MediaOptions { StreamsToLoad = MediaMode.AudioVideo };
using var mediaFile = MediaFile.Open(path, options);

bool hasVideo = mediaFile.HasVideo && mediaFile.VideoStreams.Any();
bool hasAudio = mediaFile.HasAudio && mediaFile.AudioStreams.Any();

double durationSec = mediaFile.Info.Duration.TotalSeconds;
long fileSize = new FileInfo(path).Length;

if (hasVideo)
{
    var info = mediaFile.VideoStreams.First().Info;
    fps = info.AvgFrameRate;
    if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
    {
        fps = 30;
    }

    totalFrames = info.NumberOfFrames ?? Math.Truncate(durationSec * fps);
    if (totalFrames <= 0 || double.IsNaN(totalFrames) || double.IsInfinity(totalFrames))
    {
        totalFrames = 0;
    }
}

// hasAudio は「音声のみファイル判定」などに使える
bool isAudioOnly = hasAudio && !hasVideo;
long movieLength = (long)Math.Max(0, durationSec);
```

## 注意点
- `mediaFile.Video.Info` に直接アクセスすると、動画ストリームが無い入力で失敗する可能性がある。
- 音声のみファイルでも `Duration` と `MovieSize` は取得できる。
- `MovieLength` は既存実装に合わせて秒未満切り捨て（`long`）で扱う。
