# IndigoMovieManager Development Setup

最終更新日: 2026-03-28

## 1. この文書の役割

この文書は、初めて開発環境を作る人向けの入口です。

- 何を入れればよいか
- どの順番で試せばよいか
- 最初に何ができれば成功か
- 詰まった時にどこを見るか

を、最短で確認できるようにしています。

## 2. まず揃えるもの

必須に近いもの

- Windows
- .NET 8 SDK
- Visual Studio 2026
- PowerShell 7.x
  - `pwsh` で `$PSVersionTable.PSVersion` を実行し、`7.x` 以降であることを確認します
  - ない場合は Microsoft Store から入れるのが楽です
  - `powershell` の 5.x は使いません(事故要因)

あるとよいもの

- SQLite command-line tools for Windows x64 (`sqlite3.exe`, `sqldiff.exe` など)
  - MainDB、QueueDB、FailureDb の中身確認や差分比較に使います
  - `sqlite3.exe` と `sqldiff.exe` があれば、普段の確認や切り分けはかなり進めやすくなります
  - [SQLite公式ダウンロード](https://www.sqlite.org/download.html)
- Everything
  - 監視や候補抽出の高速化で使います
  - なくても一部はフォールバックしますが、普段の確認は入っている方が分かりやすいです
  - [Everything公式](https://www.voidtools.com/)
- ffmpeg (`ffmpeg.exe`, `ffplay.exe`, `ffprobe.exe`)
  - 動画の再生確認、メタ情報確認、手動切り分けに使います
  - `ffprobe.exe` で情報確認、`ffplay.exe` で再生確認、`ffmpeg.exe` で変換や抽出確認ができます
  - [FFmpeg公式ダウンロード](https://ffmpeg.org/download.html)

## 3. このプロジェクトの前提

- アプリ本体は WPF の `net8.0-windows`
- プラットフォームは `x64`
- 文字コードと改行は `UTF-8 (BOMなし) + LF`
- PowerShell は `pwsh` を使う
  - PowerShell 5.x は UTF-16 化の事故要因になりやすいため避ける

## 4. 最短の開始手順

このコードベースに新しく入る人は、まずこの順番で進めてください。

1. `Visual Studio 2026` と `.NET 8 SDK` を入れる
2. `pwsh` でこのリポジトリを開く
3. 依存関係を復元する
4. `Debug | x64` でビルドする
5. アプリを起動する
6. 小さいテスト用フォルダで動作確認する

## 5. コマンド例

### 5.1 依存関係の復元

```powershell
dotnet restore
```

### 5.2 ソリューションのビルド

```powershell
dotnet build IndigoMovieManager.sln -c Debug -p:Platform=x64
```

### 5.3 アプリ本体の起動

```powershell
dotnet run --project IndigoMovieManager.csproj -c Debug
```

### 5.4 MSBuild を直接使う場合

SDK 形式ですが、Visual Studio 側の MSBuild で確認したい時は次でも構いません。

```powershell
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" IndigoMovieManager.sln /p:Configuration=Debug /p:Platform=x64
```

## 6. 最初に成功させること

いきなり大規模データで試さず、次の 4 つが通れば最初の環境確認として十分です。

1. アプリが起動する
2. 新しい `*.wb` を作成するか、既存 DB を開ける
3. 小さいフォルダを監視対象に追加できる
4. 動画 1 本に対してサムネイルが 1 件作られる

## 7. 初回確認は小さく始める

最初の確認では、大量動画フォルダは避けてください。

- 数本だけ入ったフォルダを使う
- まず一覧表示が出るかを見る
- 次に監視登録を試す
- 最後にサムネイル生成を確認する

理由

- 問題が起きた時に切り分けしやすい
- Queue や FailureDb が大量に動く前に基本導線を見られる
- UI の遅さと環境不備を混同しにくい

## 8. サムネイル周りの前提

サムネイル生成では、次の層が関わります。

- `IndigoMovieManager` 本体
- `IndigoMovieManager.Thumbnail.Engine`
- `IndigoMovieManager.Thumbnail.Queue`
- `IndigoMovieManager.Thumbnail.RescueWorker`

確認時に意識すること

- 通常生成は QueueDB 経由で流れる
- 失敗は FailureDb に記録される
- 重い失敗は RescueWorker 側で救済される

## 9. ログと保存先

調査時によく見る場所

- ログ
  - `%LOCALAPPDATA%\IndigoMovieManager_fork_workthree\logs\`
- QueueDB
  - `%LOCALAPPDATA%\{AppName}\QueueDb\`
- FailureDb
  - `%LOCALAPPDATA%\{AppName}\FailureDb\`

`{AppName}` はブランド設定で変わることがあります。

## 10. よくある詰まり方

### 10.1 ビルドが通らない

確認順

1. `x64` でビルドしているか
2. `pwsh` を使っているか
3. `dotnet restore` を済ませたか
4. Visual Studio のワークロード不足がないか

### 10.2 起動はするが動作が不安定

確認順

1. 小さい DB / 小さいフォルダで再現するか
2. ログに例外が出ていないか
3. QueueDB / FailureDb が大量に溜まっていないか

### 10.3 監視が期待通り動かない

確認順

1. まず小さいローカルフォルダで試す
2. Everything 依存の有無を切り分ける
3. `Watcher` 系資料を読む

### 10.4 サムネイルが出ない

確認順

1. QueueDB にジョブが積まれているか
2. FailureDb に失敗記録が出ていないか
3. RescueWorker 側のログに痕跡があるか

## 11. 次に読む文書

環境構築が済んだら、次はこの順がおすすめです。

1. **[ProjectOverview_2026-03-29.md](ProjectOverview_2026-03-29.md)**
2. **[ProjectFilesAndFolders_2026-04-01.md](ProjectFilesAndFolders_2026-04-01.md)**
3. **[Architecture_2026-02-28.md](Architecture_2026-02-28.md)**
4. **[DatabaseSpec_2026-02-28.md](DatabaseSpec_2026-02-28.md)**

## 12. 実装に入る前の注意

- いきなり `MainWindow` 全部を追わない
- まず触る領域を決める
- 読み取りと書き込みの境界を意識する
- サムネイル周りでは `Factory + Interface + Args` の入口を守る

最初の 1 回で全部理解しようとせず、

- 起動
- 一覧
- 監視
- サムネイル

の順で 1 本ずつ導線を追うのがおすすめです。
