# 開発セットアップ

## 1. 前提
- OS: Windows
- SDK: .NET 8 SDK
- IDE: Visual Studio 2022 推奨

## 2. 依存パッケージ（抜粋）
- `System.Data.SQLite`
- `OpenCvSharp4.Windows`
- `MaterialDesignThemes`
- `Dirkster.AvalonDock`
- `Notification.Wpf`

## 3. 復元とビルド
プロジェクトは現在 SDK 形式（`<Project Sdk="Microsoft.NET.Sdk">`）です。

```powershell
dotnet restore
dotnet build IndigoMovieManager.sln -c Debug
```

Visual StudioのMSBuildを使う場合:

```powershell
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager.sln /p:Configuration=Debug /p:Platform="Any CPU"
```

## 4. 実行
```powershell
dotnet run --project IndigoMovieManager.csproj
```

## 5. 初回確認ポイント
1. DBを新規作成できること
2. 小さいフォルダを監視登録し、動画追加で一覧へ反映されること
3. サムネイルを1件作成できること
4. 検索・ソートが動作すること

## 6. 補足
- 既定の共通設定は `Properties.Settings` に保存
- DB依存の個別設定は `system` テーブル（`thum`, `bookmark`, `keepHistory`, `playerPrg`, `playerParam`）
- いきなり大量ファイルで試すより、小規模フォルダでの確認が安全

