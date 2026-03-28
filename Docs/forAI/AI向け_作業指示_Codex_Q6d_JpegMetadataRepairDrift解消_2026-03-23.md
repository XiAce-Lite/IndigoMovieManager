# AI向け 作業指示 Codex Q6d JpegMetadataRepairDrift解消 2026-03-23

最終更新日: 2026-03-23

変更概要:
- `ThumbnailJpegMetadataWriterTests` の metadata repair ケースは、旧メタ不一致 jpg に正しい WB 互換メタを再追記しても `CaptureSeconds` が旧値のまま読まれて落ちていた
- source 調査では `WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(...)` が、末尾の最新メタではなく JPEG 終端直後の最初のメタ列を読んでいる疑いが強い
- main 側同一 2 ファイルには null policy 変更や別テスト削除が混ざっているため、Q6d は clean worktree で repair ケースだけに閉じる

## 1. 目的

- `jpeg metadata repair drift` を、現行 WB 互換メタ serializer 契約に沿って解消する
- null `thumbInfo` policy や incomplete jpeg delete policy の別論点を混ぜない

## 2. 対象

- `src/IndigoMovieManager.Thumbnail.Engine/Compatibility/WhiteBrowserThumbInfoSerializer.cs`
- `Tests/IndigoMovieManager_fork.Tests/ThumbnailJpegMetadataWriterTests.cs`
- 必要なら参照
  - `src/IndigoMovieManager.Thumbnail.Engine/ThumbnailJpegMetadataWriter.cs`

## 3. 現状の根拠

- failing test 棚卸し
  - `ThumbnailJpegMetadataWriterTests.TryEnsureThumbInfoMetadata_既存メタが不一致でも再追記で修復後はサイズ増加が止まる`
  - failure は
    - metadata repair `False`
    - stable `False`
    - `CaptureSeconds expected 1, actual 9`
- source 事実
  - `ThumbnailJpegMetadataWriter.TryEnsureThumbInfoMetadata(...)`
    - 期待 spec と不一致なら `WhiteBrowserThumbInfoSerializer.AppendToJpeg(...)` を再実行する
  - `WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(...)`
    - `infoBuffer` は末尾 `60` byte から読む
    - しかし `CaptureSeconds` は `FFD9` 直後に最初に見つけたメタ列から読む
- したがって
  - `ThumbWidth/Height` は新しい末尾 info を読む
  - `CaptureSeconds` は古い先頭メタを読む
  - その結果、repair 後も spec 不一致のままになり得る

## 4. 守ること

1. Q6d は repair drift だけに閉じる
2. null `thumbInfo` policy 変更を混ぜない
3. `TryDeleteIncompleteJpeg(...)` の削除や復元を混ぜない
4. main 側 dirty を正本扱いせず、clean worktree で最小差分を作る

## 5. 着地イメージ

- 第一候補
  - `WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(...)` を、末尾の最新メタ列へ揃えて読む
  - repair 後は `CaptureSeconds == [1]` を返す
- テスト
  - repair ケースを復元または追加する
  - `lengthAfterStable == lengthAfterRepair` を維持する

## 6. 検証

- `dotnet test Tests/IndigoMovieManager_fork.Tests/IndigoMovieManager_fork.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ThumbnailJpegMetadataWriterTests"`
- `git diff --check`

## 7. 禁止

- Q6d を口実に null `thumbInfo` 契約変更を入れること
- unrelated change を `ThumbnailJpegMetadataWriter.cs` へ広げること
- 失敗テストを消すだけで閉じること
