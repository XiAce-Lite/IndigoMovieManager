# 🔍 ffprobeで動画の情報を丸裸にするよ！

大量の動画を処理する時に、解像度やコーデックの情報だけを一瞬で抜き出す方法を紹介するね✨
FFmpegに同梱されてる「ffprobe」を使えば爆速で解析できるよ！

## 🧹 余計なログを消し飛ばす必須オプション
デフォルトだとFFmpegのバージョン情報とかエラーがいっぱい出て邪魔だから、まずはこれを付けよう！
* `-hide_banner` - バージョン情報なんか見なくてヨシ！
* `-v error` - エラーだけ出力！これで結果がキレイになるよ🥰

## 🎯 欲しい情報だけをピンポイントで狙い撃ち
動画全部の情報を出すと重いから、`-show_entries`で必要なとこだけ引っこ抜くのが最強のアプローチ！🔥

* **解像度:** `stream=width,height`
* **ビットレート:** `stream=bit_rate`
* **コーデック情報:** `stream=codec_name,profile`
* **長さ（尺）:** `format=duration`

## 📤 使いやすい形式で出力しよう
後続のプログラム（PythonやC#）で使いやすいようにフォーマットを指定できるよ！

1. **JSON (`-print_format json`)**
   開発でおなじみ！プログラムで扱うなら絶対これだね💡
2. **Flat (`-print_format flat`)**
   `stream.0.width=1920` みたいな形で出るから、正規表現でサクッと拾う時に便利！
3. **CSV (`-print_format csv`)**
   ExcelやDBにブチ込むならこれ一択！

### 💻 最強のコマンド例
JSON形式で最初のビデオストリームの解像度とコーデックを抜くならこんな感じ！どや！✨
```bash
ffprobe -v error -hide_banner -print_format json -select_streams v:0 -show_entries stream=width,height,codec_name input.mp4
```
これで動画の情報取得は完璧だね！🚀
