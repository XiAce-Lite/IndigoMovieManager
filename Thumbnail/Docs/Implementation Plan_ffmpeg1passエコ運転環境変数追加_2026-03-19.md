# Implementation Plan: ffmpeg1pass エコ運転環境変数追加（2026-03-19）

## 1. 目的

- `ffmpeg1pass` を完全な CPU 使用率制限ではなく、現実的な「軽く回す」方向へ寄せる。
- 既定挙動は変えず、必要時だけ環境変数で `ffmpeg.exe` の内部並列数と Windows 優先度を落とせるようにする。
- `workthree` 本線のテンポを壊さないよう、通常運用はノータッチで維持する。

## 2. 今回の変更

- `Thumbnail\Engines\FfmpegOnePassThumbnailGenerationEngine.cs`
  - `IMM_THUMB_FFMPEG1PASS_THREADS`
    - 1 以上の整数時だけ `-threads N` を付与する。
    - 未指定・不正値は既定の ffmpeg 挙動へ戻す。
  - `IMM_THUMB_FFMPEG1PASS_PRIORITY`
    - `idle`
    - `below_normal` / `belownormal` / `low`
    - `normal`
    - `above_normal` / `abovenormal`
    - `high`
    を受け付け、`process.Start()` 後に `PriorityClass` を適用する。
  - 優先度変更に失敗してもサムネ生成自体は止めない。

## 3. 使い方

PowerShell 例:

```powershell
$env:IMM_THUMB_FFMPEG1PASS_THREADS = "1"
$env:IMM_THUMB_FFMPEG1PASS_PRIORITY = "idle"
```

少しだけ軽くする例:

```powershell
$env:IMM_THUMB_FFMPEG1PASS_THREADS = "2"
$env:IMM_THUMB_FFMPEG1PASS_PRIORITY = "below_normal"
```

## 4. 非対応

- `CPU 使用率を 30% まで` のような厳密な上限制御は今回の対象外。
- そこまでやると ffmpeg 単体より、OS ジョブ制御や外部レート制御の話になるため、まずは安全で説明しやすい `threads + priority` に留める。

## 5. テスト

- `Tests\IndigoMovieManager_fork.Tests\FfmpegOnePassThumbnailGenerationEngineTests.cs`
  - thread 数の解決
  - priority 文字列の解決
  - `-threads` 引数付与
  - 既存 cancel 回帰
