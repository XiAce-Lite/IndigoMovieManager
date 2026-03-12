# FFmpeg ドキュメント案内

このフォルダは、FFmpeg / ffprobe / キーフレーム抽出まわりの調査メモ置き場です。
内容は主に実装検討向けです。

## AI / 実装向けの入口

- [video_basics_2026-03-01.md](video_basics_2026-03-01.md)
  - 動画の基本要素を整理した資料です。
- [thumbnail_extraction_2026-03-01.md](thumbnail_extraction_2026-03-01.md)
  - サムネイル抽出の基本方針です。
- [ffprobe_info_2026-03-01.md](ffprobe_info_2026-03-01.md)
  - ffprobeで取れる情報の整理です。
- [ffmpeg_dll_video_keyframe_2026-03-01.md](ffmpeg_dll_video_keyframe_2026-03-01.md)
  - DLL利用前提のキーフレーム調査です。
- [keyframe_extraction_formats_2026-03-01.md](keyframe_extraction_formats_2026-03-01.md)
  - 形式差分の確認です。
- [keyframe_extraction_dll_formats_2026-03-01.md](keyframe_extraction_dll_formats_2026-03-01.md)
  - DLL経路の形式差分メモです。

## 使い分け

- 全体方針を見たい時は `Docs/FFmpeg_Guidelines.md`
- 実装条件を掘る時はこのフォルダ配下を読む
