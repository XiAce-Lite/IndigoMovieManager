# 文字化けインシデント報告（2026-02-23）

## 1. 事象
- `../DB/SQLite.cs` の日本語コメントおよび一部文字列が文字化けした。
- 影響は主にコメントで、実行ロジックへの影響は限定的だったが、可読性と保守性が大きく低下した。

## 2. 原因
### 確認できた事実
- `HEAD:SQLite.cs` には文字化けがない。
- 作業中の `../DB/SQLite.cs` には文字化けが多数存在した。
- 文字化けは「コメントや文字列」に集中していた。

### 再現結果
- BOMなしUTF-8ファイルを Windows PowerShell の既定エンコーディング（`Default`）で読み、UTF-8で保存すると同種の文字化けが再現した。

### 根因（高確度）
- BOMなしUTF-8テキストを、明示エンコーディング指定なしで読み書きしたことによるデコード/エンコード不整合。

## 3. 今回の対処
- `../DB/SQLite.cs` の文字化けコメントを除去。
- 文字化けしていた例外メッセージを正常化。
- UTF-8(BOMなし)+LFルールを `../.editorconfig` と `../.gitattributes` に明文化。
- 文字化け検知スクリプト `../scripts/Check-Mojibake.ps1` を追加。

## 4. 再発防止策
1. PowerShellでテキストを扱う際はエンコーディングを必ず明示する。
2. 保存時は UTF-8(BOMなし) を明示する（`../.editorconfig` 準拠）。
3. 変更前後で `../scripts/Check-Mojibake.ps1` を実行する。
4. 大きなファイル再生成時は、必ず差分レビューで日本語コメントの健全性を確認する。

## 5. 推奨コマンド
```powershell
# 文字化け検知
powershell -ExecutionPolicy Bypass -File .\scripts\Check-Mojibake.ps1

# 安全に UTF-8(BOMなし) で保存
$enc = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText("path\\to\\file.cs", $text, $enc)
```
