# AGENTS.md

## 基本ルール
- 愛を持って接する
- 常に日本語で回答する
- 簡潔に記述し、コード・構成はシンプルに保つ
- コードには日本語で流れ重視のコメントを記載する
- MVVMを基本とするが開発者がコードを掴みやすいようにする事の方が重要
- 文字コードと改行は「UTF-8 (BOMなし) + LF」を使用する
- チャットコメントのリンクは必ず生パスで記載する（Markdownリンク形式は使わない）

## ドキュメント・アーティファクト
- ドキュメントは日本語で作成する
- ドキュメントは適切な名前を付け、関連コードと同じフォルダに保存する

## 開発方針（VS2026）
- このプロジェクトは Visual Studio 2026 前提で開発する
- VS2017 向けの旧ルール（非SDK形式前提）はこのプロジェクトでは適用しない

## ビルド・実行方針（.NET優先）
- 既定のビルドは `dotnet` を使用する
- 基本コマンド:
  - `dotnet restore`
  - `dotnet build IndigoMovieManager_fork.sln -c Debug`
  - `dotnet run --project IndigoMovieManager_fork.csproj`
- 例外:
  - COM 参照などで `dotnet build` が失敗する場合は、Visual Studio 付属の MSBuild を使用してよい
  - 例: `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager_fork.sln /p:Configuration=Debug /p:Platform="Any CPU"`

## ビルド失敗時ルール
- ビルド失敗時は原因を先に特定してから再試行する
- ロックされている場合はユーザーが実行中の可能性を考慮し確認する
- フォーマット起因が疑わしい場合は `CSharpier` を実行して整形する
- 無意味な再試行はしない（最大3回まで）

## スキル
- 必要に応じて `.agent\skills` を参照する

## 実行環境ルール
- PowerShellはUTF-16変換を防ぐため7.x.xを使用する

## プロジェクトルール
- WhiteBrowserの互換プログラムとして開発する
- WhiteBrowserのDB(*.wb)は変更しない

