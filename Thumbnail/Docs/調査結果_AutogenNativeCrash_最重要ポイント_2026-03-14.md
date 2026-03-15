# 調査結果 Autogen Native Crash 最重要ポイント 2026-03-14

## 目的

`autogen` を主力のまま維持しつつ、起動直後やサムネ再作成タブ切替で発生していた native crash の要点を、次回すぐ読める形で短く固定する。

## 最重要ポイント

### 1. 配列長の問題が最重要

- `sws_scale` へ渡していた `dstData` / `dstLinesize` は、FFmpeg 側が複数プレーン前提で扱う API である。
- C# 側で長さ 1 の配列を渡す経路は危険で、native 側が境界外を読みに行くと CLR ヒープ破壊へ繋がる。
- 途中で 4 要素配列へ広げても、managed 配列マーシャリングを跨ぐ構造自体が依然として不安定要因だった。
- 最終的には「配列長を合わせる」だけで止めず、`sws_scale` へ managed 配列を直接渡す経路そのものをやめた。

### 2. 最終解は `AVFrame` 出力へ寄せること

- 最終実装では `ConvertFrameToBitmap` を `sws_scale_frame + FFmpeg 管理の AVFrame` ベースへ置き換えた。
- これにより、C# の `byte*[]` / `int[]` を FFmpeg 境界へ渡す必要がなくなった。
- 出力先の確保は `av_frame_get_buffer` に任せ、変換後に `Bitmap` へ行コピーする形へ揃えた。
- 要するに、危険だったのは「変換そのもの」より「変換先の渡し方」だった。

### 3. `pix_fmt` は codec context ではなく実フレームを信じる

- `sws_getContext` を `pCodecContext->pix_fmt` ベースで先に作ると、実際にデコードされた `AVFrame` の `format` とズレることがある。
- このズレは `swscale-8.dll` の `0xc0000005` を引き起こす候補だった。
- 対策として、デコードできた各フレームの `width / height / format` から `SwsContext` を作り直すようにした。
- 原則は「推測したメタデータではなく、返ってきた実フレームを信じる」である。

### 4. `autogen` は降ろさず、危険帯だけ直列化する

- 今回の方針は `autogen` を既定ルートから外すことではない。
- `IMM_THUMB_AUTOGEN_ENGINE_PARALLEL` と `IMM_THUMB_AUTOGEN_NATIVE_PARALLEL` の既定値を 1 とし、危険帯だけ安全側へ倒した。
- `CreateAsync` / `CreateBookmarkAsync` 全体を直列化しつつ、`swscale` 周辺も小さく絞って制御している。
- 速度思想は維持し、暴れる場所だけ首輪を付けるのが本筋だった。

### 5. タブ切替は原因そのものではなく、負荷の引き金だった

- タブ切替直後に `tab-error-placeholder` を大量救済投入すると、`autogen` へ一気に負荷が寄る。
- その状態で native 側の不整合が噴くと、「タブ切替で落ちた」ように見える。
- 実際の本体原因は WPF のタブ制御ではなく、FFmpeg native 側の破壊だった。
- そのため、タブ切替時の自動救済投入は 64 件に制限した。

### 6. クラッシュ署名の変化は「原因が移った」のではなく「壊れ方が見えただけ」

- 初期は `swscale-8.dll / 0xc0000005` が見えていた。
- 途中では `coreclr.dll` や `ntdll.dll / 0xc0000374` へ変わった。
- これは修正で別バグを作ったというより、native 側で先にヒープを壊し、最後に CLR や ntdll が巻き込まれて落ちる典型形と読むべきだった。
- 「最後に落ちた DLL」だけで犯人を決めないことが重要。

## 実装の着地点

- `autogen` を既定の主力のまま維持する。
- `sws` 変換は `AVFrame` 出力へ寄せ、managed 配列 handoff をやめる。
- `SwsContext` は実フレームから再生成する。
- `autogen` 実行全体は既定で 1 本ずつ通す。
- タブ切替時の自動救済は 64 件までに抑える。

## 今回の確認結果

- `dotnet build IndigoMovieManager_fork.csproj -c Debug -p:Platform=x64` 成功
- `AutogenRegressionTests` / `AutogenExecutionFlowTests` 12 件成功
- 修正後の短時間起動確認では、新規の `Application Error` / `Windows Error Reporting` と新規 dmp 生成なし
- `debug-runtime.log` には `autogen safety config` が出力され、現行の安全弁をログから読める

## 次回見る場所

- まず `debug-runtime.log` の `autogen safety config`
- 次に `engine selected: id=autogen`
- それでも落ちた場合だけ、新しい dmp と `Application Error` / `WER` の時刻を合わせて追う
