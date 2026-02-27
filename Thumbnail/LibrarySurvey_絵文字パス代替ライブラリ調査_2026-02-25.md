# 絵文字パス問題の代替ライブラリ調査（2026-02-25）

## 1. 背景
- 現状は OpenCV + ffmpeg プロセス呼び出しでサムネイル生成している。
- ただし「動画パスに絵文字を含むケース」で失敗が発生するため、ffmpeg 引数依存を減らす代替を調査する。

## 2. 結論（先に要点）
- **第一候補: FFMediaToolkit**
  - 理由: .NET から直接フレーム取得でき、CLI引数組み立てを回避できる。
  - WPF 連携サンプルがあり、現在の「指定秒のフレーム抽出」に寄せやすい。
  - 最小PoCで、**絵文字ディレクトリ + 絵文字ファイル名**の入力からJPEG生成に成功した。
- **第二候補: LibVLCSharp**
  - 理由: 継続開発中で実績も多い。サムネイル抽出サンプルも存在する。
  - ただし VLC 系の依存導入・配布サイズ・初期化周りが重め。
- **見送り候補: Vlc.DotNet / FFME.Windows**
  - Vlc.DotNet はアーカイブ済み。
  - FFME は主用途が再生 UI で、今回の「静止画抽出」には過剰。

## 3. 候補比較

### 3.1 FFMediaToolkit
- 概要
  - FFmpeg のネイティブDLLを内部利用する .NET ライブラリ。
  - API で時刻指定フレーム取得が可能。
- 良い点
  - プロセス起動と引数エスケープの問題を減らせる。
  - MIT ライセンス（ライブラリ本体）。
  - 最近も更新されている（README/Release更新あり）。
- 注意点
  - 内部は FFmpeg なので、根本のデコード互換は FFmpeg 依存。
  - ネイティブ DLL 配置（avcodec など）管理は必要。
  - `FFmpegLoader.FFmpegPath` 設定後に `FFmpegLoader.LoadFFmpeg()` を明示実行する必要がある。
  - CUDA等のHWアクセラレーションは専用プロパティではなく、`MediaOptions.DecoderOptions` でFFmpegオプションを渡して指定する。
  - DLLセットが不整合だと `Cannot load FFmpeg libraries ... Required FFmpeg version: 7.x (shared build)` で失敗する。
- 適合度
  - **高**（現行サムネイル処理に近い）

### 3.2 LibVLCSharp
- 概要
  - VideoLAN 公式系の .NET バインディング。
  - WPF 対応パッケージあり。
- 良い点
  - 継続開発・利用実績が大きい。
  - サンプルにサムネイル抽出系あり。
- 注意点
  - LGPL 系ライセンス考慮が必要。
  - 配布物・初期化・運用が FFMediaToolkit より重め。
  - サムネイル専用用途としては実装量が増える可能性。
- 適合度
  - **中**（堅いがやや重い）

### 3.3 Vlc.DotNet
- 概要
  - VLC ラッパーだが、リポジトリがアーカイブ済み。
- 判定
  - **非推奨**（新規採用は避ける）

### 3.4 FFME.Windows
- 概要
  - WPF の再生コントロール代替（FFmpegベース）。
- 判定
  - **用途ミスマッチ**（プレイヤーUI寄り）

## 4. 実装方針（最小リスク）
1. 既存 OpenCV 経路は維持。
2. 「絵文字パス検出時のみ」FFMediaToolkit 経路を優先する。
3. 失敗時のみ現行フォールバック（既存ロジック）へ戻す。
4. 問題なければ徐々に FFMediaToolkit 比率を上げる。

## 5. PoCで確認すべき項目
### 5.1 実施済み項目（2026-02-25）
- 実施プロジェクト: `C:\Users\%USERNAME%\source\repos\IMM_Lab\EmojiPathPoc`
- 実施結果:
  - ASCIIパス入力: 成功
  - 絵文字ディレクトリ + 絵文字ファイル入力: 成功
    - 入力例: `...\EmojiTest\📁入力😀\動画🎬テスト.mp4`
    - 出力例: `...\EmojiTest\📤出力😀\thumb😀.jpg`
- 判定:
  - **FFMediaToolkit経路で絵文字パスの単発フレーム抽出は成立**

### 5.2 速度ベンチ実測（2026-02-25）
- 実施プロジェクト: `C:\Users\%USERNAME%\source\repos\IMM_Lab\ThumbnailSpeedBench`
- 題材: ffmpeg `testsrc` で生成した 1080p 動画、12フレーム抽出
- ASCII入力結果（PerFrame）
  - OpenCvSharp: **30.92ms**
  - FFMediaToolkit: 46.48ms
  - ffmpeg CLI: 189.89ms
- 絵文字入力結果（PerFrame）
  - FFMediaToolkit: **20.48ms**（成功）
  - ffmpeg CLI: 266.81ms（成功）
  - OpenCvSharp: 失敗（入力動画を開けない）
- 判定:
  - ASCII限定なら OpenCvSharp が速い。
  - **絵文字パス含みで安定運用するなら FFMediaToolkit が最有力**。

### 5.3 速度ベンチ再計測（BG重負荷考慮のやり直し / 2026-02-25）
- 実施回数: 3回連続
- ログ: `C:\Users\%USERNAME%\source\repos\IMM_Lab\SpeedBenchOutput\rerun_20260225_v2`
- 3回平均（OpenCvSharp / FFMediaToolkit）
  - ASCII
    - OpenCvSharp: `487.15ms`（`40.59ms/frame`）
    - FFMediaToolkit: `502.07ms`（`41.84ms/frame`）
  - Emoji
    - FFMediaToolkit: `247.99ms`（`20.67ms/frame`）
    - OpenCvSharp: 3/3回失敗（動画オープン失敗）
- 再判定:
  - ASCIIでは両者ほぼ同等（OpenCvSharpがわずかに速い）。
  - Emojiでは FFMediaToolkit が安定成功、OpenCvSharp は連続失敗。
  - **絵文字パス要件込みの採用判断は FFMediaToolkit 優先で妥当**。

### 5.4 FFMediaToolkit CUDAオプション比較（2026-02-25）
- 実施方法:
  - `MediaOptions.DecoderOptions` に以下を設定したプロファイルを追加測定
    - `hwaccel=cuda`
    - `hwaccel_output_format=cuda`
    - `hwaccel_device=0`
- 実測結果（PerFrame）
  - ASCII
    - FFMediaToolkit(cuda): **17.56ms**
    - FFMediaToolkit(通常): 41.11ms
  - Emoji
    - FFMediaToolkit(cuda): **17.90ms**
    - FFMediaToolkit(通常): 20.78ms
- 判定:
  - 今回環境では CUDA オプション指定プロファイルが高速だった。
  - ただし、これは「オプション指定時の実測」であり、常にGPU使用が保証される意味ではない。
  - 本番導入時は GPU 使用状況（ドライバ/デコーダ適用可否）を別途ログまたはモニタで確認する。

### 5.5 未実施項目（次フェーズ）
- 絵文字を含む以下のケースでフレーム取得できるか。
  - サロゲートペア複数（例: 家族絵文字）
- 4K/H.265/可変フレームレートでの安定性。
- 連続ジョブ時のメモリリーク・ハンドルリーク。
- 既存サムネイル品質（切り出し時刻・縦横比・リサイズ）の互換性。

## 6. 導入に必要な DLL / 配置要件
- 重要: `ffmpeg.exe` 単体では不足。**FFmpeg 7.x shared build の DLL 一式**が必要。
- 最低限必要なDLL（FFMediaToolkit実行上）
  - `avcodec*.dll`
  - `avformat*.dll`
  - `avutil*.dll`
  - `swscale*.dll`
  - `swresample*.dll`
- 今回成功したDLLセット（7.1 shared, LGPL）
  - `avcodec-61.dll`
  - `avdevice-61.dll`
  - `avfilter-10.dll`
  - `avformat-61.dll`
  - `avutil-59.dll`
  - `swresample-5.dll`
  - `swscale-8.dll`
- DLL配置先の実績例
  - `C:\Users\{USERNAME}\source\repos\IMM_Lab\ffmpeg_shared_7_1\ffmpeg-n7.1-latest-win64-lgpl-shared-7.1\bin`

## 7. 補足
- ffmpeg CLI の失敗は「引数」だけでなく、ビルド差分・入力プロトコル・パス正規化手順でも起こる。
- そのため、代替導入前に最小PoCで事実確認してから本実装に進むのが安全。
