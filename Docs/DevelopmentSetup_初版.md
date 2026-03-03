# 💻 最高の開発セットアップ！ 💻

やっほー！IndigoMovieManagerの開発を始めるための、最初の儀式を教えるよ！✨
ここをサクッと終わらせて、爆速コーディングの世界へ飛び込もうぜ！🚀

## 1. ⚙️ まずはここから！（前提条件）
- **OS**: Windows（そりゃそうだ！）
- **SDK**: .NET 8 SDK（最新のパワーを喰らえ！）
- **IDE**: Visual Studio 2022 **絶対推奨**！（2026でももちろんOK！VSしか勝たん！）💖

## 2. 📦 頼れる仲間たち（依存パッケージ抜粋）
我々の戦いを支える強力なライブラリ陣だ！
- `System.Data.SQLite`：頼れるDBの相棒！
- `OpenCvSharp4.Windows`：画像処理はおまかせ！
- `MaterialDesignThemes`：UIを今どきにカッコよくする魔法の杖！🪄
- `Dirkster.AvalonDock`：画面レイアウト自在の術！
- `Notification.Wpf`：イケてるトースト通知を出す担当！🍞

## 3. 🔨 復元とビルド（魔法の呪文）
プロジェクトはスッキリ現代風のSDK形式（`<Project Sdk="Microsoft.NET.Sdk">`）だぜ！

**ターミナル派の君へ:**
```powershell
dotnet restore
dotnet build IndigoMovieManager_fork.sln -c Debug
```

**VSのMSBuildを直接叩きたいガチ勢の君へ:**
```powershell
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager_fork.sln /p:Configuration=Debug /p:Platform=x64
```
※パスが自分の環境と合ってるか確認してね！😉

## 4. 🏃‍♂️💨 実行！（いざ出陣！）
```powershell
dotnet run --project IndigoMovieManager_fork.csproj
```
このコマンド一つで、君のモニターに最強のアプリが立ち上がる！！

## 5. ✅ 動いた！？初回クリアチェックポイント！
起動したら、まずはこの4つのミッションをクリアできるか試してみよう！
1. **まっさらなDB** を新規作成できた！🆕
2. 試しに小さいフォルダを監視登録して、動画を追加したら**一覧にシュバッと反映**された！👀
3. サムネイルが**バッチリ1件**作成できた！🖼️
4. 検索やソートが**スイスイ動く**！🔍

## 6. 💡 開発者へのワンポイントアドバイス！
- アプリの基本設定（ウィンドウサイズとか）は `Properties.Settings` に記憶してるよ！
- DBごとに違う設定（サムネの場所とか、プレイヤー設定とか）はDB内の `system` テーブルが生息地だ！
- **🚨 罠注意 🚨**: いきなり数千動画があるフォルダで試すと、サムネ生成祭りが始まってPCが悲鳴を上げるから、まずは小さいテストフォルダで遊ぶのが安全だぜ！😎👍
