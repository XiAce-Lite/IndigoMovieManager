# ToDo

## 完了（絵文字サムネイル対策）
- OpenCV/ffmpeg の入力パスを段階試行化（Raw -> ShortPath -> Alias -> Copy）。
- Raw/ShortPath 失敗時のみ Alias（ジャンクション/ハードリンク）を作成するよう最適化。
- 3GB超コピーは Deferred 化し、通常キュー完了後にユーザー確認するフローを実装。
- 一時資源（ジャンクション/ハードリンク/コピー）の `finally` クリーンアップを実装。
- ジャンクション削除時に再解析ポイントを辿らない安全削除へ変更。
- `ImWrite` 直接保存失敗時の一時ASCII保存 fallback を強化。
- `Encoding 932` 対策として `Encoding.RegisterProvider` を追加。
- 関連ドキュメント更新:
  - `Thumbnail/Docs/history/EmojiPathMitigation_絵文字問題 症状と対策.md`
  - `Thumbnail/Docs/history/EmojiPathMitigationDetailDesign.md`

## 未完了
- フォルダ内に同じ動画が別名である場合にサムネイルが1つしか作られない。
- View側の絵文字化け（ANSI）が残る。
- DB切替またぎで既に `Processing` に入っている旧ジョブを切る。
  - 独立ToDo: [ToDo_DB切替_旧Processingジョブ切り離し_2026-03-15.md](ToDo_DB切替_旧Processingジョブ切り離し_2026-03-15.md)

## 確認待ち
- 実データで `Cannot marshal: Encountered unmappable character.` が再発しないことを継続確認。
