# 💻 最高の開発セットアップ！ (2026-02-28 最新版) 💻

やっほー！IndigoMovieManagerの開発を始めるための、**最新の**儀式を教えるよ！✨
これをサクッと終わらせて、爆速コーディングの世界へ飛び込もうぜ！🚀

## 1. ⚙️ まずはここから！（最強の前提条件）
- **OS**: Windows（そりゃそうだ！）
- **SDK**: .NET 8 SDK（最新のパワーを喰らえ！）
- **IDE**: Visual Studio 2026 推奨！（2022でももちろんOK！VSしか勝たん！）💖
- **🚨 必須シェル**: **PowerShell 7.x 以降 (Microsoft Storeから入れろ！)**
  - 古いPowerShell 5.xは、様子を見ただけでファイルをUTF-16に変異させる極悪仕様だから絶対使うな！💀
- **🔍 必須ツール**: **Everything (Voidtools)**
  - 最新の超速ファイル監視をフルパワーで動かすには、ローカルにEverythingが常駐している必要があるぞ！

## 2. 📦 頼れる仲間たち（依存パッケージ抜粋）
我々の戦いを支える強力なライブラリ陣だ！
- `System.Data.SQLite.Core`：頼れるDBの相棒！
- `OpenCvSharp4.Windows`：メタ情報等を引き抜くスパイ！
- `FFMediaToolkit` & `FFmpeg.AutoGen`：動画からフレームをぶっこ抜く爆速サムネ職人！🎬
  - **重要**: 実行には `ffmpeg-n7.1.1-xxxx-shared-lgpl` のDLL群（`v7.1.1`固定！）が必要だ！忘れるなよ！
- `MaterialDesignThemes`：UIを今どきにカッコよくする魔法の杖！🪄
- `Dirkster.AvalonDock`：画面レイアウト自在の術！
- `Notification.Wpf`：イケてるトースト通知を出す担当！🍞

## 3. 🔨 復元とビルド（魔法の呪文）
プロジェクトはスッキリ現代風のSDK形式だぜ！

**ターミナル派の君へ:**
```powershell
dotnet restore
dotnet build IndigoMovieManager_fork.sln -c Debug
```

**VSのMSBuildを直接叩きたいガチ勢の君へ:**
```powershell
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager_fork.sln /p:Configuration=Debug /p:Platform="Any CPU"
```
※パスが自分の環境と合ってるか確認してね！😉
※もしビルドが通らない（警告やエラーが出る）時は、焦らずまず **`/csharpier-format` (CSharpierによるフォーマット)** の儀式を試すんだ！✨

## 4. 🏃‍♂️💨 実行！（いざ出陣！）
```powershell
dotnet run --project IndigoMovieManager_fork.csproj
```
このコマンド一つで、君のモニターに最強のアプリが立ち上がる！！

## 5. ✅ 動いた！？初回クリアチェックポイント！
起動したら、まずはこのミッションをクリアできるか試してみよう！
1. **まっさらなDB** を新規作成できた！🆕
2. 試しに小さいフォルダを監視登録（`AUTO`推奨！）して、動画を追加したら**一覧にシュバッと反映**された！👀
3. サムネイルが**バッチリ1件**作成できた！🖼️
4. 検索やソートが**スイスイ動く**！🔍

## 6. 💡 開発者へのワンポイントアドバイス！（2026年最新の教訓）
- アプリの基本設定（ウィンドウサイズとか）は `Properties.Settings` に記憶してるよ！
- DBごとに違う設定（サムネの場所とか、プレイヤー設定とか）はDB内の `system` テーブルが生息地だ！
- サムネイル生成は今や **「非同期DBキュー（`thumb_queue`）」** で管理されていて、バックグラウンドのプロセス（ワーカー）がゴリゴリ回している！UIは絶対に固まらないぜ！💪
- **🚨 罠注意 🚨**: いきなり数千動画があるフォルダで試すと、爆速でサムネ生成祭りが始まってPCが悲鳴を上げるから、まずは小さいテストフォルダで遊ぶのが安全だぜ！😎👍
