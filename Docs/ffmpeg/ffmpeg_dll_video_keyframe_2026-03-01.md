# FFmpeg DLL による動画情報・キーフレーム取得 🔥

## 1. ライブラリ構成

FFmpeg は単一のバイナリじゃなくて、役割別にモジュールが分かれてるよ！

| モジュール | やること |
|:--|:--|
| **libavformat** | コンテナ（MP4/MKV/AVI等）の読み書き・ストリーム検出・メタデータ解析 |
| **libavcodec** | コーデック（H.264/HEVC/AV1等）のエンコード・デコード |
| **libavutil** | 時間計算・辞書型・数学関数などユーティリティ |
| **libswscale** | 解像度変更・ピクセルフォーマット変換（YUV→RGB等） |
| **libswresample** | 音声リサンプリング・チャンネルミキシング |

### Windows 向け DLL の入手先

| プロバイダ | URL | 備考 |
|:--|:--|:--|
| **gyan.dev** | https://www.gyan.dev/ffmpeg/builds/ | Essentials（Win7+）/ Full（Win10+ SSE4.1） |
| **BtbN** | https://github.com/BtbN/FFmpeg-Builds | GitHub Actions ビルド |

> ⚠️ DLL として使うなら「**Shared**」パッケージを取得すること！（`ffmpeg-release-full-shared.7z` 等）

---

## 2. .NET/C# からの利用（FFmpeg.AutoGen）

FFmpeg の C API を C# から叩くには **FFmpeg.AutoGen**（NuGet）を使う。unsafe バインディングを自動生成してくれるやつだね！

### DLL ロードパスの指定

```csharp
// アプリ起動時に一発設定 💡
ffmpeg.RootPath = @"C:\path\to\ffmpeg-shared";
```

### 罠ポイント 🪤

| 罠 | 内容 |
|:--|:--|
| **アーキテクチャ不一致** | 64bit プロセスに 32bit DLL → `BadImageFormatException` で爆死 |
| **DLL 見つからない** | `RootPath` 未設定 → `DllNotFoundException` |
| **バージョン不一致** | AutoGen のバージョンと DLL のメジャーバージョンは揃えること |

---

## 3. コンテナとコーデックの対応表

| コンテナ | 特徴 | 主な映像コーデック |
|:--|:--|:--|
| **MP4** | Web・モバイルで最も普及。ストリーミング対応 | H.264, HEVC, AV1, VP9 |
| **MKV** | 何でも入る万能コンテナ。複数音声・字幕OK | H.264, HEVC, AV1, VP9, VP8 |
| **WebM** | MKV のサブセット。ブラウザ向け | VP8, VP9, AV1 |
| **TS** | 放送・HLS 向け。パケットロスに強い | H.264, MPEG-2 |
| **AVI** | レガシー。VFW ベース | ほぼ何でも（制限あり） |
| **FLV** | Flash 時代の遺産。まだ残ってる | H.264, VP6 |
| **MXF** | 放送業務用。ProRes 等 | ProRes, DNxHD, MPEG-2 |

---

## 4. 動画情報の取得フロー

```
avformat_alloc_context()
    ↓
avformat_open_input()       ← ヘッダ解析・コンテナ種類の判定
    ↓
avformat_find_stream_info() ← パケットを試し読みしてストリーム情報を確定 ✨
    ↓
AVFormatContext.streams[]   ← ストリーム配列をループ
    ↓
AVStream.codecpar           ← コーデックパラメータ取得
```

### 取得できる主な情報

| プロパティ | 説明 |
|:--|:--|
| `width` / `height` | 解像度（ピクセル） |
| `avg_frame_rate` | フレームレート（AVRational = 分数形式。29.97fps → 30000/1001） |
| `codec_id` | コーデック種別（`AV_CODEC_ID_H264` 等） |
| `pix_fmt` | ピクセルフォーマット（`AV_PIX_FMT_YUV420P` が多い） |
| `duration` | 再生時間 |
| `metadata` | タグ情報（タイトル・エンコーダ等）→ `av_dict_get()` で取得 |

---

## 5. デコードパイプライン（Send/Receive API）

現在の標準は **非同期 Send/Receive モデル**。旧 `avcodec_decode_video2` は非推奨！

```
av_read_frame()             ← コンテナからパケット(AVPacket)を1個読む
    ↓
avcodec_send_packet()       ← デコーダにパケットを投入
    ↓
avcodec_receive_frame()     ← デコード済みフレーム(AVFrame)を取り出す
    ↓                         ※ AVERROR(EAGAIN) になるまでループ
（次のパケットへ）
```

> 💡 1パケット → 複数フレームになることも、複数パケット → 1フレームになることもある。  
> B フレーム（双方向予測）があるから入出力は 1:1 じゃないよ！

---

## 6. キーフレーム（I-frame）の判定方法

GOP 構造: **I → P → B → P → B → ... → I（次のGOP）**

### 方法①: パケットレベル（高速・デコード不要）

```c
if (pkt.flags & AV_PKT_FLAG_KEY) {
    // キーフレームのパケットだ！
}
```

> ⚠️ オープン GOP や一部エンコーダ設定では、I フレームなのにフラグが立ってないケースがある

### 方法②: フレームレベル（正確・デコード必要）

```c
if (frame->key_frame == 1) { /* キーフレーム */ }
// さらに厳密に：
if (frame->pict_type == AV_PICTURE_TYPE_I) { /* 真の I フレーム */ }
```

> ✅ コンテナのフラグに頼らず、コーデック層で判定するから確実！

---

## 7. 高速シーク（av_seek_frame）

ファイル先頭から全部デコードしてたら遅すぎる！指定時刻にジャンプするにはこれ 👇

```c
av_seek_frame(fmt_ctx, stream_index, timestamp, AVSEEK_FLAG_BACKWARD);
avcodec_flush_buffers(codec_ctx);  // ← これ絶対忘れるな！！🔥
```

| ステップ | やること |
|:--|:--|
| `av_seek_frame()` | 指定タイムスタンプの直前キーフレームへジャンプ |
| `avcodec_flush_buffers()` | **必須**。デコーダ内部の古いデータをクリア |
| デコード再開 | ここから Send/Receive ループを回す |

> ⚠️ flush を忘れると **ブロックノイズまみれ** の壊れたフレームが出るよ！

---

## 8. フレーム → 画像ファイル保存

### 方法A: libswscale + 外部ライブラリ（OpenCV 等）

```
AVFrame (YUV420P)
    ↓ sws_scale()
BGR24 データ
    ↓ cv::Mat でラップ
cv::imwrite("output.jpg", img)
```

### 方法B: FFmpeg 内蔵エンコーダで直接 JPEG 化

```
AVFrame (YUV420P)
    ↓ （必要なら sws_scale で YUVJ420P に変換）
avcodec_send_frame()  → AV_CODEC_ID_MJPEG エンコーダ
    ↓
avcodec_receive_packet()
    ↓
fwrite() でバイナリ書き出し → .jpg 完成！
```

---

## 9. マルチスレッド設定

デフォルトはシングルスレッドで遅い！`avcodec_open2()` の前に設定する 👇

```c
codec_ctx->thread_count = 0;  // OS に任せて最適なスレッド数を自動決定

if (codec->capabilities & AV_CODEC_CAP_FRAME_THREADS)
    codec_ctx->thread_type = FF_THREAD_FRAME;   // フレーム並列（高スループット、レイテンシ増）
else if (codec->capabilities & AV_CODEC_CAP_SLICE_THREADS)
    codec_ctx->thread_type = FF_THREAD_SLICE;    // スライス並列（低レイテンシ）
```

---

## 9.5. HW アクセラレーション（Windows 専用）🚀

GPU でデコードすると CPU 負荷が激減する！サムネイル抽出でも爆速になるよ！

### Windows で使える HW デコーダ一覧

| 方式 | GPU ベンダー | CLI オプション | DLL の `AVHWDeviceType` | ピクセルフォーマット |
|:--|:--|:--|:--|:--|
| **DXVA2** | Intel / NVIDIA / AMD | `-hwaccel dxva2` | `AV_HWDEVICE_TYPE_DXVA2` | `AV_PIX_FMT_DXVA2_VLD` |
| **D3D11VA** | Intel / NVIDIA / AMD | `-hwaccel d3d11va` | `AV_HWDEVICE_TYPE_D3D11VA` | `AV_PIX_FMT_D3D11` |
| **CUDA** | NVIDIA のみ | `-hwaccel cuda` | `AV_HWDEVICE_TYPE_CUDA` | `AV_PIX_FMT_CUDA` |
| **QSV** | Intel のみ | `-hwaccel qsv` | `AV_HWDEVICE_TYPE_QSV` | `AV_PIX_FMT_QSV` |

> 💡 **おすすめ**: D3D11VA が一番汎用的（Intel/NVIDIA/AMD 全対応）！  
> NVIDIA 専用なら CUDA が最速。Intel 内蔵 GPU なら QSV。

### CLI → DLL API 対応表

CLIのオプションがDLL APIではどうなるかの対応だよ！

| CLI オプション | DLL API での対応 |
|:--|:--|
| `-hwaccel dxva2` | `av_hwdevice_ctx_create(&hw_ctx, AV_HWDEVICE_TYPE_DXVA2, ...)` |
| `-hwaccel d3d11va` | `av_hwdevice_ctx_create(&hw_ctx, AV_HWDEVICE_TYPE_D3D11VA, ...)` |
| `-hwaccel cuda` | `av_hwdevice_ctx_create(&hw_ctx, AV_HWDEVICE_TYPE_CUDA, ...)` |
| `-hwaccel_device 0` | `av_hwdevice_ctx_create()` の第3引数にデバイス番号を文字列で渡す（`"0"`） |
| `-hwaccel_output_format d3d11` | `get_format` コールバックで `AV_PIX_FMT_D3D11` を返す |
| `hwdownload,format=nv12` | `av_hwframe_transfer_data()` で GPU→CPU にフレーム転送 |

### DLL API での HW デコード実装フロー

```
① HW デバイスコンテキスト作成
   av_hwdevice_ctx_create(&hw_device_ctx, AV_HWDEVICE_TYPE_D3D11VA, "0", NULL, 0)
       ↓
② コーデックコンテキストに紐付け
   codec_ctx->hw_device_ctx = av_buffer_ref(hw_device_ctx)
       ↓
③ get_format コールバック設定
   codec_ctx->get_format = my_get_format   ← HW ピクセルフォーマットを選択
       ↓
④ avcodec_open2() でデコーダオープン
       ↓
⑤ 通常通り Send/Receive ループでデコード
   → frame->format が AV_PIX_FMT_D3D11 等（GPU上のデータ）
       ↓
⑥ GPU → CPU に転送（画像保存したい場合）
   av_hwframe_transfer_data(sw_frame, hw_frame, 0)
   → sw_frame->format が AV_PIX_FMT_NV12 になる
       ↓
⑦ NV12 → BGR24 等に変換して保存
   sws_scale() → imwrite() or MJPEG エンコード
```

### C言語での実装例（D3D11VA）

```c
// ① HW デバイス作成
AVBufferRef *hw_device_ctx = NULL;
av_hwdevice_ctx_create(&hw_device_ctx, AV_HWDEVICE_TYPE_D3D11VA,
                        "0",    // デバイス番号（GPU 0番）
                        NULL, 0);

// ② デコーダに紐付け
codec_ctx->hw_device_ctx = av_buffer_ref(hw_device_ctx);

// ③ get_format コールバック（HW フォーマットを選ぶ）
static enum AVPixelFormat my_get_format(
    AVCodecContext *ctx, const enum AVPixelFormat *pix_fmts)
{
    for (const enum AVPixelFormat *p = pix_fmts; *p != -1; p++) {
        if (*p == AV_PIX_FMT_D3D11)
            return *p;  // D3D11VA を選択！
    }
    return AV_PIX_FMT_NONE;  // HW 非対応ならフォールバック
}
codec_ctx->get_format = my_get_format;

// ④ デコーダオープン
avcodec_open2(codec_ctx, codec, NULL);

// ⑤ デコードループ（通常と同じ！）
while (av_read_frame(fmt_ctx, pkt) >= 0) {
    avcodec_send_packet(codec_ctx, pkt);
    while (avcodec_receive_frame(codec_ctx, hw_frame) >= 0) {

        // ⑥ GPU → CPU 転送
        AVFrame *sw_frame = av_frame_alloc();
        av_hwframe_transfer_data(sw_frame, hw_frame, 0);
        // sw_frame->format == AV_PIX_FMT_NV12 🎉

        // ⑦ ここから sws_scale で BGR 変換 → JPEG 保存
        // ...

        av_frame_free(&sw_frame);
        av_frame_unref(hw_frame);
    }
    av_packet_unref(pkt);
}

// 後片付け
av_buffer_unref(&hw_device_ctx);
```

### C#（FFmpeg.AutoGen）での実装例

```csharp
// ① HW デバイス作成
AVBufferRef* hwDeviceCtx = null;
ffmpeg.av_hwdevice_ctx_create(&hwDeviceCtx,
    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, "0", null, 0);

// ② デコーダに紐付け
codecCtx->hw_device_ctx = ffmpeg.av_buffer_ref(hwDeviceCtx);

// ③ get_format は unsafe delegate で設定
// ④ avcodec_open2 でオープン

// ⑤⑥ デコード後に GPU→CPU 転送
AVFrame* swFrame = ffmpeg.av_frame_alloc();
ffmpeg.av_hwframe_transfer_data(swFrame, hwFrame, 0);
// swFrame->format == AV_PIX_FMT_NV12

// ⑦ sws_scale で RGB 変換 → Bitmap 化 or JPEG 保存
```

### HW デコードのデータフロー図

```
┌─────────────┐    avcodec_send_packet    ┌──────────────┐
│  AVPacket   │ ──────────────────────── → │   HW デコーダ  │
│ (圧縮データ)  │                           │  (GPU 上で処理) │
└─────────────┘                           └──────┬───────┘
                                                 │
                                    avcodec_receive_frame
                                                 │
                                                 ▼
                                    ┌──────────────────┐
                                    │  AVFrame (GPU)    │
                                    │  format=D3D11     │
                                    │  data=ID3D11Tex2D │
                                    └────────┬─────────┘
                                             │
                              av_hwframe_transfer_data
                                             │
                                             ▼
                                    ┌──────────────────┐
                                    │  AVFrame (CPU)    │
                                    │  format=NV12      │
                                    │  data=メモリ上     │
                                    └────────┬─────────┘
                                             │
                                        sws_scale
                                             │
                                             ▼
                                    ┌──────────────────┐
                                    │  BGR24 / RGB24    │
                                    │  → JPEG 保存      │
                                    └──────────────────┘
```

### 各 HW 方式の特徴比較 ⚡

| 方式 | メリット | デメリット | サムネイル用途 |
|:--|:--|:--|:--|
| **DXVA2** | Win7+ で動く。レガシー互換 | 古い API。DirectX 9 ベース | ○ 安定 |
| **D3D11VA** | Win10+。全 GPU ベンダー対応。最も汎用的 | DXVA2 より若干複雑 | ◎ おすすめ |
| **CUDA** | NVIDIA 最速。NVDEC 直結。大量データに強い | NVIDIA GPU 必須。PCIe 転送コストあり | ◎ バッチ処理向き |
| **QSV** | CPU ダイ内 iGPU → **PCIe 不要！** 小データなら CUDA より速い | Intel CPU/GPU 必須 | ◎ サムネ1枚抽出に最適 |

> 💡 **サムネイル1枚抽出のような小データ処理では QSV が最速になりうる！**  
> CUDA は PCIe バス往復の転送コストがあるけど、QSV は CPU ダイ内の共有メモリで完結するから転送オーバーヘッドがほぼゼロ 🔥

### サムネイル抽出での使い方 💡

サムネイル抽出に HW デコードを使う場合、ポイントは3つ：

1. **シーク → デコード → 1枚取得 → 終了** のパターンでは HW 初期化コストが目立つ
   - 連続でバッチ処理する場合は HW デバイスを使い回すと効果的 🔥
2. **`av_hwframe_transfer_data()`** で GPU→CPU 転送は必須（画像保存するから）
3. **フォールバック**を必ず実装する
   - HW 非対応コーデック（一部 FLV, 古い AVI 等）→ ソフトウェアデコードに切り替え

```c
// フォールバック付き get_format
static enum AVPixelFormat my_get_format(
    AVCodecContext *ctx, const enum AVPixelFormat *pix_fmts)
{
    for (const enum AVPixelFormat *p = pix_fmts; *p != -1; p++) {
        if (*p == AV_PIX_FMT_D3D11)
            return *p;  // HW デコード OK！
    }
    // HW 非対応 → ソフトウェアデコードにフォールバック
    return pix_fmts[0];
}
```

### メモリ管理の追加ポイント 🧠

| 確保 | 解放 |
|:--|:--|
| `av_hwdevice_ctx_create()` | `av_buffer_unref(&hw_device_ctx)` |
| `av_buffer_ref()` で codec_ctx に渡した分 | `avcodec_free_context()` が自動解放 |
| `av_hwframe_transfer_data()` の出力 frame | `av_frame_free()` で解放 |

---

## 10. メモリ管理チートシート 🧠

**FFmpeg は GC なし。手動管理必須！** ミスるとメモリリーク → クラッシュ直行 💀

### 確保 → 解放の対応表

| 確保 | 解放 | 備考 |
|:--|:--|:--|
| `avformat_open_input()` | `avformat_close_input(&fmt_ctx)` | 内部ストリームも連鎖解放 |
| `avcodec_alloc_context3()` | `avcodec_free_context(&codec_ctx)` | |
| `sws_getContext()` | `sws_freeContext(sws_ctx)` | |
| `av_packet_alloc()` | `av_packet_free(&pkt)` | 内部で unref もやる |
| `av_frame_alloc()` | `av_frame_free(&frame)` | 内部で unref もやる |
| `av_malloc()` | `av_freep(&ptr)` | ポインタを NULL にしてくれて安全 |

### ループ内の鉄則

```c
while (av_read_frame(fmt_ctx, pkt) >= 0) {
    // ... デコード処理 ...
    av_packet_unref(pkt);   // ← 毎回やる！忘れると数秒でGB単位のリーク 💥
}

// フレーム側も同様
while (avcodec_receive_frame(codec_ctx, frame) >= 0) {
    // ... フレーム処理 ...
    av_frame_unref(frame);  // ← 毎回やる！
}
```

### 入力バッファのパディング

生ストリーム（NAL ユニット等）を手動パースする場合：

```c
// バッファサイズ = データ長 + FF_INPUT_BUFFER_PADDING_SIZE(32バイト)
// パディング領域はゼロクリア必須！
// → SIMD 最適化がバッファ末尾を超えて読む可能性があるため
```

---

## 11. CLI vs DLL（API）比較

| 観点 | CLI（ffmpeg.exe 呼び出し） | DLL（API 直接呼び出し） |
|:--|:--|:--|
| **導入の簡単さ** | ✅ 数行で動く | ❌ C 言語レベルの理解が必要 |
| **エラーハンドリング** | ❌ stderr をテキストパースするしかない | ✅ 戻り値で型安全に処理 |
| **パフォーマンス** | ❌ 毎回プロセス生成のオーバーヘッド | ✅ インメモリ処理・ゼロコピー可能 |
| **フレーム単位の制御** | ❌ 不可 | ✅ パケット/フレーム単位で自由自在 |
| **メモリ管理** | ✅ OS がプロセスごとに回収 | ❌ 手動管理必須（リーク注意） |
| **バッチ処理** | ❌ 大量ファイルで CPU 逼迫 | ✅ 1プロセス内で効率的に回せる |

> 💡 プロトタイプ → CLI、プロダクション → DLL が鉄板パターン！

---

## 参考リンク

| # | ソース |
|:--|:--|
| 1 | [FFmpeg 公式](https://www.ffmpeg.org/) |
| 2 | [FFmpeg.AutoGen (NuGet)](https://www.nuget.org/packages/FFmpeg.AutoGen/) |
| 3 | [FFmpeg.AutoGen (GitHub)](https://github.com/Ruslan-B/FFmpeg.AutoGen) |
| 4 | [ffmpeg-libav-tutorial](https://github.com/leandromoreira/ffmpeg-libav-tutorial) |
| 5 | [gyan.dev Builds](https://www.gyan.dev/ffmpeg/builds/) |
| 6 | [Send/Receive API ドキュメント](https://ffmpeg.org/doxygen/4.1/group__lavc__encdec.html) |
| 7 | [メモリ管理チートシート (Reddit)](https://www.reddit.com/r/cprogramming/comments/18m5h5x/) |
| 8 | [AVFrame をJPEG保存 (Gist)](https://gist.github.com/2309baf41e9546b7d757a6477c236418) |
| 9 | [HW デコーダ+フィルタ+エンコーダ組み合わせ (ニコラボ)](https://nico-lab.net/combine_hw_decoder_filter_encoder_with_ffmpeg/) |
| 10 | [HWAccelIntro – FFmpeg Wiki](https://trac.ffmpeg.org/wiki/HWAccelIntro) |
| 11 | [hw_decode.c サンプル (FFmpeg)](https://github.com/FFmpeg/FFmpeg/blob/master/doc/examples/hw_decode.c) |
| 12 | [hwcontext_d3d11va.c (FFmpeg)](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hwcontext_d3d11va.c) |
